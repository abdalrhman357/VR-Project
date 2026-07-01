# SwingingPaint — Original Mechanics Module

> **Authorship note.** Everything under `Assets/Scripts/SwingingPaint/` is written
> from scratch specifically for this graduation project ("محاكاة الرسم باستخدام
> الدلو المتأرجح"). It uses **no** Unity built-in physics
> (`Rigidbody`, `Collider`, `CharacterController`, `Cloth`, `Joint`, `Physics.*`)
> and **no** third-party simulation code. All dynamics are integrated by hand.
>
> This module deliberately lives in its own namespace (`SwingingPaint.*`) and is
> fully decoupled from the fluid renderer: it drives the fluid container purely
> through a `Transform` reference, so it does not depend on — and is not derived
> from — any other code in the repository.

## Why manual physics (the numerical foundation)

The whole rig (rope → suspension → bucket) is solved with **XPBD**
(Extended Position Based Dynamics, Macklin et al. 2016) plus a rigid-body
coupling in the style of *Detailed Rigid Body Simulation with XPBD*
(Müller et al. 2020).

XPBD was chosen over a raw force-based (stiff spring) integrator because:

* **Unconditional stability at stiff settings.** A steel cable has an enormous
  axial stiffness (EA/L ≈ 10⁸ N). A explicit spring at that stiffness explodes
  unless `dt` is microscopic. XPBD solves the constraint *positionally*, so a
  near-rigid cable and a stretchy rubber rope use the *same* stable loop — only
  the per-constraint **compliance** `α = 1/k` changes.
* **Physical stiffness units.** Compliance is `1/k`, so we can feed real
  material data (Young's modulus `E`, cross-section `A`, segment length `L₀`)
  directly:  `k = EA/L₀`,  `α = L₀/(EA)`.
* **Deterministic sub-stepping.** Accuracy scales with sub-step count, not with
  a fragile global stiffness knob.

## Module layout

| File | Responsibility | Physics |
|------|----------------|---------|
| `Core/NumericalIntegrator.cs` | Reusable, documented integrators (semi-implicit Euler, Verlet, RK4) + quaternion integration | Time integration |
| `Rope/RopeType.cs` | Enumeration of supported rope kinds | — |
| `Rope/RopeMaterial.cs` | Per-material physical constants + real-world presets, and the derived sim quantities (linear density, axial stiffness, XPBD compliance, breaking tension) | Continuum mechanics of a 1-D rod |
| `Rope/RopeSimulator.cs` | A chain of point masses solved with XPBD: stretch, bending, damping, air drag, breaking | Cosserat-lite rod / mass-spring |
| `Bucket/BucketBody.cs` | A hand-written 6-DOF rigid body: linear + angular state, cylinder inertia tensor, force/torque/impulse API, XPBD positional correction with generalized inverse mass | Newton–Euler rigid body |
| `Suspension/SuspensionAnchor.cs` | Attachment points (ceiling / wall / arbitrary / offset / multiple), pivot correction | Kinematic boundary |
| `PaintPendulumSystem.cs` | Orchestrator: owns the fixed-step XPBD loop, couples rope↔bucket, drives the fluid container transform, and handles all user interaction (grab/pull/rotate/release/push/force/impulse/torque) with hand-rolled ray math | Coupled system |

## Coupling algorithm (per fixed sub-step `h`)

```
save previous state (rope nodes + bucket)
predict rope free nodes:  v += h·(g + a_drag);  x += h·v
predict bucket:           v += h·(g + F_ext/m); x += h·v;  q ← integrate(q, ω, h)
repeat solverIterations:
    rope stretch constraints   (XPBD, compliance = L₀/(EA))
    rope bending constraints   (XPBD, compliance = 1/k_bend)
    attachment constraint      (rope tail  ↔  bucket handle point)   ← generates torque
    (optional) grab constraint (bucket grab point ↔ mouse target)
derive velocities: v = (x − x_prev)/h ;  ω = 2·vec(q·q_prevᐨ)/h
apply oscillation / internal-friction / air damping
```

The attachment constraint acts at the **top** of the bucket while gravity acts at
its **centre of mass**. That lever arm is what turns a pure translation into
torque, so the bucket naturally produces circular, elliptical, spiral and — at
large amplitude — chaotic motion, with no scripted animation anywhere.

## Integration with the existing scene

`PaintPendulumSystem.simulationContainer` should point at the `Transform` that
carries the fluid simulation. Each frame the system writes that transform's
position and rotation; the fluid, the bucket mesh and the collision bounds all
follow automatically. Disable/remove the old `CylinderDragger` so the two do not
fight over the transform.

See the header comment in each file for the exact equations and their sources.
