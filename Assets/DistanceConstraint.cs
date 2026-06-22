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

        // 1. حساب الخطأ الطبيعي مع تطبيق المرونة (Stiffness)
        float error = currentDistance - RestLength;
        Vector3 correction = delta.normalized * (error * Stiffness);

        // 2. التحقق من حد الأمان (Max Stretch)
        float maxLength = RestLength * MaxStretch;
        if (currentDistance > maxLength)
        {
            // إذا تجاوز الحد، نلغي المرونة ونطبق تصحيحاً صارماً (Rigid) للزيادة فقط
            float excess = currentDistance - maxLength;
            correction = delta.normalized * excess;
        }

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