using UnityEngine;
using SwingingPaint.Rope;
using SwingingPaint.Suspension;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Builds the entire demo scene from code so the project runs with ZERO manual
    /// setup: just open it and press Play. Creates a camera, lighting, a canvas, a
    /// ceiling anchor, the rope+bucket rig (real physics) and the paint-flow +
    /// GUI systems, then wires them together.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        [Header("Layout")]
        public float ceilingHeight = 3f;
        public float ropeLength = 2f;
        public RopeType ropeType = RopeType.Nylon;
        public float bucketRadius = 0.16f;
        public float bucketHeight = 0.3f;
        public float startPaintMass = 1.5f;

        // NOTE: Auto-spawn is intentionally DISABLED in the consolidated project.
        // The single integrated experience lives in the "Fluid Particles" scene,
        // wired by PendulumFluidBinder. This standalone mechanics demo only builds
        // if you add the DemoBootstrap component to a GameObject by hand.

        void Awake()
        {
            SetupEnvironment();
            var cam = SetupCamera();
            var canvas = SetupCanvas();
            SetupCeiling();

            // ── Bucket container (driven by the pendulum) ────────────────────
            var bucketGO = new GameObject("Bucket");
            var bucketRenderer = bucketGO.AddComponent<BucketMeshRenderer>();

            // ── Rope visual ──────────────────────────────────────────────────
            var ropeGO = new GameObject("Rope");
            ropeGO.AddComponent<LineRenderer>();
            var ropeRenderer = ropeGO.AddComponent<RopeLineRenderer>();

            // ── Pendulum physics ─────────────────────────────────────────────
            var pendGO = new GameObject("Pendulum");
            var pend = pendGO.AddComponent<PaintPendulumSystem>();
            pend.simulationContainer = bucketGO.transform;
            pend.interactionCamera = cam;
            pend.anchor = new SuspensionAnchor
            {
                kind = SuspensionAnchor.AnchorKind.Ceiling,
                ceilingHeight = ceilingHeight,
                ceilingXZ = Vector2.zero
            };
            pend.ropeType = ropeType;
            pend.ropeLength = ropeLength;
            pend.ropeNodes = 28;
            pend.emptyMass = 1.0f;
            pend.paintMass = startPaintMass;
            pend.bucketRadiusOverride = bucketRadius;
            pend.bucketHeightOverride = bucketHeight;
            pend.attachHeightFactor = 1.0f;
            pend.initialBucketVelocity = new Vector3(1.3f, 0f, 0.7f); // lively orbit start
            pend.drawGizmos = false;

            bucketRenderer.pendulum = pend;
            ropeRenderer.pendulum = pend;

            // ── Paint flow ───────────────────────────────────────────────────
            var drip = pendGO.AddComponent<PaintDripSystem>();
            drip.pendulum = pend;
            drip.canvas = canvas;

            // ── GUI ──────────────────────────────────────────────────────────
            var gui = pendGO.AddComponent<DemoGUI>();
            gui.pendulum = pend;
            gui.drip = drip;
            gui.canvas = canvas;
        }

        void SetupEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);
            RenderSettings.fog = false;

            var sunGO = new GameObject("Sun");
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Camera SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGO = new GameObject("Main Camera");
                camGO.tag = "MainCamera";
                cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
            cam.nearClipPlane = 0.02f;
            var orbit = cam.gameObject.GetComponent<DemoOrbitCamera>() ?? cam.gameObject.AddComponent<DemoOrbitCamera>();
            orbit.target = new Vector3(0f, ceilingHeight - ropeLength - 0.2f, 0f);
            orbit.distance = 4.5f;
            return cam;
        }

        PaintCanvas SetupCanvas()
        {
            var floorGO = new GameObject("Floor");
            var fmf = floorGO.AddComponent<MeshFilter>();
            var fmr = floorGO.AddComponent<MeshRenderer>();
            fmf.sharedMesh = PrimitiveMeshFactory.CreateQuadXZ(12f);
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.2f, 0.2f, 0.22f);
            fmr.sharedMaterial = floorMat;
            floorGO.transform.position = new Vector3(0f, 0f, 0f);

            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.position = new Vector3(0f, 0.02f, 0f);
            var canvas = canvasGO.AddComponent<PaintCanvas>();
            canvas.worldSize = 2.6f;
            canvas.resolution = 1024;
            return canvas;
        }

        void SetupCeiling()
        {
            var beamGO = new GameObject("Ceiling");
            var mf = beamGO.AddComponent<MeshFilter>();
            var mr = beamGO.AddComponent<MeshRenderer>();
            mf.sharedMesh = PrimitiveMeshFactory.CreateQuadXZ(1.2f);
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.3f, 0.3f, 0.33f);
            mr.sharedMaterial = mat;
            beamGO.transform.position = new Vector3(0f, ceilingHeight + 0.01f, 0f);

            // Small marker sphere at the anchor point.
            var markGO = new GameObject("AnchorMarker");
            var mmf = markGO.AddComponent<MeshFilter>();
            var mmr = markGO.AddComponent<MeshRenderer>();
            mmf.sharedMesh = PrimitiveMeshFactory.CreateUVSphere(0.03f, 8, 12);
            var mmat = new Material(Shader.Find("Standard"));
            mmat.color = new Color(0.9f, 0.7f, 0.2f);
            mmr.sharedMaterial = mmat;
            markGO.transform.position = new Vector3(0f, ceilingHeight, 0f);
        }
    }
}
