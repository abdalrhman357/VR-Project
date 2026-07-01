# Algorithms & Mechanisms per Stage

For every stage: the algorithm chosen, *why it is the right / best-in-class choice*, and the file.
Short answer to "did you use the best algorithms?": **yes** — each subsystem uses the standard
state-of-the-art real-time technique for its job. The earlier problems were **parameter tuning** and
**release conditions** (a pendulum released from rest paints a straight line — physics, not a bug),
not the choice of algorithm.

---

## 1. Rope / Pendulum
**Algorithm:** Verlet integration + Position-Based Dynamics (PBD) constraints, on a fixed timestep.
- Each rope node is a point mass integrated by **Verlet** (`xₙ₊₁ = xₙ + (xₙ−xₙ₋₁)(1−c·dt) + a·dt²`).
- **Distance constraints** (Gauss–Seidel relaxation, N iterations) keep segment lengths → inextensible rope.
- **Bending constraints** (skip-one) resist kinks → realistic stiffness.
- **Fixed-step accumulator** (`FixedStepClock`) → identical, stable behaviour at any frame rate.
- **Conical-pendulum release:** an initial angular velocity ⟂ to the release angle makes the bob orbit
  (circle/ellipse); damping decays the orbit → **spiral** (the pendulum-painting / harmonograph pattern).

**Why best:** Verlet+PBD is the industry standard for ropes/cloth (Unity cloth, PBD papers, every game
rope). It is unconditionally stable, cheap, and handles variable mass (so the draining bucket changes the
pendulum). A full analytic spherical-pendulum ODE would be more "exact" but can't represent the physical
rope shape or coupling, and offers no real benefit at this scale.
**Files:** `Rope/VerletParticle.cs`, `DistanceConstraint.cs`, `BendingConstraint.cs`, `Rope.cs`,
`RopeSimulator.cs`, `Core/FixedStepClock.cs`.

## 2. Bucket
**Algorithm:** kinematic body driven by the rope tip; stays upright (translation only); variable payload
mass fed back to the rope (`payload = shell + remaining paint`) so the pendulum period evolves as it drains.
**Why best:** the bucket isn't a free rigid body — it's constrained to the rope, so a kinematic coupling is
both correct and stable (no Rigidbody needed, honouring the custom-physics rule). Upright keeps the fluid's
axis-aligned collision volume valid and makes the outlet trace a clean path.
**File:** `Bucket/BucketSimulator.cs` (+ procedural `BucketRenderer` with a depth-mask occlusion pass).

## 3. Paint inside the bucket (the fluid)
**Algorithm:** **SPH (Smoothed-Particle Hydrodynamics)**, fully GPU-resident:
- Predicted positions → **uniform-grid spatial hash** → **GPU counting sort** (radix) → **reorder** to
  contiguous memory (cache-friendly) → **density** (Spiky kernels) → **pressure + near-pressure**
  (Clavet-style, prevents clumping & keeps the stream coherent) → **viscosity** → integrate.
- Stability rule (now applied): `smoothingRadius ≈ 1.7 × spacing`, `targetDensity ≈ 1/spacing³`; we use
  the proven trio `0.2 / 630 / 288` with a fixed-spacing density spawn so it's stable at any bucket size.
**Why best:** SPH is *the* real-time method for free-surface liquids; spatial hashing + GPU sort + indirect
instanced rendering is the standard way to push it to 10⁵–10⁶ particles with **zero CPU readback**. This is
exactly Part 1's optimization list (spatial hashing, uniform grid, compute shaders, parallelism, cache
reorder). Grid-based (Eulerian/FLIP) solvers exist but are heavier to make watertight in an arbitrary
moving container.
**Files:** `Resources/SwingingPaint/Compute/FluidSim.compute` (+ `FluidMaths3D.hlsl`, `SpatialHash3D.hlsl`),
`Simulation/FluidSim.cs`, `Spawner3D.cs`, `Helpers/SpatialHash`, `GPU Sort/CountSort`, `Scan`.

## 4. Paint falling outside the bucket
**Algorithm:** the same SPH particles, but once a particle drops below the bottom cap it is **"released"**
(no wall/cap collision) → pure gravity free-fall, while near-pressure + viscosity keep the falling stream
cohesive (no random spray). Implemented in `ResolveCollisions` (the `released` branch).
**Why best:** reusing the SPH particles (instead of switching to ballistic points) preserves the liquid
look — droplets stay connected as a stream. No extra system, no copies.
**File:** `FluidSim.compute → ResolveCollisions`.

## 5. Collision with the canvas (the hand-off)
**Algorithm:** a dedicated GPU kernel `DetectCanvasImpacts` compares each particle's position this frame vs
last frame (`PrevPositions`, rolled on the GPU by `CopyPositionsToPrev`); when a particle **crosses the
canvas plane** front→back within bounds, the exact crossing point + velocity are appended to a small buffer.
The CPU reads that tiny list back with **AsyncGPUReadback** (non-blocking) and feeds `PaintCanvas.TryPaint`.
**Why best:** detection runs in parallel on the GPU over all particles; only the few impacts per frame
cross the PCIe bus, asynchronously — no stall, no full-buffer readback. Front→back crossing test guarantees
each particle paints **once** (no double-paint), without per-particle CPU identity tracking (which the GPU
sort would break anyway).
**Files:** `FluidSim.compute` (kernels), `Managers/FluidManager.cs`, `Canvas/PaintImpact.cs`.

## 6. Canvas colouring (the painting)
**Algorithm:** a CPU pigment model writing into a `Color32` texture (dirty-rect upload, no per-frame GC):
- **Subtractive CMY mixing** — overlapping colours mix like real pigment (red+blue→purple, not grey).
- **Baked multi-octave Perlin grain** — canvas weave: ridges absorb less, grooves more.
- **Thickness map** — accumulates per pixel; drives drip length, drying time, capillary spread.
- **Wet capillary spreading** — gravity-biased bleed, bounded work per frame.
- **Oriented elliptical brush** — the splat elongates along the impact velocity → real brush-stroke look.
**Why best:** a subtractive-pigment + grain + wet-diffusion model is what makes the result look like
*paint* rather than coloured dots; doing it on the CPU is fine because only the small impact set is painted
per frame. (A GPU stamp is a possible future optimization but would re-implement this model in HLSL.)
**File:** `Canvas/PaintCanvas.cs`.

## Cross-cutting optimizations
GPU-resident sim + indirect instanced rendering (no readback); cache-friendly particle reorder; spatial
hashing for O(1) neighbour queries; `Color32` + dirty-rect canvas upload; async readback for the hand-off;
fixed-timestep determinism; zero per-frame allocations in the hot paths.

## Evaluation tool
`Tools/FluidSandboxBuilder.cs` — a standalone **transparent, movable box of SPH liquid** (scene
`FluidSandbox`) to judge the fluid in isolation with the same settings, so tuning transfers to the studio.
