# Phase 3 — Subsystem Selection (Decision Matrix)

"Keep only the best implementation per subsystem, then improve it." Scores are 1–5.

| Subsystem | Paint | Rope | SPH | **Winner** | Action |
|-----------|:----:|:----:|:----:|-----------|--------|
| Fluid simulation | 1 (CPU toy) | – | **5** (GPU SPH) | **SPH** | Add paint preset + hole-exit free-fall + canvas hand-off |
| Rope / pendulum | 2 (kinematic fake) | **5** (Verlet+constraints) | – | **Rope** | Fixed-timestep, config-driven, bucket attach + variable payload mass |
| Bucket | – | – | **4** (`BucketRenderer`) | **SPH** | Couple to rope tip; correct CoM, hole, occlusion |
| Canvas / painting | **5** (CMY, grain, drips) | – | – | **Paint** | Decouple, `Color32`, dirty-rect upload, GPU hand-off |
| Particle rendering | 1 | – | **5** (instanced indirect) | **SPH** | Reuse; add paint shading |
| GPU infra (hash/sort/helpers) | – | – | **5** | **SPH** | Reuse as-is |
| Orchestration / managers | 0 | 0 | 0 | **none** | Build new (Phase 4) |
| Scene auto-builder | 0 | 0 | 0 | **none** | Build new (Phase 6) |
| Control-panel UI / stats | 0 | 0 | 0 | **none** | Build new (Phase 7) |
| Save / load / export | partial (PNG) | 0 | 0 | **Paint (PNG only)** | Build new full system |
| Cameras | – | – | 3 (`OrbitCam`) | **SPH** | Reuse + add rig (top/free/orbit/closeup) |

## Final selection
- **Fluid** → SPH (Project C), retuned for paint + made to exit the hole and free-fall.
- **Rope/Pendulum** → Rope (Project B), hardened to fixed-timestep and coupled to the bucket.
- **Bucket** → SPH `BucketRenderer` (Project C), driven by the rope tip.
- **Canvas** → PaintCanvas (Project A), decoupled and optimized.
- **Everything else** (managers, scene builder, UI, save/load, export, camera rig) → **new**, written
  to look like one engineer's work.

---

## Render-pipeline decision (the one expensive fork)

All existing rendering is **Built-in RP**. Two viable paths:

### Option 1 — Built-in RP + Post-Processing Stack v2  ✅ recommended default
- **Pros:** every existing shader works unchanged (SPH surface shader, bucket `Standard`, depth-mask
  trick, instanced-indirect `procedural:setup`). Lowest risk of "doesn't compile / doesn't run".
  PPv2 still delivers Bloom, AO, Color Grading, Tonemapping, DoF, Vignette — covers Part 6's list.
- **Cons:** not as turnkey-pretty as URP's defaults; PPv2 is older.
- **Best when:** the #1 hard requirement is *"open project, press Play, no errors."*

### Option 2 — URP (Universal Render Pipeline)
- **Pros:** integrated Volume post-processing, better default lighting/shadows, more "production demo"
  look, future-proof.
- **Cons:** **must rewrite** the particle surface shader (URP has no surface shaders → hand-written
  HLSL or Shader Graph), the bucket depth-mask shader, all materials, and re-validate the
  instanced-indirect path under SRP. Materially higher risk of pink/broken materials on first open.

### Option 3 — HDRP — **not recommended.** Highest fidelity, but custom instanced-indirect + millions
of particles + bottom-hole sim is the hardest to make "just work," and is overkill for a studio scene.

**Default chosen pending your confirmation: Option 1 (Built-in RP + PPv2).** It is the only path that
honors the absolute requirement "no manual setup, press Play, everything works" while still hitting
the post-processing checklist. If you prefer the URP look and accept a shader-rewrite pass, say so and
the architecture (below) already isolates rendering behind a `RenderingManager` so the swap is contained.
