# Phase 1 & 2 — Independent Analysis of the Three Projects

> Method: every conclusion below comes from reading the actual source, not the READMEs.
> Each project is judged on: strengths, weaknesses, bugs, missing parts, code quality,
> architecture, performance, scalability, maintainability.

Unity versions found:

| Project | Unity version | Render pipeline | Nature |
|---------|---------------|-----------------|--------|
| **Paint** | 6000.4.3f1 (Unity 6) | Built-in RP | CPU canvas painting + a test pendulum dropper |
| **Rope**  | 2022.3.3f1 (LTS) | Built-in RP | Pure-C# Verlet rope (no Unity-version APIs) |
| **SPH**   | 6000.4.4f1 (Unity 6) | Built-in RP | GPU compute SPH fluid (Sebastian-Lague lineage) |

Two of three are already Unity 6; the Rope is pure C# and ports to Unity 6 with zero changes.
**Unified target: Unity 6000.4.4f1, Built-in RP** (see `02_Decision_Matrix.md` for the pipeline rationale).

---

## PROJECT A — "Paint"

Files that actually exist (the README describes `SimulationManager`, `RopeSimulator`,
`BucketSimulator`, `FlowEmitter` — **none of these were ever written**; only the canvas and a
test dropper exist):

- `Canvas/PaintCanvas.cs` (31 KB) — the real asset of this project
- `Canvas/PaintCanvasVisualTester.cs` → class `PendulumPaintDropper`
- `Fluid/FluidParticleData.cs`
- `Editor/PaintCanvasSceneSetup.cs`
- EditMode + PlayMode tests

### Strengths
- **`PaintCanvas.cs` is genuinely excellent** — production-grade pigment simulation:
  - **Subtractive CMY mixing** (`SubtractiveMix`) — red+blue → real purple, not muddy grey.
    This is the correct model for paint and is rare to see done properly.
  - **Baked multi-octave Perlin canvas grain** → grooves absorb more, ridges less.
  - **Thickness map** that feeds drip length, drying time, and capillary spreading.
  - **Wet-spreading** with a gravity-biased capillary bleed, processed in a bounded budget
    per frame (`maxProcess = 120`) so it never stalls the frame.
  - **Oriented elliptical brush** — stroke elongates along particle velocity (`aspect` from speed),
    which is exactly what produces the "brush-stroke" look the target images need.
  - Drying tracked via a `HashSet<int> _wetIndices` → O(wet) not O(1M). Smart.
  - Has unit tests (UV mapping, particle data) and PlayMode tests.
- The `PendulumPaintDropper` already implements a **2-axis (X/Z) damped pendulum** producing
  Lissajous / spiral / figure-8 paths — conceptually the right driver for the target artwork,
  and it even previews the path with gizmos.

### Weaknesses / Bugs
- **`FluidParticleData` is a `class`, not a `struct`** (the comment literally says "struct للأداء"
  but it is a reference type) → one heap allocation per drop, GC churn. At scale this is fatal.
- **One `GameObject` + `Sphere` primitive per drop** (`CreateDropSphere`) with `Destroy`/`Instantiate`
  every hit → unscalable; hundreds of drops max before GC spikes. No pooling.
- `Texture2D.SetPixels`/`Apply()` over the **entire** 1024² array every dirty frame
  (`LateUpdate`) — uploads 4 MB even when 3 pixels changed. Should use dirty-rect or
  `SetPixels32` on a sub-region.
- `StartCoroutine(DrawDrip…)` per impact → unbounded coroutine fan-out under heavy painting.
- `_pixels`/`_wetPixels`/`_wetTimer`/`_thicknessMap`/`_grainMap` are 5 full-res managed arrays
  (5 × 4 MB at 1024²) — heavy, and `Color[]` (16 B/px) instead of `Color32[]` (4 B/px).
- The painting is **2-D only** — drops are integrated by a toy `v += g·dt` in the dropper, not by
  any real fluid; the "SPH" in the README is aspirational here.
- Tight coupling: `PaintCanvas` does `FindAnyObjectByType<PendulumPaintDropper>()` in `Awake`
  to read `ArmLength`/amplitude — a hidden cross-dependency that breaks modularity.

### Verdict
- **Keep & refactor `PaintCanvas`** — it is the best canvas/pigment implementation of the three
  subsystems and the hardest to reproduce. Decouple it (inject parameters instead of `FindObject`),
  switch buffers to `Color32`, add dirty-rect upload, optionally move blending to a compute shader.
- **Discard** the `FluidParticleData` class and the GameObject-per-drop dropper — they are replaced
  by the GPU SPH fluid from Project C. Keep the dropper's **pendulum math** as reference only;
  the real swing comes from the rope (Project B) + bucket.

---

## PROJECT B — "Rope"

Files: `Rope.cs`, `VerletParticle.cs`, `DistanceConstraint.cs`, `BendingConstraint.cs`,
`ConstraintTest.cs`, `RopeTest.cs`, `VerletTest.cs`.

### Strengths
- **Clean, correct Verlet integration** with inverse-mass handling and pinned particles
  (`InverseMass = 0` for anchors). Textbook-correct PBD/Verlet.
- **`DistanceConstraint`** supports stiffness + a `MaxStretch` safety clamp (rigid correction only
  past the limit) — a nice touch that prevents over-stretch without going fully rigid.
- **`BendingConstraint`** (skip-one particle) gives the rope realistic stiffness/curvature.
- **Material presets** (`Rigid/Cotton/Nylon/Rubber`) map cleanly to stiffness + max-stretch.
- Zero external dependencies — pure `UnityEngine`, so it drops into Unity 6 untouched.
- Has tests.

### Weaknesses / Bugs
- **Hard-coded magic numbers**: `13` constraint iterations in `Rope.Simulate`, heavy bottom mass
  `10f`, `dampingFactor = 0.1f` baked into the particle. Should be config-driven.
- **Damping is frame-rate dependent**: `velocity * (1 - damping*dt)` mixes a per-step factor with
  `dt`; combined with `Position += gravity*dt*dt` it is *not* a fixed-timestep-safe integrator.
  Needs a fixed sub-step accumulator for stability across machines.
- `DistanceConstraint.Solve` **only corrects positive error** (`if (error < 0) return;`) — the rope
  resists stretching but never compression, which is fine for a rope but should be a deliberate flag.
- `Rope` is a plain C# class with **no MonoBehaviour driver committed** (the test scripts drive it) —
  there is no bucket/anchor integration, no rendering beyond tests.
- No collision, no air drag term (only linear damping), no coupling to a payload mass that changes
  over time (needed when the bucket loses paint).

### Verdict
- **Keep & promote this as the rope/pendulum core.** It is the cleanest code in the three projects.
  Refactor: pull constants into a config, add a **fixed-timestep accumulator**, expose the bottom
  particle as the **bucket attach point**, and let the bucket feed back its (decreasing) mass.
  Add a `LineRenderer`/tube mesh for visuals.

---

## PROJECT C — "SPH"

A near-production **GPU SPH fluid** (namespaces `Seb.Fluid.*`, `Seb.GPUSorting`, `Seb.Helpers` —
Sebastian Lague's fluid framework) that someone has **already extended for the paint-bucket use case**.

Files of note: `Simulation/FluidSim.cs` + `Compute/FluidSim.compute` (22 KB) + `FluidMaths3D.hlsl`
+ `SpatialHash3D.hlsl`; `Spawner3D.cs`; GPU `SpatialHash` + `GPUCountSort` + `Scan`;
`Rendering/ParticleDisplay3D.cs` (instanced-indirect), foam system, and a **custom `BucketRenderer.cs`**.

### Strengths
- **Fully GPU-resident SPH**: external forces → spatial-hash build → GPU count-sort → reorder for
  cache locality → density → pressure + near-pressure → viscosity → integrate. This is the
  state-of-the-art real-time approach and directly satisfies Part 1's optimization list
  (spatial hashing, uniform grid, compute shaders, parallelism, cache-friendly reorder).
- **`Graphics.DrawMeshInstancedIndirect`** rendering → millions of particles, **zero CPU readback**.
- **Near-pressure term** (Clavet-style) keeps the fluid coherent and prevents particle clumping —
  exactly what "paint stays together, no spray" needs.
- **Already has a "Bottom Hole" feature** (`holeRadius`, `holeOffset`) plumbed from `FluidSim.cs`
  into the compute shader — i.e. someone purpose-built the bucket outlet. Huge head start.
- **`BucketRenderer.cs`** procedurally builds a watertight bucket mesh (inner/outer walls, rim,
  holed bottom) and uses a **two-pass depth-mask trick** (queue 1999 mask + queue 2001 visual) so
  particles inside the bucket are correctly occluded. Genuinely clever and reusable.
- Foam/spray/bubble classification, optional 3-D density texture for volumetric rendering.
- Good helper layer (`ComputeHelper`) for buffer/dispatch management.

### Weaknesses / Bugs
- **Tuned as water, not paint**: `viscosityStrength = 0` by default, `targetDensity 630`,
  `pressureMultiplier 288` → splashy water. Needs a high-viscosity, high-cohesion preset.
- **Sim space is the unit cube of `transform.localScale`** with hard wall collisions + damping;
  it is a *container* sim, not a free-falling stream onto a distant canvas. To paint, particles
  must be allowed to **leave the bucket through the hole and free-fall** to the canvas — the current
  bounds clamp them inside. This is the single biggest integration gap.
- Input handling baked into `FluidSim.Update` (`Space/Q/R` keys) — should be in a manager.
- Public mutable buffers, `[HideInInspector]`-style coupling between sim and renderer via direct
  field access; no clean interface. Fine for a demo, needs light decoupling for a managed app.
- No CPU path to know *where* a particle is when it hits the canvas (painting needs impact
  position/velocity/colour on the CPU, or a GPU→canvas compute path).
- Recovery scenes (`_Recovery/*.unity`) and duplicate scene files — housekeeping debt.

### Verdict
- **Keep as the fluid core — this is the heart of the final project.** It is the strongest, most
  scalable subsystem by a wide margin and already half-adapted to the bucket.
  Work required: (1) a **paint preset** (high viscosity, high cohesion, low splash); (2) let
  particles **exit the hole and free-fall** (region-based bounds: bucket interior collides, world
  below the hole does not); (3) a **canvas hand-off** — either a GPU compute kernel that stamps the
  canvas RT directly, or an async-readback of impacting particles to feed `PaintCanvas`.
  Pull input/lifecycle out into the Simulation Manager.

---

## Cross-cutting observations

- **All three are Built-in RP.** No URP/HDRP anywhere. The SPH particle shader is a `surface`
  shader and the bucket uses `Standard` — both Built-in only. Moving to URP/HDRP means rewriting
  every shader and the instanced-indirect setup. (Pipeline decision: `02_Decision_Matrix.md`.)
- **Custom-physics constraint is respected** by the best implementations: the rope is hand-rolled
  Verlet (no Rigidbody), the fluid is hand-rolled SPH (no Unity physics). The only Unity-physics
  usage to purge is the leftover `Destroy(GetComponent<Collider>())` calls in the Paint dropper
  (which we discard anyway) and the `com.unity.modules.physics` dependency (kept only because UI
  raycasting/modules pull it; no `Rigidbody`/`Collider` simulation is used).
- **None of the three has the orchestration layer** the final project needs: no unified manager,
  no auto scene builder, no control-panel UI, no save/load/export, no camera rig. That entire
  layer is greenfield (Phases 4–6) and is where most new code will live.
