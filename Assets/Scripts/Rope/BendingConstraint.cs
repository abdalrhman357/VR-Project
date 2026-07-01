using UnityEngine;

/// <summary>
/// قيد الانحناء — يمنع الحبل من الطي الحاد بين ثلاث جزيئات متتالية.
///
/// الفرق عن DistanceConstraint:
/// ══════════════════════════════
/// DistanceConstraint: يربط الجزيئة i بـ (i+1) — يتحكم في طول القطعة
/// BendingConstraint:  يربط الجزيئة i بـ (i+2) — يتحكم في زاوية الانثناء
///
/// لماذا هو ضروري للأرجوحة؟
/// ══════════════════════════
/// بدون قيد الانحناء، الحبل يتطوى بشكل حاد عند الذروة (عكفة).
/// مع قيد الانحناء: الحبل يبقى شبه مستقيم مثل حبل الأرجوحة الحقيقي.
///
/// Stiffness مناسبة للأرجوحة: 0.3 - 0.6
/// - منخفضة جداً (< 0.1): حبل مرن جداً يلتوي
/// - عالية جداً (> 0.8): حبل صلب كعصا، لا يتأرجح بشكل طبيعي
/// </summary>
public class BendingConstraint
{
    public VerletParticle ParticleA;  // الجزيئة i
    public VerletParticle ParticleM;  // الجزيئة الوسطى i+1 (تشارك في التصحيح)
    public VerletParticle ParticleB;  // الجزيئة i+2
    public float RestAngleCos;        // cos(زاوية الراحة) — محفوظة من التهيئة
    public float Stiffness;

    /// <param name="a">الجزيئة i</param>
    /// <param name="m">الجزيئة i+1 (الوسطى)</param>
    /// <param name="b">الجزيئة i+2</param>
    /// <param name="stiffness">صلابة الانحناء 0→1</param>
    public BendingConstraint(VerletParticle a, VerletParticle m, VerletParticle b,
                              float stiffness = 0.4f)
    {
        ParticleA  = a;
        ParticleM  = m;
        ParticleB  = b;
        Stiffness  = stiffness;

        // حفظ زاوية الراحة الأولية (عادة 180° = حبل مستقيم)
        Vector3 ab = b.Position - a.Position;
        Vector3 am = m.Position - a.Position;
        if (ab.sqrMagnitude > 0.0001f && am.sqrMagnitude > 0.0001f)
            RestAngleCos = Vector3.Dot(ab.normalized, am.normalized);
        else
            RestAngleCos = 1f; // مستقيم
    }

    public void Solve()
    {
        Vector3 ab = ParticleB.Position - ParticleA.Position;
        float   abLen = ab.magnitude;
        if (abLen < 0.0001f) return;

        Vector3 abDir = ab / abLen;

        // الموضع الهدف للجزيئة الوسطى على الخط المستقيم بين A و B
        // هذا يُبقي الجزيئة الوسطى على الخط بدلاً من السماح لها بالانحراف
        Vector3 midpoint   = (ParticleA.Position + ParticleB.Position) * 0.5f;
        Vector3 toMid      = midpoint - ParticleM.Position;

        // نطبق فقط إذا كان الانحراف كبيراً (لا نضغط على انحناءات صغيرة طبيعية)
        float deviation = toMid.magnitude;
        if (deviation < 0.0001f) return;

        Vector3 correction = toMid * Stiffness;

        float invMassA = ParticleA.InverseMass;
        float invMassM = ParticleM.InverseMass;
        float invMassB = ParticleB.InverseMass;

        // الجزيئة الوسطى تتحرك نحو الخط المستقيم
        // الطرفان يتحركان في الاتجاه المعاكس بنسبة صغيرة
        float totalInv = invMassA + invMassM * 2f + invMassB;
        if (totalInv <= 0f) return;

        ParticleM.Position += correction * (invMassM * 2f / totalInv);
        ParticleA.Position -= correction * (invMassA      / totalInv);
        ParticleB.Position -= correction * (invMassB      / totalInv);
    }
}
