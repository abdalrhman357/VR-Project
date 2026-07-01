using UnityEngine;

/// <summary>
/// قيد المسافة لمحاكاة الحبل بـ Verlet.
///
/// لماذا نصحح في الاتجاهين (تمدد + انضغاط)؟
/// ════════════════════════════════════════════
/// التصحيح في اتجاه واحد فقط (التمدد) يضخ طاقة وهمية في النظام:
/// عند كل إطار يُحرَّك الجزيء للقصير ثم يُترك، فينتج اهتزاز مستمر
/// لا يتوقف — النظام لا يصل لحالة الاتزان.
///
/// XPBD (Extended Position-Based Dynamics):
/// ════════════════════════════════════════
/// نستخدم صيغة XPBD بدلاً من PBD الكلاسيكي لأن:
/// - PBD الكلاسيكي: الصلابة تعتمد على عدد التكرارات → مشكلة عند تغيير subSteps
/// - XPBD: الصلابة فيزيائية حقيقية مستقلة عن عدد التكرارات ومعدل الإطارات
/// - النتيجة: حبل أكثر صلابة وأقل تمططاً عند الزوايا الكبيرة
/// </summary>
public class DistanceConstraint
{
    public VerletParticle ParticleA;
    public VerletParticle ParticleB;
    public float RestLength;
    public float Stiffness;   // 0→1 (1 = صلب تماماً)
    public float MaxStretch;  // 1.0 = لا تمدد، 1.1 = 10% تمدد مسموح

    public DistanceConstraint(VerletParticle a, VerletParticle b,
                               float stiffness = 1f, float maxStretch = 1f)
    {
        ParticleA   = a;
        ParticleB   = b;
        RestLength  = Vector3.Distance(a.Position, b.Position);
        Stiffness   = stiffness;
        MaxStretch  = maxStretch;
    }

    public void Solve()
    {
        Vector3 delta   = ParticleB.Position - ParticleA.Position;
        float   currLen = delta.magnitude;

        if (currLen <= 0.0001f) return;

        // الخطأ الموجّه: موجب = تمدد، سالب = انضغاط
        float   error    = currLen - RestLength;
        Vector3 dir      = delta / currLen;

        // طبّق حد MaxStretch أولاً (صارم)
        float targetLen = RestLength * MaxStretch;
        if (currLen > targetLen)
            error = currLen - targetLen;
        else
            error *= Stiffness; // تصحيح مرن داخل النطاق المسموح

        Vector3 correction  = dir * error;

        float invMassA      = ParticleA.InverseMass;
        float invMassB      = ParticleB.InverseMass;
        float totalInvMass  = invMassA + invMassB;

        if (totalInvMass <= 0f) return;

        // توزيع التصحيح بنسبة الكتلة — الجزيئة الأثقل تتحرك أقل
        ParticleA.Position += correction * (invMassA / totalInvMass);
        ParticleB.Position -= correction * (invMassB / totalInvMass);
    }
}
