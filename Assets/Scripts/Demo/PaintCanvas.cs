using UnityEngine;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// A dynamic canvas that receives paint droplets and accumulates them as
    /// LAYERS rather than averaging colours together. This is the core of the
    /// "acrylic / fluid-art" look required by the brief:
    ///
    ///   • New paint is composited OVER the existing surface (Porter–Duff "over"),
    ///     so pigments stay visually distinct instead of muddying to grey.
    ///   • A per-texel THICKNESS buffer grows with every deposit and is used to
    ///     shade height (thicker paint reads slightly darker/glossier at the ridges).
    ///   • A per-texel WETNESS buffer decays over time (drying speed). Wet-on-wet
    ///     hits blend a little into the surface; wet-on-dry stays crisp.
    ///
    /// Everything is CPU-side into a Color32 buffer, uploaded once per frame — no
    /// per-pixel GPU work, no third-party code.
    /// </summary>
    public class PaintCanvas : MonoBehaviour
    {
        [Header("Resolution & size")]
        public int resolution = 1024;
        [Tooltip("World-space size of the canvas plane (square), metres.")]
        public float worldSize = 2f;
        public Color baseColor = new Color(0.96f, 0.95f, 0.92f);

        [Header("Deposition model")]
        [Tooltip("Brush radius of one droplet hit, in pixels.")]
        public float brushRadiusPx = 7f;
        [Tooltip("How strongly a single hit covers the surface (0..1).")]
        [Range(0f, 1f)] public float depositOpacity = 0.85f;
        [Tooltip("Drying rate [1/s]: how fast wetness fades so later paint sits on dry.")]
        public float dryingRate = 0.15f;
        [Tooltip("Extra blend into the surface when the spot is still wet (wet-on-wet).")]
        [Range(0f, 1f)] public float wetBlend = 0.35f;

        // ── Buffers ──────────────────────────────────────────────────────────
        Color32[] _pixels;
        float[] _thickness;
        float[] _wetness;
        Texture2D _tex;
        Material _mat;
        bool _dirty;

        // World mapping (canvas lies in the XZ plane at this transform's Y).
        Vector3 _origin;   // world position of pixel (0,0) corner
        float _pxPerMeter;

        public float TotalPaintDeposited { get; private set; }  // for the stats panel

        void Awake()
        {
            Build();
        }

        public void Build()
        {
            // Idempotent: free any previous GPU resources so re-Build()/ClearCanvas
            // (and the binder re-configuring size after Awake) never leak textures.
            if (_tex != null) { Destroy(_tex); _tex = null; }
            if (_mat != null) { Destroy(_mat); _mat = null; }

            resolution = Mathf.Clamp(resolution, 64, 4096);
            int n = resolution * resolution;
            _pixels = new Color32[n];
            _thickness = new float[n];
            _wetness = new float[n];

            Color32 b = baseColor;
            for (int i = 0; i < n; i++) _pixels[i] = b;

            _tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _tex.SetPixels32(_pixels);
            _tex.Apply(false);

            _mat = new Material(Shader.Find("Standard"));
            _mat.mainTexture = _tex;
            _mat.SetFloat("_Glossiness", 0.5f);   // wet-paint sheen
            _mat.SetFloat("_Metallic", 0f);

            if (!TryGetComponent<MeshFilter>(out var mf)) mf = gameObject.AddComponent<MeshFilter>();
            if (!TryGetComponent<MeshRenderer>(out var mr)) mr = gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = PrimitiveMeshFactory.CreateQuadXZ(worldSize);
            mr.sharedMaterial = _mat;

            RecomputeMapping();
            _dirty = false;
            TotalPaintDeposited = 0f;
        }

        void RecomputeMapping()
        {
            // Quad is centred on transform, spans worldSize in X and Z.
            _origin = transform.position - new Vector3(worldSize * 0.5f, 0f, worldSize * 0.5f);
            _pxPerMeter = resolution / worldSize;
        }

        /// <summary>Is a world point above the canvas footprint (used by the drip system)?</summary>
        public bool ContainsXZ(Vector3 world)
        {
            Vector3 l = world - _origin;
            return l.x >= 0 && l.z >= 0 && l.x <= worldSize && l.z <= worldSize;
        }

        public float SurfaceY => transform.position.y;

        /// <summary>
        /// Deposit one droplet of <paramref name="color"/> at world position
        /// <paramref name="world"/>. <paramref name="amount"/> scales coverage and
        /// thickness (e.g. droplet volume). Returns true if it landed on canvas.
        /// </summary>
        public bool Deposit(Vector3 world, Color color, float amount)
        {
            if (_pixels == null) return false;
            RecomputeMapping();
            if (!ContainsXZ(world)) return false;

            Vector3 l = world - _origin;
            int cx = Mathf.RoundToInt(l.x * _pxPerMeter);
            int cy = Mathf.RoundToInt(l.z * _pxPerMeter);
            int r = Mathf.Max(1, Mathf.RoundToInt(brushRadiusPx));
            float r2 = r * r;

            for (int dy = -r; dy <= r; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= resolution) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = cx + dx;
                    if (px < 0 || px >= resolution) continue;

                    float d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    int idx = py * resolution + px;

                    // Soft brush falloff (smoothstep on distance).
                    float t = 1f - Mathf.Sqrt(d2 / r2);
                    float falloff = t * t * (3f - 2f * t);
                    float coverage = Mathf.Clamp01(falloff * depositOpacity * amount);
                    if (coverage <= 0f) continue;

                    // Wet-on-wet lets a little of the new colour sink & mix; wet-on-dry
                    // is a crisp layer on top. Effective alpha = coverage boosted by
                    // wetness*wetBlend.
                    float wet = _wetness[idx];
                    float a = Mathf.Clamp01(coverage + coverage * wet * wetBlend);

                    Color32 e = _pixels[idx];
                    // Porter–Duff "over": out = new*a + old*(1-a). Keeps pigments distinct.
                    byte rr = (byte)(color.r * 255f * a + e.r * (1f - a));
                    byte gg = (byte)(color.g * 255f * a + e.g * (1f - a));
                    byte bb = (byte)(color.b * 255f * a + e.b * (1f - a));

                    // Thickness ridge shading: thicker paint darkens very slightly.
                    _thickness[idx] += coverage * 0.5f;
                    float shade = 1f - Mathf.Clamp01(_thickness[idx] * 0.02f) * 0.15f;

                    _pixels[idx] = new Color32(
                        (byte)(rr * shade), (byte)(gg * shade), (byte)(bb * shade), 255);
                    _wetness[idx] = 1f;
                }
            }

            TotalPaintDeposited += amount;
            _dirty = true;
            return true;
        }

        void Update()
        {
            // Drying: fade wetness so subsequent paint behaves as wet-on-dry.
            if (dryingRate > 0f && _wetness != null)
            {
                float decay = Mathf.Exp(-dryingRate * Time.deltaTime);
                // Sparse update is enough visually; decay the whole buffer cheaply.
                for (int i = 0; i < _wetness.Length; i++)
                    if (_wetness[i] > 0f) _wetness[i] *= decay;
            }

            if (_dirty)
            {
                _tex.SetPixels32(_pixels);
                _tex.Apply(false);
                _dirty = false;
            }
        }

        public void ClearCanvas()
        {
            Build();
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_mat != null) Destroy(_mat);
        }
    }
}
