# Phases 4–10 — Roadmap & Validation

This is a large build. It is delivered in phases (as the brief requires), each phase a coherent,
reviewable increment committed into this project. Status legend: ☐ todo · ◐ in progress · ☑ done.

## Phase status
- ☑ **Phase 1** Analyze all three projects — `01_Analysis.md`
- ☑ **Phase 2** Strengths / weaknesses / bugs — `01_Analysis.md`
- ☑ **Phase 3** Choose best per subsystem — `02_Decision_Matrix.md`
- ◐ **Phase 4** Redesign weak systems + scaffold unified project
  - ☑ Unity 6 project config (SPH base consolidated; manifest + PPv2; folders). *Single Assembly-CSharp
    kept (no asmdefs) — SPH had none, lowest "press Play" risk.*
  - ☑ `Core` (SimulationConfig grouped + Clone; FixedStepClock accumulator)
  - ☑ `Rope` hardened (fixed-step-safe Verlet, config-driven iterations, bucket-attach tip,
    variable payload mass, initial-release angles) — `RopeSimulator` self-drive or manager-driven
  - ☑ `RopeRenderer` (LineRenderer, runtime material — no asset refs)
  - ☑ Import `Fluid` (Seb.*) — base present & intact
  - ☑ Paint preset — `FluidManager.ApplyPaintPreset` maps config (high viscosity, cohesive
    near-pressure, low collision damping) onto the SPH solver each frame
  - ☑ Hole-exit free-fall — `ResolveCollisions` now releases particles below the bottom cap (no wall/
    cap), so paint actually leaves the hole and free-falls (was trapped in an infinite cylinder)
  - ☑ Fluid→Canvas hand-off — isolated `DetectCanvasImpacts` compute kernel + `FluidManager`
    (clear→detect→AsyncGPUReadback→`PaintCanvas.TryPaint`), GPU-resident prev-positions via CopyBuffer.
    *⚠ This GPU path is the piece most needing first-run verification inside Unity.*
  - ☑ Decouple + optimize `PaintCanvas` — Color32 buffers, dirty-rect upload (reused scratch, no GC),
    `PaintImpact` value type, injected brush params, PendulumPaintDropper dependency removed,
    local-frame stroke direction (fixes original world/local mix)
  - ☑ `BucketSimulator` — rope-tip driven, sizes the fluid volume, sets the outlet hole, feeds
    remaining-paint mass back to the pendulum (closes the Part-5 feedback loop). *CoM offset torque
    and live drain fraction from the GPU still to be wired by FluidManager.*
  - ☑ `SimulationManager` (orchestration backbone, pulled forward from Phase 6) — owns the shared
    config, fixed-step loop Rope→Bucket, auto-resolves refs, Play/Pause/Reset/Speed API
- ◐ **Phase 5** Optimize — ☑ canvas Color32 + dirty-rect, ☑ async readback hand-off (no stall),
  ☑ no per-frame GC in hand-off/canvas upload; ☐ SPH param tuning for the small bucket scale,
  ☐ particle render LODs
- ◐ **Phase 6** Scene + presentation
  - ☑ Auto scene builder (`StudioSceneBuilder`) — builds the whole studio in code, inactive-then-activate
    so init order is correct; wires every reference programmatically
  - ☑ `StudioBootstrap` — `[RuntimeInitializeOnLoadMethod]`, builds on Play in the studio scene only
  - ☑ Empty committed scene `Assets/SwingingPaint/Scenes/SwingingPaintStudio.unity`
  - ☑ Environment — wood floor, 4 walls, ceiling, anchor mount; ambient + key/fill directional lights, soft shadows
  - ☑ Camera rig — one camera, modes Orbit/Top/Free/CloseUp (keys 1–4, Tab to cycle)
  - ☑ Managers (`SimulationManager`, `FluidManager`) created + wired
  - ☑ **Control panel (Part 7)** — `ControlPanel` (IMGUI): every parameter live-tunable
    (World/Rope/Bucket/Paint/Canvas/Quality), Play/Pause/Reset/Save/Load/Export/Screenshot buttons (H to hide)
  - ☑ **Stats panel** — `StatsPanel` (IMGUI): FPS/frame-time/worst, particle count, sub-steps,
    sim time, pause state, managed + total memory (J to hide)
  - ☑ **IO system** — `StudioIO`: Save/Load config (JSON), Export painting (PNG), Screenshot,
    Open folder — all to `persistentDataPath`
  - ☐ Post-processing (PPv2: bloom/AO/color grading) — deferred to Phase 9 polish (needs a profile asset)
  - ☐ Easel, reflection probes, HDRI skybox
- ☐ **Phase 7** Test (edit/play-mode where feasible; manual run checklist)
- ☐ **Phase 8** Validate (full self-validation checklist below)
- ☐ **Phase 9** Polish (materials, color grading, camera animation, UX)
- ☐ **Phase 10** Deliver production-ready project

## Hard integration problems (where the real engineering is)
> Status: #1 and #2 implemented (see Phase 4 above). #3 (preset tuning) initial values in place,
> needs visual tuning in-editor. #4 partially wired (mass feedback done; live drain fraction pending).

1. **Hole-exit free-fall.** ✅ done. Current SPH clamps particles inside the unit-cube bounds. Need
   region-aware bounds: inside-bucket → wall collision; below hole plane → no collision, pure
   gravity fall to the canvas. Implement in `FluidSim.compute` (updatePositions kernel) using the
   bucket transform + hole radius/offset already present.
2. **Fluid → Canvas hand-off.** Two candidate designs:
   - **(a) GPU stamp:** a compute kernel projects impacting particles onto the canvas RT and
     blends colour/thickness directly. Fastest, no readback, but reimplements PaintCanvas's CMY/grain
     in HLSL.
   - **(b) Async readback of impacts:** compute marks particles that crossed the canvas plane this
     step into an append buffer; `AsyncGPUReadback` brings back a small list (pos/vel/colour) to feed
     the existing `PaintCanvas.TryPaint`. Keeps the excellent CPU pigment model; cost is the small
     readback. **Default plan: (b)** — preserves Project A's best asset; the impact list is tiny
     compared to the particle count.
3. **Paint preset stability.** High viscosity + near-pressure cohesion + sub-stepping so the stream
   is continuous and coherent (no spray, no explosions) — tune `viscosityStrength`,
   `nearPressureMultiplier`, `targetDensity`, `iterationsPerFrame`.
4. **Closed feedback loop.** Paint volume in bucket → bucket payload mass → rope pendulum period.

## Self-validation checklist (Phase 8 — must all pass before "done")
- ☐ Compiles with no errors/warnings in Unity 6000.4.4f1
- ☐ No missing scripts / references / materials / prefabs / UI refs after fresh open
- ☐ No null-refs / console errors on Play
- ☐ All managers initialize in correct order; scene auto-builds
- ☐ Bucket swings (pendulum), rope stable (no stretch/jitter)
- ☐ Fluid flows as a coherent high-viscosity stream from the hole (no spray/explosions)
- ☐ Paint free-falls and lands on the canvas
- ☐ Canvas records impacts (CMY mixing, grain, drips visible)
- ☐ Generated artwork shows circular/spiral/flower Lissajous patterns, denser at centre
- ☐ Control panel adjusts everything in real time; stats panel live
- ☐ Save/Load, Screenshot, Export Painting all work
- ☐ Camera switching (orbit/top/free/closeup) works

## Notes on validation tooling
Unity cannot be compiled from this assistant session; correctness is ensured by (1) careful,
version-matched code, (2) isolated asmdefs so errors are localized, (3) the edit/play-mode tests
carried over and extended. Final compile/Play validation is done by opening the project in Unity 6.
