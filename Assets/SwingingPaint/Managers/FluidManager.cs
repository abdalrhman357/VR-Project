using UnityEngine;
using UnityEngine.Rendering;
using Seb.Fluid.Simulation;
using Seb.Helpers;
using SwingingPaint.Core;
using SwingingPaint.Canvas;

namespace SwingingPaint.Managers
{
    /// <summary>
    /// Owns the GPU fluid: pushes the high-viscosity "paint" preset onto the FluidSim each frame, and
    /// performs the fluid→canvas hand-off. The hand-off uses the isolated DetectCanvasImpacts compute
    /// kernel: every frame it clears the impact buffers, detects particles that crossed the canvas plane
    /// (comparing against last frame's positions, kept on the GPU via a copy kernel), reads the
    /// small impact list back asynchronously, and feeds each crossing to <see cref="PaintCanvas.TryPaint"/>.
    /// Nothing here blocks the GPU; if anything is unavailable the fluid still runs untouched.
    /// </summary>
    [DefaultExecutionOrder(-150)] // before FluidSim.Update so preset params are applied this frame
    public sealed class FluidManager : MonoBehaviour
    {
        public FluidSim fluidSim;
        public PaintCanvas canvas;

        [SerializeField] SimulationConfig _config;
        public SimulationConfig Config { get => _config; set => _config = value; }

        [Header("Paint preset mapping")]
        [Tooltip("Scales config paint viscosity (0..30) into the SPH viscosityStrength. Higher = thicker/calmer, but slower to start draining.")]
        public float viscosityScale = 0.035f;
        [Tooltip("Low damping → paint doesn't bounce off the bucket walls (calmer).")]
        [Range(0f, 1f)] public float paintCollisionDamping = 0.25f;
        [Tooltip("Near-pressure cohesion. Demo-proven 2.16; higher = more cohesive but risk instability.")]
        public float paintNearPressure = 2.16f;

        [Header("Hand-off")]
        public bool handoffEnabled = true;
        public int maxImpactsPerFrame = 4096;
        [Tooltip("Representative mass per impacting particle (drives canvas brush size).")]
        public float impactMass = 0.012f;

        // GPU buffers for the hand-off.
        ComputeBuffer _prevPositions;
        ComputeBuffer _impacts;
        ComputeBuffer _counter;
        int _detectKernel = -1;
        int _copyPrevKernel = -1;
        int _numParticles;
        bool _ready;

        // CPU-side scratch (allocated once → no per-frame GC).
        ImpactGPU[] _sentinelClear;
        readonly uint[] _counterClear = { 0u };
        bool _readbackPending;

        struct ImpactGPU { public Vector3 position; public Vector3 velocity; }

        const float Sentinel = 1e30f;

        void Awake()
        {
            ResolveRefs();
            if (fluidSim != null)
            {
                fluidSim.SimulationInitCompleted += OnFluidInit;
                if (fluidSim.positionBuffer != null) OnFluidInit(fluidSim); // already initialized
            }
        }

        void OnDestroy()
        {
            if (fluidSim != null) fluidSim.SimulationInitCompleted -= OnFluidInit;
            ReleaseBuffers();
        }

        void ResolveRefs()
        {
            if (fluidSim == null) fluidSim = Core.SceneRefs.FindFirst<FluidSim>();
            if (canvas == null) canvas = Core.SceneRefs.FindFirst<PaintCanvas>();
        }

        void OnFluidInit(FluidSim sim)
        {
            if (sim.compute == null || sim.positionBuffer == null) return;
            ReleaseBuffers();

            _numParticles = sim.positionBuffer.count;
            _detectKernel = sim.compute.FindKernel("DetectCanvasImpacts");
            _copyPrevKernel = sim.compute.FindKernel("CopyPositionsToPrev");

            _prevPositions = new ComputeBuffer(_numParticles, sizeof(float) * 3);
            _impacts = new ComputeBuffer(Mathf.Max(1, maxImpactsPerFrame), sizeof(float) * 6);
            _counter = new ComputeBuffer(1, sizeof(uint));

            _sentinelClear = new ImpactGPU[_impacts.count];
            for (int i = 0; i < _sentinelClear.Length; i++)
                _sentinelClear[i].position = new Vector3(Sentinel, Sentinel, Sentinel);

            var c = sim.compute;
            c.SetBuffer(_detectKernel, "Positions", sim.positionBuffer);
            c.SetBuffer(_detectKernel, "Velocities", sim.velocityBuffer);
            c.SetBuffer(_detectKernel, "PrevPositions", _prevPositions);
            c.SetBuffer(_detectKernel, "CanvasImpacts", _impacts);
            c.SetBuffer(_detectKernel, "CanvasImpactCounter", _counter);
            c.SetInt("maxCanvasImpacts", _impacts.count);

            // Bind the GPU position-roll copy kernel (replaces Graphics.CopyBuffer, which needs a
            // GraphicsBuffer in Unity 6 while FluidSim uses ComputeBuffers).
            c.SetBuffer(_copyPrevKernel, "Positions", sim.positionBuffer);
            c.SetBuffer(_copyPrevKernel, "PrevPositions", _prevPositions);
            c.SetInt("numParticles", _numParticles);

            // Seed prev positions = current so the first frame produces no spurious crossings.
            ComputeHelper.Dispatch(c, _numParticles, kernelIndex: _copyPrevKernel);

            _ready = true;
        }

        void Update()
        {
            ApplyPaintPreset();
        }

        /// <summary>Map the config paint parameters onto the SPH solver (high viscosity, cohesive, low bounce).</summary>
        void ApplyPaintPreset()
        {
            if (fluidSim == null || _config == null) return;
            var w = _config.world;
            var p = _config.paint;

            fluidSim.gravity = -Mathf.Abs(w.gravity);
            fluidSim.normalTimeScale = w.simulationSpeed;
            fluidSim.viscosityStrength = Mathf.Clamp(p.viscosity * viscosityScale, 0f, 1.5f);
            fluidSim.nearPressureMultiplier = paintNearPressure;
            fluidSim.collisionDamping = paintCollisionDamping;
            fluidSim.iterationsPerFrame = Mathf.Max(1, _config.quality.solverIterationsPerFrame);
            fluidSim.recycleParticles = p.continuousSource;   // Continuous (recycle) vs Drain (empties)

            // Keep the rendered particle colour in sync with the live paint-colour slider.
            if (_display == null) _display = Core.SceneRefs.FindFirst<Seb.Fluid.Rendering.ParticleDisplay3D>();
            if (_display != null) _display.tint = p.color;
        }

        Seb.Fluid.Rendering.ParticleDisplay3D _display;

        void LateUpdate()
        {
            if (!_ready || !handoffEnabled || fluidSim == null || canvas == null) return;

            var c = fluidSim.compute;

            // Build the canvas plane in world space from the PaintCanvas transform.
            Transform ct = canvas.transform;
            Vector3 nLocal = canvas.PaintNormalLocal.normalized;
            Vector3 upRef = Mathf.Abs(Vector3.Dot(nLocal, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
            Vector3 tU = Vector3.Cross(nLocal, upRef).normalized;
            Vector3 tV = Vector3.Cross(tU, nLocal).normalized;

            c.SetInt("canvasActive", 1);
            c.SetVector("canvasCentre", ct.position);
            c.SetVector("canvasNormal", ct.TransformDirection(nLocal).normalized);
            c.SetVector("canvasTangentU", ct.TransformDirection(tU).normalized);
            c.SetVector("canvasTangentV", ct.TransformDirection(tV).normalized);
            c.SetVector("canvasHalfSize", new Vector4(canvas.CanvasWidth * 0.5f, canvas.CanvasHeight * 0.5f, 0, 0));
            c.SetInt("numParticles", _numParticles);

            // Clear impact list + counter, detect crossings, read back asynchronously.
            _impacts.SetData(_sentinelClear);
            _counter.SetData(_counterClear);
            ComputeHelper.Dispatch(c, _numParticles, kernelIndex: _detectKernel);

            if (!_readbackPending)
            {
                _readbackPending = true;
                AsyncGPUReadback.Request(_impacts, OnImpactsReadback);
            }

            // Roll positions forward for next frame's crossing test (GPU copy kernel).
            ComputeHelper.Dispatch(c, _numParticles, kernelIndex: _copyPrevKernel);
        }

        void OnImpactsReadback(AsyncGPUReadbackRequest req)
        {
            _readbackPending = false;
            if (req.hasError || canvas == null || _config == null) return;

            var data = req.GetData<ImpactGPU>();
            float visc = _config.paint.viscosity;
            Color col = _config.paint.color;

            for (int i = 0; i < data.Length; i++)
            {
                // Used slots are filled contiguously from 0; stop at the first sentinel.
                if (data[i].position.x >= Sentinel * 0.5f) break;
                canvas.TryPaint(new PaintImpact(data[i].position, data[i].velocity, col, impactMass, visc));
            }
        }

        void ReleaseBuffers()
        {
            _ready = false;
            _prevPositions?.Release(); _prevPositions = null;
            _impacts?.Release(); _impacts = null;
            _counter?.Release(); _counter = null;
        }
    }
}
