# Phase 4 — Target Architecture of the Unified Project

Goal: one project that opens in **Unity 6000.4.4f1**, press Play, and runs — looking like it was
written by a single experienced engineer. No Unity built-in *physics* (no Rigidbody/Collider sim);
all motion is custom (Verlet rope + GPU SPH fluid).

## Assembly / namespace layout (`Assets/SwingingPaint/`)

```
SwingingPaint/
├─ Core/            SwingingPaint.Core        — config assets, math, fixed-step clock, service locator
│   ├─ SimulationConfig.cs        (ScriptableObject: gravity, air drag, sim speed, particle budget…)
│   ├─ FixedStepClock.cs          (accumulator → deterministic sub-steps)
│   └─ Events.cs                  (typed events: OnReset, OnPaused, OnPaintAmountChanged…)
│
├─ Rope/            SwingingPaint.Rope        — from Project B, hardened
│   ├─ VerletParticle.cs, DistanceConstraint.cs, BendingConstraint.cs, Rope.cs
│   ├─ RopeSimulator.cs           (MonoBehaviour driver, fixed-step, anchor + bucket attach)
│   └─ RopeRenderer.cs            (tube/line mesh)
│
├─ Bucket/          SwingingPaint.Bucket
│   ├─ BucketSimulator.cs         (rigid bucket body hung from rope tip; CoM shifts as paint drains)
│   └─ BucketRenderer.cs          (from Project C; driven by BucketSimulator, not FluidSim directly)
│
├─ Fluid/           SwingingPaint.Fluid       — from Project C (Seb.Fluid), retuned + hole-exit
│   ├─ Simulation/  FluidSim.cs, Spawner3D.cs, Compute/*.compute, *.hlsl
│   ├─ Helpers/     ComputeHelper, SpatialHash, GPUCountSort, Scan   (reused as-is)
│   ├─ Rendering/   ParticleDisplay3D.cs, shaders, foam
│   └─ PaintPreset.cs             (high-viscosity/cohesion preset asset)
│
├─ Canvas/          SwingingPaint.Canvas      — from Project A, decoupled + optimized
│   ├─ PaintCanvas.cs             (Color32 buffers, dirty-rect upload, injected params)
│   └─ CanvasHandoff.cs           (fluid impact → canvas: async readback or GPU stamp)
│
├─ Managers/        SwingingPaint.Managers    — NEW orchestration layer
│   ├─ SimulationManager.cs       (master: owns clock, drives rope→bucket→fluid→canvas order)
│   ├─ FluidManager.cs, RopeManager.cs, BucketManager.cs, CanvasManager.cs
│   └─ RenderingManager.cs        (pipeline-isolated: post-processing, quality)
│
├─ SceneBuild/      SwingingPaint.SceneBuild  — NEW auto scene construction (Part 6.5)
│   ├─ StudioSceneBuilder.cs      ([RuntimeInitializeOnLoadMethod] or Bootstrap GO)
│   ├─ EnvironmentBuilder.cs      (floor/walls/ceiling/easel/canvas/lighting/reflection probes)
│   └─ CameraRigBuilder.cs        (orbit/top/free/closeup + switcher)
│
├─ UI/              SwingingPaint.UI          — NEW control panel + stats (Part 7)
│   ├─ ControlPanel.cs            (all real-time sliders/buttons)
│   ├─ StatsPanel.cs              (FPS, particle count, memory, sim time, GPU/CPU est.)
│   └─ UIBuilder.cs               (builds the uGUI/UIToolkit hierarchy at runtime)
│
├─ IO/              SwingingPaint.IO          — NEW save/load/screenshot/export
│   ├─ SaveLoadSystem.cs          (JSON of SimulationConfig + scene state)
│   ├─ ScreenshotSystem.cs
│   └─ PaintingExporter.cs        (canvas RT → PNG/EXR)
│
└─ Editor/          SwingingPaint.Editor      — optional editor utilities
```

Each folder gets an **assembly definition** so compile units are isolated and dependency direction
is enforced: `Core ← Rope, Fluid, Canvas, Bucket ← Managers ← SceneBuild, UI, IO`.
Fluid keeps the `Seb.*` asmdefs from Project C unchanged (they already isolate the GPU sort/hash).

## Runtime data flow (one frame)

```
SimulationManager.FixedStepClock  ── accumulates dt → N sub-steps ──┐
                                                                    ▼
  for each sub-step:
    1. RopeSimulator.Step()      Verlet rope; anchor pinned, tip = bucket hang point
    2. BucketSimulator.Step()    bucket follows rope tip; rotation from tip velocity;
                                  CoM & mass updated from remaining paint volume  ──┐
                                                                                    │ feeds back
    3. FluidSim.Step()  (GPU)    bucket transform → sim transform; gravity/visc from config;
                                  particles inside bucket collide walls; particles past the
                                  hole plane are released to free-fall (region bounds)
    4. CanvasHandoff             particles crossing the canvas plane → stamp PaintCanvas
                                  (GPU compute stamp, or AsyncGPUReadback of "impacted" list)
  after sub-steps:
    ParticleDisplay3D renders particles; BucketRenderer + RopeRenderer draw; PaintCanvas.Apply()
    UI/Stats read manager state.
```

Paint mass leaving the bucket decreases `BucketSimulator` payload mass → pendulum period changes
over time (Part 5). This closes the loop the original projects never connected.

## Custom-physics compliance
- Rope: Verlet + PBD constraints (no Rigidbody). ✔
- Fluid: SPH on GPU (no Unity physics). ✔
- Bucket: kinematic body driven by rope tip + analytic torque from CoM offset; no Rigidbody. ✔
- Collisions: analytic plane/cylinder tests in compute & C#; no PhysX. ✔

## Scene auto-build strategy (Part 6.5)
A single `Bootstrap` GameObject in the one committed scene holds `StudioSceneBuilder`, which on
`Awake` builds the entire hierarchy in code (environment, lighting, reflection probes, camera rig,
managers, UI) and wires every reference programmatically. Result: the committed scene can be nearly
empty — there is nothing to drag, nothing to forget. This is the most robust way to guarantee
"no missing references, press Play."
