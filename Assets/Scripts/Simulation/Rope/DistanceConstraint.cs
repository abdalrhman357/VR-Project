using UnityEngine;

public class DistanceConstraint
{
    public VerletParticle ParticleA;
    public VerletParticle ParticleB;
    public float RestLength;

    
    public float Stiffness;
    public float MaxStretch;

    public DistanceConstraint(VerletParticle a, VerletParticle b, float stiffness = 1f, float maxStretch = 1f)
    {
        ParticleA = a;
        ParticleB = b;
        RestLength = Vector3.Distance(a.Position, b.Position);
        
        Stiffness = stiffness;
        MaxStretch = maxStretch;
    }

    public void Solve()
    {
        Vector3 delta = ParticleB.Position - ParticleA.Position;
        float currentDistance = delta.magnitude;

        if (currentDistance <= 0.0001f)
            return;

        Vector3 direction = delta / currentDistance; // normalized

        // 1. Soft correction: push/pull toward rest length, scaled by stiffness.
        //    Handles both compression AND extension (allows the rope to act like a stiff rod).
        float error = currentDistance - RestLength;
        Vector3 correction = direction * (error * Stiffness);

        // 2. Hard clamp: if the segment exceeds the absolute maximum length,
        //    add a rigid correction for the excess ON TOP of the soft correction.
        //    This prevents the rope from ever stretching beyond maxStretch * RestLength.
        float maxLength = RestLength * MaxStretch;
        if (currentDistance > maxLength)
        {
            float excess = currentDistance - maxLength;
            correction += direction * excess;
        }

        // Mass-weighted distribution
        float invMassA = ParticleA.InverseMass;
        float invMassB = ParticleB.InverseMass;
        float totalInvMass = invMassA + invMassB;

        if (totalInvMass <= 0f)
            return;

        float ratioA = invMassA / totalInvMass;
        float ratioB = invMassB / totalInvMass;

        ParticleA.Position += correction * ratioA;
        ParticleB.Position -= correction * ratioB;
    }
}