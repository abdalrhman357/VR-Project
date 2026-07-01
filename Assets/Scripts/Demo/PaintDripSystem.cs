using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Original paint-flow model. Droplets are emitted from the bucket outlet at a
    /// rate driven by the physical state, then integrated by hand (gravity + air
    /// drag) until they strike the canvas and deposit as a layer.
    ///
    /// Exit speed follows Torricelli's law for an opening under a fluid column:
    ///     v_exit = C_d · √(2·g·h)
    /// where h is the paint head (estimated from the remaining paint mass). The
    /// emission rate additionally scales with the outlet area (hole diameter),
    /// how much the outlet faces "downhill" (inclination), and a user valve; it is
    /// damped by viscosity and cut off by surface tension at low head. Each emitted
    /// droplet removes mass from the bucket, so the swing changes as it empties.
    ///
    /// Rendering uses GPU instancing, batched per palette colour (so no custom
    /// shader is needed for per-droplet colour).
    /// </summary>
    public class PaintDripSystem : MonoBehaviour
    {
        [Header("Links")]
        public PaintPendulumSystem pendulum;
        public PaintCanvas canvas;

        [Header("Palette (switch colour live for layered art)")]
        public Color[] palette =
        {
            new Color(0.90f, 0.15f, 0.15f), // red
            new Color(0.15f, 0.35f, 0.90f), // blue
            new Color(0.98f, 0.80f, 0.10f), // yellow
            new Color(0.15f, 0.70f, 0.30f), // green
            new Color(0.10f, 0.10f, 0.12f), // black
            new Color(0.95f, 0.95f, 0.97f), // white
        };
        public int currentColor = 0;

        [Header("Flow")]
        [Tooltip("Master valve — is paint allowed to pour?")]
        public bool pouring = true;
        [Tooltip("Outlet (hole) diameter [m]. Larger = faster flow (∝ area).")]
        public float outletDiameter = 0.03f;
        [Tooltip("Discharge coefficient C_d of the outlet (0..1).")]
        [Range(0.1f, 1f)] public float dischargeCoefficient = 0.6f;
        [Tooltip("Paint kinematic viscosity factor: higher = slower flow.")]
        [Range(0f, 1f)] public float viscosity = 0.25f;
        [Tooltip("Surface-tension head cut-off [m]: below this paint head, flow stops.")]
        public float surfaceTensionHead = 0.01f;
        [Tooltip("Emission scale (droplets per unit of computed volumetric flow).")]
        public float emissionScale = 900f;

        [Header("Droplet")]
        public float dropletRadius = 0.012f;
        [Tooltip("Air drag on falling droplets [1/s].")]
        public float dropletAirDrag = 0.2f;
        public int maxDroplets = 4000;
        [Tooltip("Mass removed from the bucket per droplet [kg].")]
        public float dropletMass = 0.0006f;
        [Tooltip("Assumed paint density for the head estimate [kg/m³].")]
        public float paintDensity = 1300f;

        // ── Droplet pool (structure of arrays) ───────────────────────────────
        Vector3[] _pos;
        Vector3[] _vel;
        int[] _col;
        int _count;
        float _emitAccum;

        // Rendering
        Mesh _mesh;
        Material[] _colMats;
        Matrix4x4[] _batch;

        public int ActiveDroplets => _count;
        public float PaintConsumed { get; private set; }

        void Awake()
        {
            _pos = new Vector3[maxDroplets];
            _vel = new Vector3[maxDroplets];
            _col = new int[maxDroplets];
            _batch = new Matrix4x4[1023];
            _mesh = PrimitiveMeshFactory.CreateUVSphere(dropletRadius, 6, 8);
            BuildMaterials();
        }

        void BuildMaterials()
        {
            _colMats = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                var m = new Material(Shader.Find("Standard"));
                m.color = palette[i];
                m.SetFloat("_Glossiness", 0.6f);
                m.enableInstancing = true;
                _colMats[i] = m;
            }
        }

        void Update()
        {
            if (pendulum == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            Emit(dt);
            Integrate(dt);
            Render();
        }

        // ── Emission ─────────────────────────────────────────────────────────
        void Emit(float dt)
        {
            if (!pouring || canvas == null) return;

            var bucket = pendulum.Bucket;
            if (bucket == null) return;

            float paintMass = Mathf.Max(0f, pendulum.paintMass);
            if (paintMass <= 0f) return;

            // Paint head h from remaining mass: h = m / (ρ · A_base).
            float baseArea = Mathf.PI * bucket.Radius * bucket.Radius;
            float head = paintMass / (paintDensity * Mathf.Max(1e-4f, baseArea));
            if (head <= surfaceTensionHead) return;   // surface tension holds it in

            // Torricelli exit speed, damped by viscosity.
            float vExit = dischargeCoefficient * Mathf.Sqrt(2f * Mathf.Max(0.01f, gravityMag) * head);
            vExit *= (1f - 0.7f * viscosity);

            // Volumetric flow Q = Cd · A_hole · v_exit, scaled by how much the
            // outlet faces downhill (inclination coupling).
            float aHole = Mathf.PI * (outletDiameter * 0.5f) * (outletDiameter * 0.5f);
            Vector3 outletDir = (bucket.WorldOutletPoint - bucket.Position).normalized;
            float downhill = Mathf.Clamp01(Vector3.Dot(outletDir, Vector3.down) * 0.5f + 0.5f);
            float Q = dischargeCoefficient * aHole * vExit * Mathf.Lerp(0.3f, 1f, downhill);

            _emitAccum += Q * emissionScale * dt;
            int emit = Mathf.FloorToInt(_emitAccum);
            _emitAccum -= emit;

            Vector3 outlet = bucket.WorldOutletPoint;
            Vector3 carrier = bucket.PointVelocity(outlet);      // the outlet's own velocity (swing)

            for (int i = 0; i < emit && _count < maxDroplets; i++)
            {
                if (pendulum.paintMass <= 0f) break;

                Vector3 jitter = Random.insideUnitSphere * (outletDiameter * 0.4f);
                _pos[_count] = outlet + jitter;
                _vel[_count] = carrier + Vector3.down * vExit + jitter * 2f;
                _col[_count] = Mathf.Clamp(currentColor, 0, palette.Length - 1);
                _count++;

                // Consume paint → the bucket gets lighter and swings differently.
                pendulum.paintMass = Mathf.Max(0f, pendulum.paintMass - dropletMass);
                PaintConsumed += dropletMass;
            }
        }

        float gravityMag => pendulum != null ? Mathf.Max(0.01f, pendulum.gravity.magnitude) : 9.81f;

        // ── Integration + canvas collision ──────────────────────────────────
        void Integrate(float dt)
        {
            Vector3 g = pendulum.gravity;
            float surfaceY = canvas != null ? canvas.SurfaceY : -10f;
            float drag = Mathf.Exp(-dropletAirDrag * dt);

            for (int i = 0; i < _count;)
            {
                _vel[i] = (_vel[i] + g * dt) * drag;
                Vector3 next = _pos[i] + _vel[i] * dt;

                bool remove = false;

                // Crossed the canvas plane this step?
                if (_pos[i].y > surfaceY && next.y <= surfaceY && canvas != null)
                {
                    // Interpolate the exact crossing point for a clean splat.
                    float t = (_pos[i].y - surfaceY) / Mathf.Max(1e-5f, _pos[i].y - next.y);
                    Vector3 hit = Vector3.Lerp(_pos[i], next, t);
                    if (canvas.ContainsXZ(hit))
                        canvas.Deposit(hit, palette[_col[i]], 1f);
                    remove = true;
                }
                else if (next.y < surfaceY - 5f)   // fell past everything
                {
                    remove = true;
                }

                if (remove)
                {
                    _count--;
                    _pos[i] = _pos[_count];
                    _vel[i] = _vel[_count];
                    _col[i] = _col[_count];
                }
                else
                {
                    _pos[i] = next;
                    i++;
                }
            }
        }

        // ── Instanced rendering, batched by colour ──────────────────────────
        void Render()
        {
            if (_mesh == null || _colMats == null) return;
            Quaternion rot = Quaternion.identity;
            Vector3 scale = Vector3.one;

            for (int c = 0; c < palette.Length; c++)
            {
                int b = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (_col[i] != c) continue;
                    _batch[b++] = Matrix4x4.TRS(_pos[i], rot, scale);
                    if (b == _batch.Length)
                    {
                        Graphics.DrawMeshInstanced(_mesh, 0, _colMats[c], _batch, b);
                        b = 0;
                    }
                }
                if (b > 0) Graphics.DrawMeshInstanced(_mesh, 0, _colMats[c], _batch, b);
            }
        }

        public void ClearDroplets() => _count = 0;

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_colMats != null)
                foreach (var m in _colMats) if (m != null) Destroy(m);
        }
    }
}
