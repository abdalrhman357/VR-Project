using UnityEngine;

/// <summary>
/// Distance constraint for Verlet rope simulation.
/// Maintains rest length between two particles — corrects BOTH stretching AND compression.
/// This bidirectional correction is critical for stability: one-directional constraints
/// inject energy into the system and cause perpetual oscillation.
/// </summary>
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

        // حساب الخطأ: موجب = تمدد، سالب = انضغاط
        // نصحح في كلا الاتجاهين لمنع حقن الطاقة (Energy Injection)
        float error = currentDistance - RestLength;

        // تطبيق المرونة (Stiffness) على التصحيح
        Vector3 correction = delta.normalized * (error * Stiffness);

        // التحقق من حد الأمان (Max Stretch) — فقط عند التمدد
        if (currentDistance > RestLength * MaxStretch)
        {
            float excess = currentDistance - (RestLength * MaxStretch);
            // التصحيح الصارم للزيادة فوق الحد + التصحيح المرن للباقي
            correction = delta.normalized * (excess + (RestLength * MaxStretch - RestLength) * Stiffness);
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