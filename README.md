# Swinging Paint Studio

A real-time, physically-believable **swinging paint bucket** simulation in Unity 6 — a suspended
bucket swings like a pendulum, high-viscosity paint flows from a hole in its bottom, free-falls, and
lands on a canvas, building up Lissajous / spiral / flower-like artwork like real pendulum paintings.

All motion is **custom** (no Unity Rigidbody/Collider physics): a Verlet rope drives the pendulum,
and a GPU compute-shader SPH solver drives the paint.

## How this project was built
It is the consolidation of three separate student projects. Each was analyzed independently and the
**best implementation of every subsystem was kept, hardened, and merged** — not a blind merge:

- **Fluid** ← *SPH* project (GPU SPH, spatial hash, GPU sort, instanced-indirect rendering)
- **Rope / pendulum** ← *Rope* project (Verlet + distance/bending constraints)
- **Bucket** ← *SPH* project's procedural `BucketRenderer`
- **Canvas / pigment** ← *Paint* project's `PaintCanvas` (subtractive CMY, canvas grain, drips)
- **Everything else** (managers, auto scene builder, control-panel UI, save/load, export, camera rig)
  is written fresh for this project.

See `Docs/` for the full engineering record:
1. `Docs/01_Analysis.md` — per-project analysis (Phases 1–2)
2. `Docs/02_Decision_Matrix.md` — best-per-subsystem selection + render-pipeline decision (Phase 3)
3. `Docs/03_Architecture.md` — unified architecture (Phase 4 design)
4. `Docs/04_Roadmap.md` — phased plan + self-validation checklist (Phases 4–10)

## Target
- Unity **6000.4.4f1**, Built-in Render Pipeline + Post-Processing Stack v2 (pending confirmation)
- Open the project, press **Play** — the studio scene auto-builds; nothing to assemble by hand.

## How to run
1. Open the project in Unity **6000.4.4f1** (let it import — it will regenerate `Library/` and the
   `com.unity.postprocessing` package).
2. Open `Assets/SwingingPaint/Scenes/SwingingPaintStudio.unity` (an empty scene by design).
3. Press **Play**. `StudioBootstrap` auto-builds the entire studio — environment, lights, GPU fluid,
   rope, bucket, canvas, camera rig, managers — with every reference wired in code.

**Controls:** mouse-drag = orbit, scroll = zoom, keys **1–4** = camera modes (Orbit/Top/Free/CloseUp),
Tab = cycle. (Space/R still toggle/reset the fluid from the original SPH controls.)

## Status
Phases 1–3 complete. Phase 4 complete (Core, Rope, Bucket, Canvas, Fluid paint-preset + hole-exit +
GPU→canvas hand-off). Phase 6 mostly complete (auto scene builder, environment, lighting, cameras,
managers). **Remaining:** control-panel UI + stats (Part 7), save/load/screenshot/export (IO),
post-processing polish, and first-run validation + SPH tuning inside the Unity editor.

### Known things to verify/tune on first open (honest caveats — built without a live Unity compile)
- **SPH stability at bucket scale:** `smoothingRadius`/`targetDensity`/`pressureMultiplier` vs particle
  count are scale-sensitive; tune via the (upcoming) control panel or the FluidSim inspector if the
  paint jitters or explodes.
- **GPU→canvas hand-off** (`DetectCanvasImpacts` + `FluidManager`) is the piece most needing a first run.
- If `Shader.Find` can't find the particle shaders in a *build*, add them to Project Settings → Graphics
  → Always Included Shaders. (In the editor, Play works without this.)
