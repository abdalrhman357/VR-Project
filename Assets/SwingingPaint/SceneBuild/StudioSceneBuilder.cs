using UnityEngine;
using Seb.Fluid.Simulation;
using Seb.Fluid.Rendering;
using SwingingPaint.Core;
using SwingingPaint.Rope;
using SwingingPaint.Bucket;
using SwingingPaint.Canvas;
using SwingingPaint.Managers;
using SwingingPaint.UI;
using SwingingPaint.IO;

namespace SwingingPaint.SceneBuild
{
    /// <summary>
    /// Builds the ENTIRE studio scene in code (Part 6.5) so the project is press-Play ready with nothing
    /// to assemble by hand: environment, lighting, the GPU fluid stack, the Verlet rope, the bucket, the
    /// canvas, the camera rig, and the managers — every reference wired programmatically.
    ///
    /// Pattern: everything is built under a single INACTIVE root, fully configured, then activated once at
    /// the end — so each component's Awake/Start runs only after its fields are set and references resolved.
    /// </summary>
    public sealed class StudioSceneBuilder : MonoBehaviour
    {
        [SerializeField] SimulationConfig _config = new SimulationConfig();
        public SimulationConfig Config => _config;

        public bool buildOnAwake = true;
        GameObject _root;

        void Awake()
        {
            if (buildOnAwake) Build();
        }

        public void Build()
        {
            if (SceneRefs.Exists<SimulationManager>()) return; // already built

            var cfg = _config;
            Vector3 anchor = cfg.rope.anchorPosition;
            float diameter = Mathf.Max(0.05f, cfg.bucket.radius * 2f);
            float height = diameter * 1.3f;
            // Bucket rest position (straight down) — used only to frame the camera.
            Vector3 rest = anchor + Vector3.down * (cfg.rope.length + height * 0.5f);

            _root = new GameObject("SwingingPaintStudio");
            _root.SetActive(false); // build everything inactive, activate at the end

            BuildEnvironment(cfg, anchor);
            ConfigureRenderSettings();
            BuildLighting();

            // Reflection probe so the glass bucket + wet paint pick up the room (baked once on enable).
            var rpGO = Child("ReflectionProbe");
            rpGO.transform.position = new Vector3(anchor.x, Mathf.Max(2f, rest.y), anchor.z);
            var rp = rpGO.AddComponent<ReflectionProbe>();
            rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            rp.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            rp.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            rp.size = new Vector3(60f, 40f, 60f);
            rp.resolution = 128;

            // ── Fluid stack ──────────────────────────────────────────
            var fluidGO = Child("FluidSimulation");

            // DENSITY spawn mode: particle SPACING is fixed by the density (= 1/cbrt(density)), so it
            // always matches the SPH constants below → stable at any bucket size. The particle COUNT then
            // scales automatically with the bucket volume.
            var spawner = fluidGO.AddComponent<Spawner3D>();
            spawner.useExactCount = false;
            spawner.particleSpawnDensity = 1000;         // more particles → finer, more continuous stream
            spawner.jitterStrength = 0.015f;
            spawner.initialVel = Vector3.zero;

            var fluid = fluidGO.AddComponent<FluidSim>();
            // Position the fluid volume + spawn region at the bucket's initial pose (shared with Reset).
            BucketSimulator.ConfigureInitialFluidPose(cfg, fluid, spawner, 1.3f);
            fluid.compute = Resources.Load<ComputeShader>("SwingingPaint/Compute/FluidSim");
            fluid.spawner = spawner;
            fluid.gravity = -Mathf.Abs(cfg.world.gravity);
            // SPH constants tied to the spawn density by the stability rule:
            //   spacing = 1/cbrt(density),  smoothingRadius ≈ 1.7·spacing,  targetDensity ≈ density.
            // (density 1000 ⇒ spacing≈0.10, smoothingRadius≈0.17 — same neighbour count as the demo.)
            fluid.smoothingRadius = 0.17f;
            fluid.targetDensity = 1050f;     // ~5% above density (matches demo ratio) → ~zero rest pressure, less "popping"
            fluid.pressureMultiplier = 288f;
            fluid.nearPressureMultiplier = 2.16f;
            fluid.viscosityStrength = 0.08f;             // light cohesion; FluidManager refines from config
            fluid.collisionDamping = 0.4f;
            fluid.maxTimestepFPS = 60f;
            fluid.iterationsPerFrame = Mathf.Max(1, cfg.quality.solverIterationsPerFrame);
            fluid.foamActive = false;                    // paint shouldn't foam/spray
            fluid.renderToTex3D = false;
            fluid.holeRadius = cfg.bucket.holeRadius;
            fluid.holeOffset = cfg.bucket.holeOffset;
            fluid.recycleParticles = cfg.paint.continuousSource; // continuous vs drain (toggle in panel)
            fluid.recycleHeight = -1f;          // below the canvas (paints first, then recycles)

            var displayGO = Child("ParticleDisplay", fluidGO.transform);
            var display = displayGO.AddComponent<ParticleDisplay3D>();
            display.sim = fluid;
            display.shaderShaded = Shader.Find("Fluid/Particle3DSurf");
            display.shaderBillboard = Shader.Find("Fluid/ParticleBillboard");
            display.mode = ParticleDisplay3D.DisplayMode.Shaded3D;
            // Render size ~1.4× spacing (≈0.10) so particles overlap into a cohesive liquid without bloating.
            display.scale = 13f;
            display.tint = cfg.paint.color;
            display.meshResolution = 2;
            display.gradientResolution = 64;
            display.velocityDisplayMax = 6f;
            display.colourMap = MakePaintGradient(cfg.paint.color);

            var bucketRenderer = fluidGO.AddComponent<BucketRenderer>();
            bucketRenderer.fluidSim = fluid;
            bucketRenderer.heightScale = cfg.bucket.heightScale;
            bucketRenderer.glass = true;                      // transparent → the fluid inside is visible
            bucketRenderer.bucketColour = new Color(0.7f, 0.8f, 0.9f);
            bucketRenderer.rimColour = new Color(0.85f, 0.9f, 0.95f);
            bucketRenderer.glassAlpha = 0.16f;

            // ── Rope ────────────────────────────────────────────────
            var ropeGO = Child("Rope");
            var rope = ropeGO.AddComponent<RopeSimulator>();
            rope.Config = cfg;
            rope.selfDrive = false;
            var ropeRenderer = ropeGO.AddComponent<RopeRenderer>();
            ropeRenderer.rope = rope;

            // ── Bucket coupling ─────────────────────────────────────
            var bucketGO = Child("Bucket");
            var bucket = bucketGO.AddComponent<BucketSimulator>();
            bucket.Config = cfg;
            bucket.rope = rope;
            bucket.fluidSim = fluid;

            // ── Canvas ──────────────────────────────────────────────
            var canvas = BuildCanvas(cfg, anchor);

            // ── Camera rig ──────────────────────────────────────────
            BuildCamera(anchor, rest);

            // ── Managers ────────────────────────────────────────────
            var mgrGO = Child("Managers");
            var sim = mgrGO.AddComponent<SimulationManager>();
            sim.SetConfig(cfg);
            sim.rope = rope; sim.bucket = bucket; sim.fluidSim = fluid; sim.canvas = canvas;
            var fluidMgr = mgrGO.AddComponent<FluidManager>();
            fluidMgr.Config = cfg; fluidMgr.fluidSim = fluid; fluidMgr.canvas = canvas;
            sim.fluidManager = fluidMgr;

            // ── UI + IO (Part 7) ────────────────────────────────────
            var io = mgrGO.AddComponent<StudioIO>();
            io.sim = sim; io.canvas = canvas;

            var uiGO = Child("UI");
            var panel = uiGO.AddComponent<ControlPanel>();
            panel.sim = sim; panel.io = io;
            var stats = uiGO.AddComponent<StatsPanel>();
            stats.sim = sim;

            // Activate the whole graph at once → Awakes/Starts now run in execution order with refs set.
            _root.SetActive(true);
        }

        // ── Builders ─────────────────────────────────────────────────

        void BuildEnvironment(SimulationConfig cfg, Vector3 anchor)
        {
            Color woodCol = new Color(0.40f, 0.28f, 0.18f);
            Color wallCol = new Color(0.82f, 0.80f, 0.76f);
            Color ceilCol = new Color(0.90f, 0.90f, 0.90f);
            float room = 44f, wallH = 22f;

            CreatePlane("Floor", new Vector3(anchor.x, 0f, anchor.z), room, Mat(woodCol, 0f, 0.15f));

            // Ceiling
            var ceil = CreatePlane("Ceiling", new Vector3(anchor.x, wallH, anchor.z), room, Mat(ceilCol, 0f, 0.1f));
            ceil.transform.rotation = Quaternion.Euler(180f, 0f, 0f); // face down

            // Four walls (thin cubes)
            CreateCube("Wall_N", new Vector3(anchor.x, wallH * 0.5f, anchor.z + room * 0.5f), new Vector3(room, wallH, 0.2f), Mat(wallCol, 0f, 0.1f));
            CreateCube("Wall_S", new Vector3(anchor.x, wallH * 0.5f, anchor.z - room * 0.5f), new Vector3(room, wallH, 0.2f), Mat(wallCol, 0f, 0.1f));
            CreateCube("Wall_E", new Vector3(anchor.x + room * 0.5f, wallH * 0.5f, anchor.z), new Vector3(0.2f, wallH, room), Mat(wallCol, 0f, 0.1f));
            CreateCube("Wall_W", new Vector3(anchor.x - room * 0.5f, wallH * 0.5f, anchor.z), new Vector3(0.2f, wallH, room), Mat(wallCol, 0f, 0.1f));

            // Ceiling mount the rope hangs from
            CreateCube("AnchorMount", anchor + Vector3.up * 0.15f, new Vector3(0.8f, 0.3f, 0.8f), Mat(new Color(0.15f, 0.15f, 0.17f), 0.6f, 0.5f));
        }

        void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.40f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.16f, 0.14f);
            RenderSettings.fog = false;
            UnityEngine.QualitySettings.shadowDistance = 80f; // cover the scaled-up room

            // Soft procedural sky (nicer background + reflections than a flat colour).
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetColor("_SkyTint", new Color(0.52f, 0.60f, 0.70f));
            sky.SetColor("_GroundColor", new Color(0.30f, 0.28f, 0.25f));
            sky.SetFloat("_AtmosphereThickness", 0.8f);
            sky.SetFloat("_Exposure", 1.1f);
            RenderSettings.skybox = sky;

        }

        Light BuildLighting()
        {
            var sunGO = Child("KeyLight");
            sunGO.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            RenderSettings.sun = sun;

            var fillGO = Child("FillLight");
            fillGO.transform.rotation = Quaternion.Euler(28f, 150f, 0f);
            var fill = fillGO.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.7f, 0.78f, 0.9f);
            fill.intensity = 0.4f;
            fill.shadows = LightShadows.None;
            return sun;
        }

        PaintCanvas BuildCanvas(SimulationConfig cfg, Vector3 anchor)
        {
            // IMPORTANT: build the canvas mesh at real-metre size with transform scale = 1.
            // PaintCanvas.TryPaint works in the canvas's local space and compares against CanvasWidth
            // (metres); a scaled mesh would make local coords ≠ metres and reject every hit.
            float w = cfg.canvas.size.x, h = cfg.canvas.size.y;
            var go = Child("Canvas");
            go.transform.position = new Vector3(anchor.x, 0.05f, anchor.z); // scale stays 1

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildCanvasQuad(w, h);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(Color.white, 0f, 0.05f);

            var pc = go.AddComponent<PaintCanvas>();
            pc.CanvasWidth = w;
            pc.CanvasHeight = h;
            pc.TextureRes = cfg.canvas.textureResolution;
            pc.AbsorptionRate = cfg.canvas.absorptionRate;
            pc.Humidity = cfg.canvas.humidity;
            pc.AutoCalculateTilt = cfg.canvas.autoTilt;
            pc.PaintNormalLocal = Vector3.up; // quad lies in XZ, faces +Y
            return pc;
        }

        /// <summary>A flat quad in the XZ plane (normal +Y), sized w×h metres, UV 0..1, centred at origin.</summary>
        static Mesh BuildCanvasQuad(float w, float h)
        {
            float hw = w * 0.5f, hh = h * 0.5f;
            var mesh = new Mesh { name = "CanvasQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-hw, 0f, -hh), new Vector3(hw, 0f, -hh),
                new Vector3(hw, 0f,  hh), new Vector3(-hw, 0f,  hh)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; // wound so the +Y face is front
            mesh.RecalculateBounds();
            return mesh;
        }

        void BuildCamera(Vector3 anchor, Vector3 rest)
        {
            var targetGO = Child("CameraTarget");
            targetGO.transform.position = new Vector3(anchor.x, Mathf.Max(0.4f, rest.y * 0.5f), anchor.z);

            var camGO = Child("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            camGO.AddComponent<AudioListener>();
            var rig = camGO.AddComponent<CameraRig>();
            rig.target = targetGO.transform;
            rig.orbitDistance = Mathf.Max(4f, (anchor.y) * 1.2f);
        }

        // ── Primitive / material helpers ─────────────────────────────

        GameObject Child(string name) => Child(name, _root.transform);

        GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        GameObject CreateCube(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        GameObject CreatePlane(string name, Vector3 pos, float sizeUnits, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (sizeUnits / 10f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>(); // remove Unity physics colliders (custom physics only)
            if (col != null) DestroyImmediate(col); // immediate: GO may still be inactive during build
        }

        static Material Mat(Color c, float metallic, float smoothness)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);
            return m;
        }

        static Gradient MakePaintGradient(Color baseColor)
        {
            var g = new Gradient();
            Color bright = Color.Lerp(baseColor, Color.white, 0.5f);
            g.SetKeys(
                new[] { new GradientColorKey(baseColor, 0f), new GradientColorKey(bright, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
