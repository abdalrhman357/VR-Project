using UnityEngine;
using Seb.Fluid.Simulation;

public class GPUFluidPainter : MonoBehaviour
{
    [Header("References")]
    public FluidSim fluidSimulation;
    public PaintCanvas paintCanvas;

    [Header("Paint Properties")]
    public Color PaintColor = Color.blue;
    [Range(0.1f, 10f)]
    public float Viscosity = 1.5f;
    [Range(0.001f, 0.05f)]
    public float DropMass = 0.008f;
    [Range(0.1f, 2f)]
    public float BrushSizeScale = 0.35f;

    void LateUpdate()
    {
        if (fluidSimulation == null || paintCanvas == null)
            return;

        var positions = fluidSimulation.positionBuffer;
        var velocities = fluidSimulation.velocityBuffer;

        if (positions != null && velocities != null && positions.count > 0)
        {
            // We pass -1 for brushSizeScale so it defaults to the PaintCanvas's own BrushSizeScale property
            paintCanvas.ProcessGPUHits(positions, velocities, positions.count, PaintColor, Viscosity, DropMass, -1f);
        }
    }
}
