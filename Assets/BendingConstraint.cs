using UnityEngine;

public class BendingConstraint
{
    public VerletParticle ParticleA;
    public VerletParticle ParticleB;
    public float RestDistance;
    public float Stiffness;

    public BendingConstraint(VerletParticle a, VerletParticle b, float stiffness = 0.2f)
    {
        ParticleA = a;
        // لاحظ: ParticleB هنا سيكون الجزيء الذي يبعد خطوتين، وليس الجزيء المجاور
        ParticleB = b;
        Stiffness = stiffness;

        // نحفظ المسافة الأصلية (وهي هنا تساوي طول قطعتين من الحبل)
        RestDistance = Vector3.Distance(a.Position, b.Position);
    }

    public void Solve()
    {
        Vector3 delta = ParticleB.Position - ParticleA.Position;
        float currentDistance = delta.magnitude;

        if (currentDistance < 0.0001f)
            return;

        // حساب الخطأ وتطبيق الصلابة
        float error = currentDistance - RestDistance;
        Vector3 correction = delta.normalized * (error * Stiffness);

        float invMassA = ParticleA.InverseMass;
        float invMassB = ParticleB.InverseMass;
        float totalInvMass = invMassA + invMassB;

        if (totalInvMass <= 0f)
            return;

        // توزيع التصحيح بناءً على الكتلة
        ParticleA.Position += correction * (invMassA / totalInvMass);
        ParticleB.Position -= correction * (invMassB / totalInvMass);
    }
}