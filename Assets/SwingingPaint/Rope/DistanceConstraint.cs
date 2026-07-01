using UnityEngine;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// Position-based distance constraint between two particles. Corrects the separation back toward
    /// RestLength, distributing the correction by inverse mass (heavier particle moves less).
    /// A MaxStretch clamp applies a rigid correction past the limit so the rope can't blow up.
    ///
    /// Note: a real rope resists stretching but not compression, so by default only positive error
    /// (over-stretch) is corrected. Set <see cref="ResistCompression"/> for a stiff strut instead.
    /// </summary>
    public sealed class DistanceConstraint
    {
        public readonly VerletParticle A;
        public readonly VerletParticle B;
        public float RestLength;
        public float Stiffness;
        public float MaxStretch;
        public bool ResistCompression;

        public DistanceConstraint(VerletParticle a, VerletParticle b,
                                  float stiffness = 1f, float maxStretch = 1f, bool resistCompression = false)
        {
            A = a;
            B = b;
            RestLength = Vector3.Distance(a.Position, b.Position);
            Stiffness = stiffness;
            MaxStretch = maxStretch;
            ResistCompression = resistCompression;
        }

        public void Solve()
        {
            Vector3 delta = B.Position - A.Position;
            float dist = delta.magnitude;
            if (dist <= 1e-5f) return;

            float error = dist - RestLength;
            if (!ResistCompression && error < 0f) return;   // ropes don't push

            Vector3 dir = delta / dist;
            Vector3 correction = dir * (error * Stiffness);

            // Hard safety clamp: never let a segment exceed RestLength * MaxStretch.
            float maxLength = RestLength * MaxStretch;
            if (dist > maxLength)
                correction = dir * (dist - maxLength);

            float wA = A.InverseMass, wB = B.InverseMass;
            float wSum = wA + wB;
            if (wSum <= 0f) return;

            A.Position += correction * (wA / wSum);
            B.Position -= correction * (wB / wSum);
        }
    }
}
