using UnityEngine;
using SwingingPaint.Core;
using SwingingPaint.Rope;
using SwingingPaint.Bucket;
using SwingingPaint.Canvas;
using Seb.Fluid.Simulation;

namespace SwingingPaint.Managers
{
    /// <summary>
    /// Master orchestrator. Owns the single shared <see cref="SimulationConfig"/>, distributes it to
    /// every subsystem, and drives the deterministic fixed-step loop in the correct order each frame:
    ///
    ///   for each fixed sub-step:  RopeSimulator.Step → BucketSimulator.Step
    ///   (the GPU FluidSim self-steps in its own Update; BucketSimulator has already placed/oriented
    ///    its volume, and runs earlier via execution order, so the fluid follows the swing this frame.)
    ///
    /// References are resolved automatically if not wired, so this works whether the scene was built by
    /// the auto scene builder or assembled by hand. Exposes Play/Pause/Reset/Speed for the control panel.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SimulationManager : MonoBehaviour
    {
        [SerializeField] SimulationConfig _config = new SimulationConfig();
        public SimulationConfig Config => _config;

        /// <summary>Inject the shared config before Awake (used by the scene builder). No-op afterwards is fine.</summary>
        public void SetConfig(SimulationConfig cfg) { if (cfg != null) _config = cfg; }

        [Header("Subsystems (auto-resolved if left empty)")]
        public RopeSimulator rope;
        public BucketSimulator bucket;
        public FluidSim fluidSim;
        public FluidManager fluidManager;
        public PaintCanvas canvas;

        public bool IsPaused { get; private set; }
        public float SimTime { get; private set; }
        public int StepsLastFrame { get; private set; }

        public event System.Action OnReset;
        public event System.Action<bool> OnPauseChanged;

        FixedStepClock _clock;

        void Awake()
        {
            ResolveReferences();
            DistributeConfig();
            _clock = new FixedStepClock(_config.world.fixedStep, _config.world.maxSubStepsPerFrame);
            InitializeAll();
        }

        void ResolveReferences()
        {
            if (rope == null) rope = SceneRefs.FindFirst<RopeSimulator>();
            if (bucket == null) bucket = SceneRefs.FindFirst<BucketSimulator>();
            if (fluidSim == null) fluidSim = SceneRefs.FindFirst<FluidSim>();
            if (fluidManager == null) fluidManager = SceneRefs.FindFirst<FluidManager>();
            if (canvas == null) canvas = SceneRefs.FindFirst<PaintCanvas>();
        }

        /// <summary>Hand the one shared config to every subsystem and take ownership of stepping.</summary>
        void DistributeConfig()
        {
            if (rope != null) { rope.Config = _config; rope.selfDrive = false; }
            if (bucket != null) { bucket.Config = _config; bucket.rope = rope; bucket.fluidSim = fluidSim; }
            if (fluidManager != null) { fluidManager.Config = _config; fluidManager.fluidSim = fluidSim; fluidManager.canvas = canvas; }
            if (canvas != null)
            {
                canvas.CanvasWidth = _config.canvas.size.x;
                canvas.CanvasHeight = _config.canvas.size.y;
                canvas.TextureRes = _config.canvas.textureResolution;
                canvas.AbsorptionRate = _config.canvas.absorptionRate;
                canvas.Humidity = _config.canvas.humidity;
                canvas.AutoCalculateTilt = _config.canvas.autoTilt;
            }
        }

        void InitializeAll()
        {
            rope?.Initialize();
            SimTime = 0f;
        }

        void Update()
        {
            if (IsPaused) return;

            _clock.Configure(_config.world.fixedStep, _config.world.maxSubStepsPerFrame);
            _clock.Accumulate(Time.deltaTime * _config.world.simulationSpeed);

            while (_clock.TryConsumeStep(out float dt))
            {
                rope?.Step(dt);
                bucket?.Step(dt);
                SimTime += dt;
            }
            StepsLastFrame = _clock.StepsLastFrame;

            PushLiveCanvasParams();
        }

        /// <summary>Feed the live pendulum state into the canvas brush scaling and surface params.</summary>
        void PushLiveCanvasParams()
        {
            if (canvas == null) return;
            canvas.ArmLength = _config.rope.length;
            canvas.SwingAmplitude = Mathf.Max(Mathf.Abs(_config.rope.initialAngleX),
                                              Mathf.Abs(_config.rope.initialAngleZ));
            canvas.AbsorptionRate = _config.canvas.absorptionRate;
            canvas.Humidity = _config.canvas.humidity;
        }

        // ── Control panel API ───────────────────────────────────────

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            OnPauseChanged?.Invoke(paused);
        }

        public void TogglePause() => SetPaused(!IsPaused);

        public void SetSpeed(float speed) => _config.world.simulationSpeed = Mathf.Clamp(speed, 0.05f, 2f);

        public void ResetSimulation()
        {
            _clock.Reset();
            SimTime = 0f;
            DistributeConfig();
            rope?.Initialize();
            if (bucket != null) bucket.RemainingPaintFraction = 1f;

            // Refill the bucket: move the fluid volume + spawn region back to the initial pose, respawn.
            if (fluidSim != null)
            {
                float hOverD = bucket != null ? bucket.heightOverDiameter : 1.3f;
                BucketSimulator.ConfigureInitialFluidPose(_config, fluidSim, fluidSim.spawner, hOverD);
                fluidSim.ResetParticles();
                var display = SceneRefs.FindFirst<Seb.Fluid.Rendering.ParticleDisplay3D>();
                if (display != null) display.ForceRebuild();
            }

            canvas?.Clear();
            OnReset?.Invoke();
        }
    }
}
