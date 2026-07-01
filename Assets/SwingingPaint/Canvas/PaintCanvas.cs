using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwingingPaint.Canvas
{
    /// <summary>
    /// Canvas that records paint impacts as a believable acrylic painting. Preserves the strong pigment
    /// model from the original project — subtractive CMY mixing, baked multi-octave canvas grain,
    /// thickness accumulation, gravity-biased wet capillary spreading, drying, and oriented elliptical
    /// brush strokes that elongate along the impact velocity (this is what yields the brush-stroke look).
    ///
    /// Hardened for the unified project:
    ///   • Decoupled — no FindObjectOfType; brush scaling is injected via <see cref="ArmLength"/> /
    ///     <see cref="SwingAmplitude"/>, set by the manager from the live pendulum.
    ///   • Color32 pixel buffers (4× less memory than Color, fast SetPixels32 upload path).
    ///   • Dirty-rectangle upload — only the touched sub-region is copied each frame, with a reused
    ///     buffer so there is no per-frame GC allocation.
    ///   • Public API takes a <see cref="PaintImpact"/> value type instead of a heap class.
    /// </summary>
    public sealed class PaintCanvas : MonoBehaviour
    {
        // ── Dimensions ──────────────────────────────────────────────
        [Header("Canvas dimensions")]
        public float CanvasWidth = 2f;
        public float CanvasHeight = 2f;
        public int TextureRes = 1024;

        // ── Surface ─────────────────────────────────────────────────
        [Header("Surface")]
        [Range(0f, 1f)]
        [Tooltip("0 = glass (wide spread), 1 = canvas (sharp edges).")]
        public float AbsorptionRate = 0.85f;

        [Header("Orientation")]
        [Tooltip("Local normal of the painted face. Plane = up, camera-facing Quad = forward.")]
        public Vector3 PaintNormalLocal = Vector3.up;
        public bool AutoCalculateTilt = true;
        [Range(0f, 180f)] public float TiltAngle = 0f;

        [Header("Environment")]
        [Range(0f, 1f)]
        [Tooltip("0 = dry (separate colours), 1 = wet (colours merge and run).")]
        public float Humidity = 0.5f;

        [Header("Impact scaling")]
        [Tooltip("Reference mass for the brush-size scaling equation.")]
        public float ReferenceParticleMass = 0.012f;
        public Vector2 MassScaleClamp = new Vector2(0.6f, 2.8f);

        [Header("Collision detection")]
        [Tooltip("Distance threshold from the canvas plane (local units).")]
        public float HitThreshold = 0.05f;

        [Header("Canvas grain")]
        [Range(5f, 80f)]
        [Tooltip("Grain scale — larger = finer, more detailed pattern.")]
        public float GrainScale = 25f;

        // ── Brush coupling (injected by the manager from the pendulum) ──
        [Header("Brush coupling (set by manager)")]
        public float ArmLength = 3f;
        public float SwingAmplitude = 40f;

        // Derived dynamic properties.
        float GrainStrength => Mathf.Clamp01(AbsorptionRate * 0.5f);
        float WetSpreadRate => Mathf.Lerp(0.02f, 0.5f, Humidity);
        float BrushSizeScale => Mathf.Clamp((ArmLength * 0.05f) + (SwingAmplitude * 0.005f), 0.05f, 2f);

        // ── Private state ───────────────────────────────────────────
        Vector2 _uvGravityDir = new Vector2(0, -1);
        Texture2D _paintTex;
        Color32[] _pixels;        // committed canvas
        Color32[] _wetPixels;     // wet layer (drives subtractive mixing)
        float[] _wetTimer;
        float[] _thicknessMap;    // accumulated paint thickness per pixel
        float[] _grainMap;        // baked canvas weave (read-only after Bake)
        Color32[] _uploadBlock;   // reused scratch for dirty-rect upload (no per-frame GC)
        Renderer _renderer;
        int _minBrushRadius;

        // O(wet) drying via a live set of wet pixel indices.
        readonly HashSet<int> _wetIndices = new HashSet<int>();

        // Dirty rectangle (texel bounds touched since the last upload).
        bool _dirty;
        int _dx0, _dy0, _dx1, _dy1;

        // ── Lifecycle ───────────────────────────────────────────────
        void Awake()
        {
            if (_paintTex == null) Initialize();
        }

        /// <summary>(Re)allocate buffers and the texture. Safe to call from the scene builder.</summary>
        public void Initialize()
        {
            _renderer = GetComponent<Renderer>();
            _minBrushRadius = Mathf.Max(4, Mathf.RoundToInt(TextureRes * 0.008f));

            int total = TextureRes * TextureRes;
            _paintTex = new Texture2D(TextureRes, TextureRes, TextureFormat.RGBA32, false);
            _pixels = new Color32[total];
            _wetPixels = new Color32[total];
            _wetTimer = new float[total];
            _thicknessMap = new float[total];
            _grainMap = new float[total];
            _uploadBlock = new Color32[total];

            BakeGrainMap();

            Color32 white = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < total; i++) { _pixels[i] = white; _wetPixels[i] = clear; }

            _paintTex.SetPixels32(_pixels);
            _paintTex.Apply();
            if (_renderer != null) _renderer.material.mainTexture = _paintTex;
            _dirty = false;
        }

        /// <summary>Bake the canvas weave with multi-octave Perlin noise. Ridges absorb less, grooves more.</summary>
        void BakeGrainMap()
        {
            float seed = Random.Range(0f, 500f);
            int total = TextureRes * TextureRes;
            for (int i = 0; i < total; i++)
            {
                float px = i % TextureRes;
                float py = i / TextureRes;
                float u = px * GrainScale / TextureRes + seed;
                float v = py * GrainScale / TextureRes + seed;
                float g = Mathf.PerlinNoise(u, v)
                        + 0.50f * Mathf.PerlinNoise(u * 2.1f, v * 2.1f)
                        + 0.25f * Mathf.PerlinNoise(u * 4.3f, v * 4.3f);
                _grainMap[i] = Mathf.Clamp01(g / 1.75f);
            }
        }

        void LateUpdate()
        {
            UpdateDynamicTilt();
            UpdateDrying();
            UpdateWetSpreading();
            FlushDirty();
        }

        void UpdateDynamicTilt()
        {
            Vector3 n = PaintNormalLocal.normalized;
            Vector3 localUp = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
            Vector3 tangentU = Vector3.Cross(n, localUp).normalized;
            Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

            Vector3 wU = transform.TransformDirection(tangentU);
            Vector3 wV = transform.TransformDirection(tangentV);
            Vector3 wN = transform.TransformDirection(n);

            Vector2 grav = new Vector2(Vector3.Dot(Vector3.down, wU), Vector3.Dot(Vector3.down, wV));
            _uvGravityDir = grav.sqrMagnitude > 0.001f ? grav.normalized : new Vector2(0, -1);

            if (AutoCalculateTilt) TiltAngle = Vector3.Angle(Vector3.up, wN);
        }

        // ── Public API ──────────────────────────────────────────────

        /// <summary>
        /// Test a paint particle against the canvas plane and, on hit, paint it. Returns true if it hit.
        /// Velocity is taken directly from the impact (no dt needed). Stroke orientation is computed in
        /// the canvas local frame (more correct than the original world/local mix).
        /// </summary>
        public bool TryPaint(in PaintImpact impact)
        {
            Vector3 localPos = transform.InverseTransformPoint(impact.WorldPosition);
            Vector3 n = PaintNormalLocal.normalized;
            float planeDist = Vector3.Dot(localPos, n);
            if (Mathf.Abs(planeDist) > HitThreshold) return false;

            Vector3 inPlane = localPos - n * planeDist;
            Vector3 upRef = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
            Vector3 tangentU = Vector3.Cross(n, upRef).normalized;
            Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

            float u = Vector3.Dot(inPlane, tangentU);
            float v = Vector3.Dot(inPlane, tangentV);
            if (Mathf.Abs(u) > CanvasWidth * 0.5f || Mathf.Abs(v) > CanvasHeight * 0.5f) return false;

            Vector2 uv = new Vector2(u / CanvasWidth + 0.5f, v / CanvasHeight + 0.5f);

            // Stroke direction in UV space, computed entirely in the canvas local frame.
            Vector3 localVel = transform.InverseTransformVector(impact.WorldVelocity);
            float speed = localVel.magnitude;
            Vector3 velInPlane = localVel - n * Vector3.Dot(localVel, n);
            Vector2 strokeDir = (velInPlane.sqrMagnitude > 0.001f)
                ? new Vector2(Vector3.Dot(velInPlane, tangentU), Vector3.Dot(velInPlane, tangentV)).normalized
                : Vector2.zero;

            float refMass = Mathf.Max(ReferenceParticleMass, 0.00001f);
            float massSc = Mathf.Clamp(Mathf.Sqrt(impact.Mass / refMass), MassScaleClamp.x, MassScaleClamp.y);

            Paint(uv, impact.Color, speed, impact.Viscosity, massSc, strokeDir);
            return true;
        }

        /// <summary>Reset the canvas to white.</summary>
        public void Clear()
        {
            int total = TextureRes * TextureRes;
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < total; i++)
            {
                _pixels[i] = white;
                _wetPixels[i] = clear;
                _wetTimer[i] = 0f;
                _thicknessMap[i] = 0f;
            }
            _wetIndices.Clear();
            _paintTex.SetPixels32(_pixels);
            _paintTex.Apply();
            _dirty = false;
        }

        public Texture2D Texture => _paintTex;

        /// <summary>Encode the current canvas as PNG bytes (used by the export system).</summary>
        public byte[] EncodePNG() => _paintTex.EncodeToPNG();

        // ── Core painting ───────────────────────────────────────────

        void Paint(Vector2 uv, Color color, float speed, float viscosity, float massScale, Vector2 strokeDir)
        {
            int radius = ComputeRadius(uv, speed, viscosity, massScale);
            Color tinted = SpeedTint(color, speed);
            float strength = Mathf.Clamp(massScale, 0.25f, 3f);
            float aspect = ComputeAspect(speed);

            DrawSpot(uv, radius, tinted, true, strength, strokeDir, aspect);

            // Splatter only on genuinely hard, thin impacts → keeps the strokes as clean lines, not blobs.
            if (speed > 5f && viscosity < 3.0f)
                DrawSplatter(uv, radius, tinted, speed, strength, viscosity);

            if (TiltAngle > 5f && viscosity < 5.0f)
                StartCoroutine(DrawDrip(uv, radius, tinted, strength, viscosity));
        }

        int ComputeRadius(Vector2 uv, float speed, float viscosity, float massScale)
        {
            float speedF = Mathf.Clamp01(speed / 6f);
            float absF = 1f - AbsorptionRate * 0.3f;
            // Brush sized as a fraction of the texture so each mark is clearly VISIBLE on the canvas
            // (viscosity no longer shrinks it — that made the strokes nearly invisible).
            float r = 0.05f * (0.7f + 0.5f * speedF) * absF * Mathf.Clamp(massScale, 0.6f, 2.2f);

            int cx = Mathf.Clamp(Mathf.RoundToInt(uv.x * TextureRes), 0, TextureRes - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(uv.y * TextureRes), 0, TextureRes - 1);
            float g = _grainMap[cy * TextureRes + cx];
            r *= 1f + GrainStrength * (g - 0.5f) * 0.4f;

            return Mathf.Max(8, Mathf.RoundToInt(r * TextureRes * BrushSizeScale));
        }

        float ComputeAspect(float speed)
        {
            float dynamicMaxStrokeAspect = Mathf.Clamp(1f + speed * 0.5f, 1f, 4f);
            return Mathf.Lerp(1f, dynamicMaxStrokeAspect, Mathf.Clamp01(speed / 6f) * 0.7f);
        }

        static Color SpeedTint(Color color, float speed)
        {
            float t = Mathf.Clamp01(speed / 6f) * 0.4f;
            return Color.Lerp(color, Color.Lerp(color, Color.white, 0.25f), t);
        }

        // ── Oriented elliptical spot with canvas grain ──────────────

        void DrawSpot(Vector2 uv, int radius, Color color, bool addNoise, float strength,
                      Vector2 strokeDir = default, float aspect = 1f)
        {
            float noiseOX = Random.Range(0f, 1000f);
            float noiseOY = Random.Range(0f, 1000f);

            int cx = Mathf.RoundToInt(uv.x * TextureRes);
            int cy = Mathf.RoundToInt(uv.y * TextureRes);
            int scanR = Mathf.RoundToInt(radius * (Mathf.Max(aspect, 1f) + 1f) * 1.1f);

            bool hasDir = strokeDir.sqrMagnitude > 0.001f;
            float cosA = hasDir ? strokeDir.x : 1f;
            float sinA = hasDir ? strokeDir.y : 0f;

            for (int px = cx - scanR; px <= cx + scanR; px++)
            for (int py = cy - scanR; py <= cy + scanR; py++)
            {
                if ((uint)px >= (uint)TextureRes || (uint)py >= (uint)TextureRes) continue;

                float dx = px - cx;
                float dy = py - cy;

                // Rotate into the stroke's local frame so the spot elongates along the velocity.
                float lx = dx * cosA + dy * sinA;
                float ly = -dx * sinA + dy * cosA;
                float normDist = Mathf.Sqrt((lx / aspect) * (lx / aspect) + ly * ly);

                float noiseVal = addNoise
                    ? Mathf.PerlinNoise(px * 0.12f + noiseOX, py * 0.12f + noiseOY) * radius * 0.45f
                    : 0f;
                float effRadius = radius + noiseVal;
                if (normDist > effRadius) continue;

                float grain = _grainMap[py * TextureRes + px];
                float grainFactor = Mathf.Lerp(1f, 0.3f, GrainStrength * (1f - grain));

                float t = normDist / (effRadius + 0.5f);
                float smooth = t * t * (3f - 2f * t);
                float alpha = Mathf.Pow(1f - smooth, 0.4f)
                            * Mathf.Lerp(0.7f, 1.35f, Mathf.Clamp01(strength - 1f))
                            * grainFactor;

                BlendPixel(px, py, color, Mathf.Clamp01(alpha));
            }
        }

        void DrawSplatter(Vector2 centerUV, int mainRadius, Color color, float speed, float strength, float viscosity)
        {
            float viscMult = Mathf.Clamp(1.5f / Mathf.Max(viscosity, 0.1f), 0.5f, 2.5f);
            float dynamicSplatterCount = Mathf.Clamp(speed * 2f, 0f, 20f);
            int droplets = Mathf.RoundToInt(dynamicSplatterCount * Mathf.Clamp01(speed / 8f) * viscMult);
            float dynamicSplatterSpread = Mathf.Clamp01((speed * 0.05f) + (1f / Mathf.Max(viscosity, 0.1f)) * 0.1f);

            for (int i = 0; i < droplets; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(1.5f, 3.2f) * mainRadius / (float)TextureRes
                             * dynamicSplatterSpread * viscMult;
                Vector2 dropUV = centerUV + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
                if (dropUV.x < 0f || dropUV.x > 1f || dropUV.y < 0f || dropUV.y > 1f) continue;

                int dropR = Mathf.Max(Mathf.RoundToInt(_minBrushRadius * 0.3f),
                                      Mathf.RoundToInt(mainRadius * Random.Range(0.06f, 0.22f)));
                DrawSpot(dropUV, dropR, color, false, strength * 0.75f);
            }
        }

        IEnumerator DrawDrip(Vector2 startUV, int width, Color color, float strength, float viscosity)
        {
            float viscMult = Mathf.Clamp(1.5f / Mathf.Max(viscosity, 0.1f), 0.3f, 3.0f);
            float tiltFactor = Mathf.Clamp01(TiltAngle / 90f);
            float absorpFactor = 1f - AbsorptionRate;

            int cx = Mathf.Clamp(Mathf.RoundToInt(startUV.x * TextureRes), 0, TextureRes - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(startUV.y * TextureRes), 0, TextureRes - 1);
            float localThick = _thicknessMap[cy * TextureRes + cx];
            float thickBoost = Mathf.Clamp(localThick * 2f, 0f, 1.5f);

            float dripLength = Random.Range(0.04f, 0.2f) * viscMult * tiltFactor * absorpFactor * (1f + thickBoost);
            int steps = Mathf.RoundToInt(dripLength * TextureRes);
            Vector2 current = startUV;

            Vector2 latDir = new Vector2(-_uvGravityDir.y, _uvGravityDir.x);
            Vector2 dripDir = (_uvGravityDir + latDir * Random.Range(-0.15f, 0.15f)).normalized * (1f / TextureRes);

            for (int s = 0; s < steps; s++)
            {
                float t = (float)s / steps;
                int r = Mathf.Max(1, Mathf.RoundToInt(width * Mathf.Lerp(0.55f, 0.08f, t)));
                Color faded = new Color(color.r, color.g, color.b, Mathf.Lerp(0.75f, 0f, t));
                DrawSpot(current, r, faded, false, strength * 0.6f);
                current += dripDir;
                if (current.y < 0f || current.y > 1f) break;
                yield return new WaitForSeconds(0.012f);
            }
        }

        // ── Pixel blending: subtractive CMY + grain + thickness ─────

        void BlendPixel(int x, int y, Color newColor, float alpha)
        {
            int idx = y * TextureRes + x;
            if ((uint)idx >= (uint)_pixels.Length) return;

            Color existing = _pixels[idx];        // implicit Color32 → Color
            bool isWet = _wetTimer[idx] > 0f;

            float grain = _grainMap[idx];
            float grainAbsorp = Mathf.Lerp(1f, 0.6f, GrainStrength * (1f - grain));
            // Opaque coverage: each pass lays solid paint so it reads as real paint, not a faint wash.
            float finalAlpha = Mathf.Clamp01(alpha * newColor.a * grainAbsorp * 1.7f);

            Color solidColor = new Color(newColor.r, newColor.g, newColor.b, 1f);
            // LAYERING (the real pendulum-paint look): a new colour sits ON TOP of wet paint of a
            // different colour, forming distinct concentric rings — it does NOT muddy-blend. Only at
            // high humidity does a little wet-on-wet bleeding mix the boundary.
            Color paintColor = (isWet && Humidity > 0.6f)
                ? SubtractiveMix(solidColor, _wetPixels[idx], (Humidity - 0.6f) * 0.7f)
                : solidColor;

            Color finalColor = Color.Lerp(existing, paintColor, finalAlpha);

            _pixels[idx] = finalColor;            // implicit Color → Color32
            _wetPixels[idx] = finalColor;

            _thicknessMap[idx] = Mathf.Clamp(_thicknessMap[idx] + finalAlpha * 0.5f, 0f, 3f);

            float dryTime = Mathf.Lerp(0.4f, 6f, Humidity) * (1f + _thicknessMap[idx] * 0.3f);
            _wetTimer[idx] = dryTime;
            _wetIndices.Add(idx);

            MarkDirty(x, y);
        }

        /// <summary>
        /// Subtractive pigment mixing in CMY space — red+blue → real purple (not muddy grey as RGB Lerp
        /// would give). This is what makes overlapping colours look like real paint.
        /// </summary>
        static Color SubtractiveMix(Color c1, Color c2, float t)
        {
            Vector3 cmy1 = new Vector3(1f - c1.r, 1f - c1.g, 1f - c1.b);
            Vector3 cmy2 = new Vector3(1f - c2.r, 1f - c2.g, 1f - c2.b);
            Vector3 mixed = Vector3.Lerp(cmy1, cmy2, t);
            return new Color(
                Mathf.Clamp01(1f - mixed.x),
                Mathf.Clamp01(1f - mixed.y),
                Mathf.Clamp01(1f - mixed.z),
                Mathf.Lerp(c1.a, c2.a, t));
        }

        // ── Wet capillary spreading ─────────────────────────────────

        void UpdateWetSpreading()
        {
            if (_wetIndices.Count == 0 || Humidity < 0.3f) return;

            int maxProcess = Mathf.Min(_wetIndices.Count, 120);
            int processed = 0;
            var spreadQueue = new List<(int x, int y, Color c, float a)>(64);

            foreach (int idx in _wetIndices)
            {
                if (processed++ >= maxProcess) break;
                if (_thicknessMap[idx] < 0.05f) continue;

                float prob = _thicknessMap[idx] * WetSpreadRate * Humidity;
                if (Random.value > prob) continue;

                int x = idx % TextureRes;
                int y = idx / TextureRes;
                float spreadA = 0.025f * _thicknessMap[idx] * Humidity;
                Color src = _wetPixels[idx];

                QueueSpread(spreadQueue, x + 1, y, src, spreadA);
                QueueSpread(spreadQueue, x - 1, y, src, spreadA);
                QueueSpread(spreadQueue, x, y + 1, src, spreadA);
                QueueSpread(spreadQueue, x, y - 1, src, spreadA);

                int gx = Mathf.RoundToInt(_uvGravityDir.x);
                int gy = Mathf.RoundToInt(_uvGravityDir.y);
                if (gx != 0 || gy != 0)
                    QueueSpread(spreadQueue, x + gx, y + gy, src, spreadA * 1.5f);
            }

            foreach (var (sx, sy, col, alp) in spreadQueue)
                BlendPixel(sx, sy, col, alp);
        }

        void QueueSpread(List<(int, int, Color, float)> q, int x, int y, Color c, float a)
        {
            if ((uint)x < (uint)TextureRes && (uint)y < (uint)TextureRes)
                q.Add((x, y, c, a));
        }

        // ── Drying ──────────────────────────────────────────────────

        void UpdateDrying()
        {
            if (_wetIndices.Count == 0) return;

            float dt = Time.deltaTime;
            List<int> toRemove = null;

            foreach (int idx in _wetIndices)
            {
                _wetTimer[idx] -= dt;
                if (_thicknessMap[idx] > 0f)
                    _thicknessMap[idx] = Mathf.Max(0f, _thicknessMap[idx] - dt * 0.015f);

                if (_wetTimer[idx] <= 0f)
                {
                    _wetTimer[idx] = 0f;
                    _wetPixels[idx] = new Color32(0, 0, 0, 0);
                    (toRemove ??= new List<int>(32)).Add(idx);
                }
            }

            if (toRemove != null)
                foreach (int idx in toRemove) _wetIndices.Remove(idx);
        }

        // ── Dirty-rect upload ───────────────────────────────────────

        void MarkDirty(int x, int y)
        {
            if (!_dirty) { _dx0 = _dx1 = x; _dy0 = _dy1 = y; _dirty = true; return; }
            if (x < _dx0) _dx0 = x; else if (x > _dx1) _dx1 = x;
            if (y < _dy0) _dy0 = y; else if (y > _dy1) _dy1 = y;
        }

        void FlushDirty()
        {
            if (!_dirty) return;

            int rx = Mathf.Clamp(_dx0, 0, TextureRes - 1);
            int ry = Mathf.Clamp(_dy0, 0, TextureRes - 1);
            int rw = Mathf.Clamp(_dx1 - _dx0 + 1, 1, TextureRes - rx);
            int rh = Mathf.Clamp(_dy1 - _dy0 + 1, 1, TextureRes - ry);

            // Copy only the dirty rows into the reused scratch buffer (no allocation).
            for (int row = 0; row < rh; row++)
                System.Array.Copy(_pixels, (ry + row) * TextureRes + rx, _uploadBlock, row * rw, rw);

            _paintTex.SetPixels32(rx, ry, rw, rh, _uploadBlock);
            _paintTex.Apply(false);
            _dirty = false;
        }
    }
}
