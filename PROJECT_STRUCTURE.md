# Project Structure Overview

This Unity project is a fluid simulation and rendering demo. The main code and scene assets live under the Unity project root, while generated engine files are stored in Library/ and Temp/.

## Key folders

- README.md
  - Short project description and links to the simulation/rendering videos.

- Assets/
  - Main content for the project.
  - Scenes/ : Unity scene files for the different fluid demos (particles, marching cubes, raymarch, screen-space).
  - Scripts/ : Source code for the simulation and rendering pipeline.

- Assets/Scripts/Simulation
  - Core fluid simulation logic and particle spawning.
  - Main files: FluidSim.cs, Spawner3D.cs.

- Assets/Scripts/Rendering
  - Visual rendering approaches for the fluid output.
  - Includes foam, marching cubes, particles, raymarch, and screen-space rendering.

- Assets/Scripts/Helpers
  - Utility and support code for GPU compute, sorting, and spatial hashing.

- Assets/Scripts/Demo
  - Small demo helpers, such as camera movement.

- Packages/
  - Unity package manifest and lock file for project dependencies.

- ProjectSettings/
  - Unity editor/project configuration files.

- Library/, Temp/, UserSettings/
  - Generated Unity working files. These are not the main source of the project.

## What to read first

1. README.md for the high-level project purpose.
2. Assets/Scripts/Simulation for the simulation core.
3. Assets/Scripts/Rendering for how the fluid is displayed.
4. Assets/Scenes for the demo scenes that connect the code together.
