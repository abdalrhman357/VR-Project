using UnityEngine;
using Seb.Fluid.Simulation;
using SwingingPaint;
using SwingingPaint.Rope;
using SwingingPaint.Suspension;
using SwingingPaint.Demo;

namespace SwingingPaint.Integration
{
    /// <summary>
    /// The single "link everything together" component. On Play it finds the
    /// existing SPH <see cref="FluidSim"/> and assembles the full experience around
    /// it, WITHOUT modifying any fluid code.
    ///
    /// IMPORTANT — everything is sized PROPORTIONALLY to the actual bucket. The
    /// fluid cylinder scale in the scene can be anything (this project's scene uses
    /// 10 units), so absolute metres would make the rope/canvas mismatch the bucket.
    /// Instead we derive rope length, canvas size & drop, brush size, deposition
    /// band, rope thickness and camera framing from the bucket's own dimensions, so
    /// the whole rig stays visually consistent at any scale.
    ///
    /// It: drives the fluid container (paint sloshes & pours from the swinging
    /// bucket), opens the bottom outlet, adds a rope visual, spawns a proportional
    /// canvas + a <see cref="FluidCanvasDepositor"/> (real SPH → layered paint),
    /// swaps the left-mouse OrbitCam for a right-mouse orbit camera, and adds the
    /// control panel. Just open "Fluid Particles" and press Play.
    /// </summary>
    public class PendulumFluidBinder : MonoBehaviour
    {
        [Header("Rope / pendulum (proportional to bucket size)")]
        public RopeType ropeType = RopeType.Nylon;
        [Tooltip("Rope length as a multiple of the bucket's overall size.")]
        public float ropeLengthFactor = 1.5f;
        public int ropeNodes = 28;
        public float emptyMass = 1.2f;
        [Tooltip("Paint load [kg] for bucket dynamics (tune live).")]
        public float paintMass = 2.0f;
        [Tooltip("Initial swing speed as a fraction of bucket size per second (gentle).")]
        public float nudgeFactor = 0.1f;
        [Tooltip("Keep the bucket upright (spherical pendulum) so the SPH paint stays inside — the fluid collision cylinder does not rotate yet.")]
        public bool keepBucketUpright = true;

        [Header("Paint outlet")]
        public bool openBottomHole = true;
        [Range(0.05f, 0.9f)]
        [Tooltip("Hole radius as a fraction of the bucket radius (small = thin natural stream).")]
        public float holeFraction = 0.15f;

        [Header("Canvas (proportional)")]
        public bool addCanvas = true;
        [Tooltip("Canvas drop below the bucket as a multiple of bucket size.")]
        public float canvasDropFactor = 0.9f;
        [Tooltip("Canvas width as a multiple of rope length (to catch the swing).")]
        public float canvasSizeFactor = 1.8f;
        public int canvasResolution = 1024;
        [Tooltip("Deposition brush world radius as a fraction of the bucket radius.")]
        public float brushFactor = 0.12f;

        public bool addRopeVisual = true;
        public bool addControlPanel = true;

        bool _bound;
        PaintPendulumSystem _pend;

        // Runs on EVERY entry to Play (NO static guard, so re-playing always re-wires).
        // It only creates the host GameObject; the host's Awake() does the single Bind.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBind()
        {
            if (FindAnyObjectByType<FluidSim>() == null) return;            // not a fluid scene
            if (FindAnyObjectByType<PaintPendulumSystem>() != null) return; // already wired (manual binder)
            new GameObject("PaintPendulumBinder").AddComponent<PendulumFluidBinder>();
        }

        void Awake()
        {
            if (_bound) return;
            var fluid = FindAnyObjectByType<FluidSim>();
            if (fluid != null && FindAnyObjectByType<PaintPendulumSystem>() == null)
                Bind(fluid);
        }

        public PaintPendulumSystem Bind(FluidSim fluid)
        {
            // Guard: bind exactly once (prevents the double-build that made two
            // pendulums fight over the same container).
            if (_bound) return _pend;
            _bound = true;

            Transform container = fluid.transform;

            // ── Bucket dimensions from the fluid cylinder ────────────────────
            float radius = Mathf.Max(0.02f, container.localScale.x * 0.5f);
            float height = Mathf.Max(0.04f, container.localScale.y);
            float halfH  = height * 0.5f;
            float bucketSize = Mathf.Max(height, radius * 2f);   // overall extent

            // ── Everything else derived proportionally ───────────────────────
            float ropeLength   = Mathf.Max(0.1f, ropeLengthFactor * bucketSize);
            float canvasDrop   = canvasDropFactor * bucketSize;
            float canvasSize   = Mathf.Max(canvasSizeFactor * ropeLength, 6f * radius);
            float ropeWidth    = Mathf.Max(radius * 0.06f, bucketSize * 0.02f);
            float bandThick    = Mathf.Max(0.3f, bucketSize * 0.08f);
            float nudgeSpeed   = nudgeFactor * bucketSize;

            // Attach the rope at the VISUAL rim: BucketRenderer draws the top rim at
            // transform.y + (Scale.y/2)*heightScale, so match attachHeightFactor to it.
            float attachFactor = (fluid.bucketRenderer != null) ? fluid.bucketRenderer.heightScale : 1.0f;

            // Open the bottom outlet so the SPH paint actually pours out.
            if (openBottomHole && fluid.holeRadius <= 0f)
                fluid.holeRadius = radius * holeFraction;

            // A little viscosity makes the SPH behave more like paint (coherent
            // stream, less splashy). Tunable live from the panel.
            if (fluid.viscosityStrength <= 0f)
                fluid.viscosityStrength = 0.1f;

            Vector3 p = container.position;
            float anchorY = p.y + ropeLength + halfH * attachFactor;
            float canvasY = p.y - halfH - canvasDrop;

            // ── Camera: swap left-mouse OrbitCam → right-mouse orbit, framed to rig ──
            Camera cam = Camera.main;
            if (cam != null)
            {
                var legacyOrbit = cam.GetComponent("OrbitCam") as Behaviour;
                if (legacyOrbit != null) legacyOrbit.enabled = false;

                var demoCam = cam.GetComponent<DemoOrbitCamera>();
                if (demoCam == null) demoCam = cam.gameObject.AddComponent<DemoOrbitCamera>();
                float rigHeight = anchorY - canvasY;
                demoCam.target      = new Vector3(p.x, (anchorY + canvasY) * 0.5f, p.z);
                demoCam.distance    = rigHeight * 1.25f;
                demoCam.minDistance = bucketSize * 0.5f;
                demoCam.maxDistance = rigHeight * 4f;
                demoCam.zoomSpeed   = bucketSize * 0.6f;
            }

            // ── Anchor + pendulum ────────────────────────────────────────────
            var anchor = new SuspensionAnchor
            {
                kind = SuspensionAnchor.AnchorKind.Ceiling,
                ceilingHeight = anchorY,
                ceilingXZ = new Vector2(p.x, p.z)
            };

            var pendGO = new GameObject("PaintPendulum");
            var pend = pendGO.AddComponent<PaintPendulumSystem>();
            pend.simulationContainer = container;
            pend.interactionCamera = cam;
            pend.anchor = anchor;
            pend.ropeType = ropeType;
            pend.ropeLength = ropeLength;
            pend.ropeNodes = ropeNodes;
            pend.emptyMass = emptyMass;
            pend.paintMass = paintMass;
            pend.bucketRadiusOverride = radius;
            pend.bucketHeightOverride = height;
            pend.attachHeightFactor = attachFactor;
            pend.keepBucketUpright = keepBucketUpright;
            pend.initialBucketVelocity = new Vector3(1f, 0f, 0.5f).normalized * nudgeSpeed;
            // pend.Start() also disables the legacy CylinderDragger on the fluid.

            // ── Rope visual (proportional width) ─────────────────────────────
            if (addRopeVisual)
            {
                var ropeGO = new GameObject("RopeVisual");
                ropeGO.AddComponent<LineRenderer>();
                var rr = ropeGO.AddComponent<RopeLineRenderer>();
                rr.pendulum = pend;
                rr.widthOverride = ropeWidth;
            }

            // ── Canvas + real SPH deposition (all proportional) ──────────────
            PaintCanvas canvas = null;
            FluidCanvasDepositor depositor = null;
            if (addCanvas)
            {
                var canvasGO = new GameObject("PaintCanvas");
                canvasGO.transform.position = new Vector3(p.x, canvasY, p.z);
                canvas = canvasGO.AddComponent<PaintCanvas>();
                canvas.worldSize = canvasSize;
                canvas.resolution = canvasResolution;
                // Brush sized in world units, converted to pixels for this canvas.
                float pxPerUnit = canvasResolution / Mathf.Max(0.01f, canvasSize);
                canvas.brushRadiusPx = Mathf.Clamp(brushFactor * radius * pxPerUnit, 2f, canvasResolution * 0.1f);
                canvas.Build();   // rebuild at the correct size (Awake built with defaults)

                depositor = pendGO.AddComponent<FluidCanvasDepositor>();
                depositor.fluid = fluid;
                depositor.canvas = canvas;
                depositor.bandThickness = bandThick;
                depositor.frameInterval = 2;      // gentler accumulation (was every frame)
                depositor.depositAmount = 0.4f;   // avoid instant saturation to solid colour
            }

            // ── Control panel + stats ────────────────────────────────────────
            if (addControlPanel)
            {
                var gui = pendGO.AddComponent<FluidSceneGUI>();
                gui.pendulum = pend;
                gui.depositor = depositor;
                gui.canvas = canvas;
                gui.fluid = fluid;
                gui.ropeLenMin = ropeLength * 0.3f;
                gui.ropeLenMax = ropeLength * 2.5f;
            }

            // Lift the ambient a little so the rig is readable if the scene is dark.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            if (RenderSettings.ambientLight.maxColorComponent < 0.2f)
                RenderSettings.ambientLight = new Color(0.35f, 0.36f, 0.40f);

            _pend = pend;
            return pend;
        }
    }
}
