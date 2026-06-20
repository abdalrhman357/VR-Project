# Pendulum Simulation without Physics (SimulationCore)

This Unity project implements a pendulum simulation using Verlet Integration and SPH (Smoothed Particle Hydrodynamics) for fluid simulation, completely bypassing Unity's built-in physics engine.

## Setup Instructions

To set up the simulation in Unity, follow these steps:

### 1. Scene Hierarchy Setup
1. Create an empty GameObject named **SimulationManager** and attach the `SimulationManager.cs` script.
2. Create an empty GameObject named **Anchor** at position `(0, 5, 0)`.
3. Create a GameObject named **Bucket**.
   - Attach `RopeSimulator.cs`
   - Attach `BucketSimulator.cs`
   - Attach `FlowEmitter.cs`
4. Create a **Plane** (or Quad) named **Canvas** at position `(0, 0, 0)` with scale `(0.2, 1, 0.2)` (to make it roughly 2x2 meters).
   - Attach `PaintCanvas.cs`.

### 2. Component Configuration

#### RopeSimulator
- **AnchorWorldPosition**: `(0, 5, 0)`
- **SegmentCount**: `10`
- **TotalLength**: `2.5`
- **ConstraintIter**: `5`

#### FlowEmitter
- **ParticlePrefab**: Create a small Sphere prefab (scale `0.01`).
- **IMPORTANT**: Remove any `Rigidbody` or `Collider` components from the particle prefab.

#### PaintCanvas
- **TextureRes**: `1024`

#### SimulationManager
- Drag the **Rope**, **Bucket**, **Emitter**, and **Canvas** GameObjects into their respective fields in the `SimulationManager` component.
- (Optional) Set up UI Sliders and Buttons and link them to the fields in `SimulationManager`.

## Features
- **Verlet Integration**: Stable rope and particle movement.
- **SPH Solver**: Realistic paint flow with viscosity and surface tension.
- **Dynamic Center of Mass**: Pendulum period changes as paint empties.
- **Organic Splatters**: Perlin noise based painting on the canvas.
- **No Built-in Physics**: All calculations are handled in the provided scripts.
