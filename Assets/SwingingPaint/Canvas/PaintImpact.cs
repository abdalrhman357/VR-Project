using UnityEngine;

namespace SwingingPaint.Canvas
{
    /// <summary>
    /// Lightweight value type describing one paint particle striking the canvas. This is the contract
    /// between the fluid→canvas hand-off and <see cref="PaintCanvas"/>, replacing the old reference-type
    /// FluidParticleData (which caused per-drop heap allocations). Carries world-space pose plus the
    /// pigment properties the canvas needs to render a believable stroke.
    /// </summary>
    public readonly struct PaintImpact
    {
        public readonly Vector3 WorldPosition;
        public readonly Vector3 WorldVelocity;
        public readonly Color Color;
        public readonly float Mass;
        public readonly float Viscosity;

        public PaintImpact(Vector3 worldPosition, Vector3 worldVelocity, Color color, float mass, float viscosity)
        {
            WorldPosition = worldPosition;
            WorldVelocity = worldVelocity;
            Color = color;
            Mass = mass;
            Viscosity = viscosity;
        }
    }
}
