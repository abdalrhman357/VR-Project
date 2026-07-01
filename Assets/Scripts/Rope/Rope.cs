using System.Collections.Generic;
using UnityEngine;

public enum RopeMaterial
{
    Rigid,   // حبل صلب تماماً — لا تمدد
    Cotton,  // قطن — تمدد طفيف
    Nylon,   // نايلون — تمدد متوسط
    Rubber   // مطاط — تمدد كبير
}

/// <summary>
/// محاكاة الحبل بـ Verlet Integration + Position-Based Dynamics.
///
/// مبادئ التصميم لحركة الأرجوحة الواقعية:
/// ══════════════════════════════════════════
///
/// 1. كتلة الحبل خفيفة نسبة للدلو (1:20)
///    السبب: الحبل الحقيقي يزن أقل بكثير من الدلو + السائل.
///    إذا كان الحبل ثقيلاً، يسرق الزخم من الدلو ويبطئ التأرجح.
///
/// 2. تخميد منخفض جداً (0.02 - 0.05)
///    السبب: الهواء لا يمتص طاقة الأرجوحة بسرعة. تخميد 0.03 يعطي
///    ~20-30 تأرجحة قبل التوقف — مثل أرجوحة حديقة حقيقية.
///
/// 3. BendingConstraint بين 3 جزيئات (i, i+1, i+2)
///    السبب: يمنع الطي الحاد "العكفة" عند الذروة دون تصلب الحبل.
///    يُبقي الجزيئة الوسطى على خط مستقيم بين جارتيها.
///
/// 4. Gauss-Seidel المتناوب (أعلى↔أسفل)
///    السبب: الحل من اتجاه واحد يُقوّي الطرف الأعلى ويُضعف الطرف الأسفل
///    (حيث الدلو الثقيل). التناوب يوزع التقارب بالتساوي.
///
/// 5. تثبيت نقطة التعليق بعد كل تكرار
///    السبب: القيود قد تُحرّك الجزيئة [0] بشكل طفيف حتى لو كانت مثبّتة.
///    نُعيد تثبيتها صراحةً + نُصحح PreviousPosition لمنع حقن طاقة وهمية.
///
/// 6. subSteps عالية (4-6) في PendulumSimulationManager
///    السبب: Verlet يحتاج dt صغيرة للدقة عند الزوايا الكبيرة.
///    subStep = تقسيم كل إطار لخطوات أصغر → استقرار أكبر.
/// </summary>
public class Rope
{
    public List<VerletParticle>    Particles          = new List<VerletParticle>();
    public List<DistanceConstraint> Constraints       = new List<DistanceConstraint>();
    public List<BendingConstraint>  BendingConstraints = new List<BendingConstraint>();

    public int SolverIterations = 20;

    // الموضع الأصلي لنقطة التعليق — لإعادة التثبيت الصارم
    private Vector3 _anchorPosition;

    /// <param name="startPosition">موضع نقطة التعليق في العالم</param>
    /// <param name="ropeLength">الطول الكلي للحبل بالمتر</param>
    /// <param name="segmentCount">عدد القطع (15 مناسب للأرجوحة)</param>
    /// <param name="material">نوع المادة — Cotton مناسب للأرجوحة</param>
    /// <param name="endpointMass">كتلة الدلو + السائل بالكيلوغرام</param>
    /// <param name="bendingStiffness">صلابة مقاومة الطي 0→1 (0.4 مناسب للأرجوحة)</param>
    public Rope(Vector3 startPosition, float ropeLength, int segmentCount,
                RopeMaterial material, float endpointMass = 10f, float bendingStiffness = 0.4f)
    {
        _anchorPosition = startPosition;

        // ── صلابة وحد التمدد حسب نوع الحبل ──────────────────────
        float stiffness  = 1.0f;
        float maxStretch = 1.0f;
        switch (material)
        {
            case RopeMaterial.Rigid:  stiffness = 1.00f; maxStretch = 1.00f; break;
            case RopeMaterial.Cotton: stiffness = 0.90f; maxStretch = 1.03f; break;
            case RopeMaterial.Nylon:  stiffness = 0.75f; maxStretch = 1.10f; break;
            case RopeMaterial.Rubber: stiffness = 0.35f; maxStretch = 1.40f; break;
        }

        float segmentLength = ropeLength / segmentCount;

        // ── كتلة جزيئات الحبل ────────────────────────────────────
        // نسبة 1:20 من الدلو: حبل 50kg دلو → كل جزيئة 2.5kg
        // هذا يعطي حبلاً خفيفاً لا يسرق الزخم ويسمح بتأرجح حر وطويل
        float ropeNodeMass = Mathf.Max(0.3f, endpointMass / 20f);

        // ── بناء الجزيئات ─────────────────────────────────────────
        for (int i = 0; i <= segmentCount; i++)
        {
            Vector3 pos    = startPosition + Vector3.down * segmentLength * i;
            bool    pinned = (i == 0);
            float   mass   = (i == segmentCount) ? endpointMass : ropeNodeMass;
            Particles.Add(new VerletParticle(pos, pinned, mass));
        }

        // ── قيود المسافة (حبل بين كل جزيئتين متتاليتين) ──────────
        for (int i = 0; i < Particles.Count - 1; i++)
            Constraints.Add(new DistanceConstraint(Particles[i], Particles[i + 1],
                                                    stiffness, maxStretch));

        // ── قيود الانحناء (i, i+1, i+2) — يمنع العكفة ─────────────
        for (int i = 0; i < Particles.Count - 2; i++)
            BendingConstraints.Add(new BendingConstraint(
                Particles[i], Particles[i + 1], Particles[i + 2], bendingStiffness));
    }

    // ── واجهة عامة ───────────────────────────────────────────────

    public void SetEndpointMass(float mass)
    {
        if (Particles.Count > 0)
            Particles[Particles.Count - 1].Mass = mass;
    }

    public float GetEndpointMass()
    {
        return Particles.Count > 0 ? Particles[Particles.Count - 1].Mass : 0f;
    }

    public float GetMaxVelocitySqr()
    {
        float maxSqr = 0f;
        foreach (var p in Particles)
        {
            if (p.IsPinned) continue;
            float sqr = (p.Position - p.PreviousPosition).sqrMagnitude;
            if (sqr > maxSqr) maxSqr = sqr;
        }
        return maxSqr;
    }

    // ── حلقة المحاكاة ────────────────────────────────────────────

    /// <summary>
    /// خطوة محاكاة واحدة — تُستدعى من PendulumSimulationManager عدة مرات للإطار.
    /// </summary>
    public void Simulate(float deltaTime, Vector3 gravity)
    {
        // ══ خطوة 1: تطبيق القوى والتكامل (Verlet) ══════════════════
        foreach (var particle in Particles)
            particle.UpdateParticle(deltaTime, gravity);

        // ══ خطوة 2: حل القيود — Gauss-Seidel المتناوب ═══════════════
        //
        // التناوب ضروري لأن:
        //   - Forward (0→N) يُقوّي القمة ويُضعف الدلو الثقيل
        //   - Backward (N→0) يُقوّي الدلو ويُضعف القمة
        //   - التناوب يوزع التقارب بشكل متوازن على طول الحبل كله
        //
        for (int iter = 0; iter < SolverIterations; iter++)
        {
            bool forward = (iter % 2 == 0);

            if (forward)
            {
                for (int i = 0; i < Constraints.Count; i++)
                    Constraints[i].Solve();
                for (int i = 0; i < BendingConstraints.Count; i++)
                    BendingConstraints[i].Solve();
            }
            else
            {
                for (int i = Constraints.Count - 1; i >= 0; i--)
                    Constraints[i].Solve();
                for (int i = BendingConstraints.Count - 1; i >= 0; i--)
                    BendingConstraints[i].Solve();
            }

            // ── تثبيت نقطة التعليق الصارم بعد كل تكرار ──────────
            // القيود قد تُزحزح الجزيئة [0] بشكل طفيف رغم كونها مثبّتة.
            // نُعيدها لموضعها الأصلي ونُصلح PreviousPosition حتى لا
            // تُحقن طاقة وهمية في الخطوة التالية.
            if (Particles.Count > 0)
            {
                Particles[0].Position         = _anchorPosition;
                Particles[0].PreviousPosition = _anchorPosition;
            }
        }
    }

    /// <summary>
    /// تحديث موضع نقطة التعليق (إذا كانت المستخدم يحرك نقطة العلق)
    /// </summary>
    public void SetAnchorPosition(Vector3 newAnchor)
    {
        _anchorPosition = newAnchor;
        if (Particles.Count > 0)
        {
            Particles[0].Position         = newAnchor;
            Particles[0].PreviousPosition = newAnchor;
        }
    }
}
