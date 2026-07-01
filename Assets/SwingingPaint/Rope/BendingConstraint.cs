using UnityEngine;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// Skip-one bending constraint: links particle i to particle i+2 at their rest separation
    /// (≈ two segment lengths). This resists sharp kinks and gives the rope realistic stiffness
    /// and curvature without making it fully rigid. Stiffness in [0,1] scales the correction.
    /// </summary>
    public sealed class BendingConstraint
    {
        public readonly VerletParticle A;
        public readonly VerletParticle B;
        public float RestDistance;
        public float Stiffness;

        public BendingConstraint(VerletParticle a, VerletParticle b, float stiffness = 0.2f)
        {
            A = a;
            B = b;            // the particle two steps along the rope
            Stiffness = stiffness;
            RestDistance = Vector3.Distance(a.Position, b.Position);
        }

        public void Solve()
        {
            Vector3 delta = B.Position - A.Position;
            float dist = delta.magnitude;
            if (dist <= 1e-5f) return;

            Vector3 correction = (delta / dist) * ((dist - RestDistance) * Stiffness);

            float wA = A.InverseMass, wB = B.InverseMass;
            float wSum = wA + wB;
            if (wSum <= 0f) return;

            A.Position += correction * (wA / wSum);
            B.Position -= correction * (wB / wSum);
        }
    }
}
