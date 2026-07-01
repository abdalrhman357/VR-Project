using UnityEngine;

/// <summary>
/// جزيئة Verlet لمحاكاة الحبل.
///
/// التخميد الصحيح للأرجوحة:
/// ══════════════════════════
/// الأرجوحة الحقيقية تتخامد ببطء شديد — الهواء لا يمتص الطاقة بسرعة.
/// معامل التخميد الواقعي لحبل في الهواء: 0.02 - 0.08 (وليس 0.5).
///
/// الخطأ السابق: ربط التخميد بـ InverseMass يجعل الجزيئات الخفيفة
/// تتوقف بسرعة 10x أكثر من الدلو الثقيل → اختلاف في سرعة التخميد
/// على طول الحبل → التواء عند الذروة.
///
/// الحل: تخميد موحّد لجميع الجزيئات، قيمة صغيرة جداً للحصول على
/// تأرجح طويل مثل الأرجوحة الحقيقية.
/// </summary>
public class VerletParticle
{
    public Vector3 Position;
    public Vector3 PreviousPosition;
    public bool    IsPinned;
    public float   Mass;

    /// <summary>
    /// مقاومة الهواء — القيم الواقعية للأرجوحة: 0.02 - 0.08
    /// قيمة 0.03 تعطي ~30 تأرجحة قبل التوقف (مثل أرجوحة حديقة حقيقية)
    /// </summary>
    public float DampingFactor = 0.03f;

    public float InverseMass => IsPinned ? 0f : 1f / Mass;

    public VerletParticle(Vector3 startPosition, bool pinned = false, float mass = 1f)
    {
        Position         = startPosition;
        PreviousPosition = startPosition;
        IsPinned         = pinned;
        Mass             = mass;
    }

    public void UpdateParticle(float deltaTime, Vector3 gravity)
    {
        if (IsPinned) return;

        // الزخم المحفوظ من الخطوة السابقة
        Vector3 velocity = Position - PreviousPosition;

        // تخميد أسي موحّد — مستقل عن الكتلة ومستقل عن معدل الإطارات
        // exp(-d*dt) يضمن نفس معدل التخميد بغض النظر عن حجم dt
        velocity *= Mathf.Exp(-DampingFactor * deltaTime);

        PreviousPosition = Position;

        // تكامل Verlet: P_new = P + V + g*dt²
        Position += velocity;
        Position += gravity * (deltaTime * deltaTime);
    }
}
