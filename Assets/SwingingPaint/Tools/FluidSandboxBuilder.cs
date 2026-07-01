using UnityEngine;
using UnityEngine.Rendering;
using Seb.Fluid.Simulation;
using Seb.Fluid.Rendering;
using SwingingPaint.Core;
using SwingingPaint.SceneBuild;

namespace SwingingPaint.Tools
{
    /// <summary>
    /// Standalone fluid sandbox (a project "tool"): a TRANSPARENT, MOVABLE box with SPH liquid inside,
    /// for evaluating the fluid simulation on its own — independent of the rope/bucket/canvas. Move the
    /// box (WASD/Q/E, mouse-drag, or X to shake) and watch the liquid slosh. Uses the exact same proven
    /// SPH settings as the studio so what you tune here transfers directly.
    ///
    /// Built entirely in code (inactive-then-activate), like the studio scene. Auto-runs in the
    /// "FluidSandbox" scene via <see cref="FluidSandboxBootstrap"/>.
    /// </summary>
    public sealed class FluidSandboxBuilder : MonoBehaviour
    {
        public Vector3 boxSize = new Vector3(4.5f, 4f, 4.5f);
        public bool buildOnAwake = true;
        GameObject _root;

        void Awake() { if (buildOnAwake) Build(); }

        public void Build()
        {
            if (SceneRefs.Exists<FluidSim>()) return;

            _root = new GameObject("FluidSandbox");
            _root.SetActive(false);

            // Lighting + ambient.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.40f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.20f, 0.22f);
            RenderSettings.fog = false;
            var sunGO = Child("KeyLight");
            sunGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.97f, 0.9f);
            sun.shadows = LightShadows.Soft;

            // Floor reference grid.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor"; StripCollider(floor);
            floor.transform.SetParent(_root.transform, false);
            floor.transform.position = new Vector3(0f, -boxSize.y * 0.5f - 1.5f, 0f);
            floor.transform.localScale = Vector3.one * 4f;
            floor.GetComponent<MeshRenderer>().sharedMaterial = Opaque(new Color(0.3f, 0.3f, 0.33f));

            // Fluid container (the sim bounds = transform.localScale). No hole → closed tank.
            var fluidGO = Child("FluidTank");
            fluidGO.transform.position = Vector3.zero;
            fluidGO.transform.localScale = boxSize;

            var spawner = fluidGO.AddComponent<Spawner3D>();
            spawner.useExactCount = false;
            spawner.particleSpawnDensity = 1000;         // spacing ≈ 0.10 → matches the SPH constants below
            spawner.jitterStrength = 0.015f;
            spawner.initialVel = Vector3.zero;
            float spawnSize = Mathf.Min(boxSize.x, boxSize.z) * 0.8f;
            spawner.spawnRegions = new[]
            {
                new Spawner3D.SpawnRegion
                {
                    centre = new Vector3(0f, -boxSize.y * 0.5f + spawnSize * 0.5f + 0.05f, 0f),
                    size = spawnSize,
                    debugDisplayCol = new Color(0.2f, 0.6f, 1f, 0.5f)
                }
            };

            var fluid = fluidGO.AddComponent<FluidSim>();
            fluid.compute = Resources.Load<ComputeShader>("SwingingPaint/Compute/FluidSim");
            fluid.spawner = spawner;
            fluid.gravity = -9.81f;
            fluid.smoothingRadius = 0.17f;               // = 1.7/cbrt(1000); same neighbour count as demo
            fluid.targetDensity = 1050f;                 // ~5% above density → ~zero rest pressure, calmer
            fluid.pressureMultiplier = 288f;
            fluid.nearPressureMultiplier = 2.16f;
            fluid.iterationsPerFrame = 5;                // calmer, more settled
            fluid.collisionDamping = 0.4f;
            fluid.maxTimestepFPS = 60f;
            fluid.foamActive = false;
            fluid.renderToTex3D = false;
            fluid.holeRadius = 0f;                        // closed box → nothing escapes
            fluid.useBoxBounds = true;                    // real box container (not a cylinder)
            fluid.viscosityStrength = 0.2f;               // calmer liquid for evaluation

            var displayGO = Child("ParticleDisplay");
            var display = displayGO.AddComponent<ParticleDisplay3D>();
            display.sim = fluid;
            display.shaderShaded = Shader.Find("Fluid/Particle3DSurf");
            display.shaderBillboard = Shader.Find("Fluid/ParticleBillboard");
            display.mode = ParticleDisplay3D.DisplayMode.Shaded3D;
            display.scale = 13f;                 // overlap → cohesive liquid look
            display.tint = new Color(0.15f, 0.45f, 0.95f);
            display.meshResolution = 2;
            display.gradientResolution = 64;
            display.velocityDisplayMax = 8f;
            display.colourMap = WaterGradient();

            // Transparent glass box (child cube of localScale 1 matches the sim bounds exactly).
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "GlassBox"; StripCollider(glass);
            glass.transform.SetParent(fluidGO.transform, false);
            glass.transform.localPosition = Vector3.zero;
            glass.transform.localScale = Vector3.one;          // = sim bounds
            glass.GetComponent<MeshRenderer>().sharedMaterial = Glass(new Color(0.55f, 0.8f, 1f, 0.12f));

            // Camera + controls.
            var targetGO = Child("CamTarget"); targetGO.transform.position = Vector3.zero;
            var camGO = Child("Main Camera"); camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.13f, 0.14f, 0.16f);
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.03f;
            camGO.AddComponent<AudioListener>();
            var rig = camGO.AddComponent<CameraRig>();
            rig.target = targetGO.transform;
            rig.orbitDistance = Mathf.Max(boxSize.x, boxSize.y) * 2.2f;

            var drag = Child("BoxDrag").AddComponent<BoxDragController>();
            drag.target = fluidGO.transform;
            drag.cam = cam;

            Child("SandboxHUD").AddComponent<FluidSandboxHUD>();

            _root.SetActive(true);
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            return go;
        }

        static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
        }

        static Material Opaque(Color c)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Glossiness", 0.1f);
            return m;
        }

        static Material Glass(Color c)
        {
            var m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Mode", 3); // Transparent
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            m.color = c;
            m.SetFloat("_Glossiness", 0.85f);
            m.SetFloat("_Metallic", 0.1f);
            return m;
        }

        static Gradient WaterGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.05f, 0.25f, 0.7f), 0f),
                        new GradientColorKey(new Color(0.4f, 0.85f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    /// <summary>Tiny on-screen help + stats for the sandbox.</summary>
    public sealed class FluidSandboxHUD : MonoBehaviour
    {
        FluidSim _sim;
        float _fps = 60f;

        void Update()
        {
            if (_sim == null) _sim = SceneRefs.FindFirst<FluidSim>();
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.08f);
        }

        void OnGUI()
        {
            int n = (_sim != null && _sim.positionBuffer != null) ? _sim.positionBuffer.count : 0;
            GUILayout.BeginArea(new Rect(8, 8, 320, 130), GUI.skin.box);
            GUILayout.Label("<b>Fluid Sandbox</b>  (independent SPH test)");
            GUILayout.Label($"FPS: {Mathf.RoundToInt(_fps)}    Particles: {n:N0}");
            GUILayout.Label("Move box: WASD / arrows, Q-E up/down, Shift faster");
            GUILayout.Label("Right-drag = move • X = shake • Left-drag = orbit cam");
            GUILayout.EndArea();
        }
    }
}
