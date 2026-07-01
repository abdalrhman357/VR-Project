using UnityEngine;
using Seb.Fluid.Simulation;

/// <summary>
/// مدير محاكاة البندول — حبل + دلو + سائل.
///
/// منطق الأرجوحة الصحيح:
/// ══════════════════════
/// الأرجوحة ليست "دفعة على آخر جزيئة" — بل هي حبل كامل مُمال بزاوية ابتدائية.
/// الطريقة الصحيحة: نضع كل جزيئات الحبل في موضع مائل (كأن شخصاً أمسك الأرجوحة
/// وسحبها للجانب ثم أفلتها). الجاذبية تُسرّع الحركة نحو المنتصف تلقائياً.
///
/// مشاكل النظام السابق:
/// ════════════════════
/// 1. initialPush على جزيئة واحدة فقط → الجزيئات الأخرى تقاوم وتمتص الطاقة
/// 2. Spring-Damper شديد الصلابة (1500) → يوقف الحركة بدل نقلها
/// 3. criticalDamping بالقيمة الكاملة → يمتص كل الطاقة في إطارات قليلة
/// 4. ropeDamping = 0.5 (قديم محفوظ في Scene) → تخميد شديد جداً
/// </summary>
[DefaultExecutionOrder(-200)]
public class PendulumSimulationManager : MonoBehaviour
{
    [Header("Rope Settings")]
    [Tooltip("طول الحبل الكلي بالمتر")]
    public float ropeLength = 10f;

    [Tooltip("عدد مقاطع الحبل (10-15 كافٍ)")]
    public int segmentCount = 12;

    public RopeMaterial ropeMaterial = RopeMaterial.Cotton;
    public LineRenderer lineRenderer;

    [Header("Swing Settings")]
    [Tooltip("زاوية الإمالة الابتدائية بالدرجات — 30° = أرجوحة هادئة، 60° = أرجوحة قوية")]
    [Range(5f, 80f)]
    public float initialSwingAngle = 45f;

    [Tooltip("اتجاه الإمالة الابتدائية (X=يمين/يسار، Z=أمام/خلف)")]
    public Vector2 swingDirection = new Vector2(1f, 0f);

    [Header("Rope Physics")]
    [Tooltip("عدد الخطوات الفرعية لكل إطار (4 = توازن جيد بين الدقة والأداء)")]
    [Range(2, 10)]
    public int ropeSubSteps = 4;

    [Tooltip("عدد تكرارات محلّل القيود")]
    [Range(5, 40)]
    public int solverIterations = 15;

    [Tooltip("تخميد الهواء — 0.02: أرجوحة تدوم طويلاً | 0.08: تتوقف أسرع")]
    [Range(0.005f, 0.15f)]
    public float airDamping = 0.025f;

    [Header("Bucket Mass")]
    [Range(1f, 100f)]
    public float emptyBucketMass = 20f;

    [Range(0f, 200f)]
    public float fluidMass = 30f;

    [Header("Fluid & Bucket Settings")]
    public FluidSim fluidSimulation;

    [Tooltip("إزاحة مركز الدلو عن نقطة اتصال الحبل")]
    public Vector3 bucketOffset = new Vector3(0f, -1.5f, 0f);

    // ── private ──────────────────────────────────────────────────
    private Rope    _rope;
    private Vector3 _bucketPos;
    private Vector3 _bucketVel;
    private bool    _isDragging;
    private Vector3 _dragTarget;

    public float TotalBucketMass => emptyBucketMass + fluidMass;

    // ── Start ─────────────────────────────────────────────────────
    void Start()
    {
        _rope = new Rope(transform.position, ropeLength, segmentCount,
                         ropeMaterial, TotalBucketMass);
        _rope.SolverIterations = solverIterations;

        // ── الإمالة الابتدائية الصحيحة ────────────────────────────
        // بدلاً من دفع جزيئة واحدة، نضع كل الجزيئات في موضع مائل
        // كأن الأرجوحة سُحبت للجانب وأُفلتت — الجاذبية تُسرّع تلقائياً
        ApplyInitialSwing();

        // تهيئة الدلو — الإزاحة على طول اتجاه الحبل (Vector3.down عند البداية)
        Vector3 ropeEnd = _rope.Particles[_rope.Particles.Count - 1].Position;
        _bucketPos = ropeEnd + Vector3.down * Mathf.Abs(bucketOffset.y);
        _bucketVel = Vector3.zero;

        if (fluidSimulation != null)
            fluidSimulation.transform.position = _bucketPos;
    }

    /// <summary>
    /// يُمال الحبل كاملاً بزاوية <see cref="initialSwingAngle"/> في اتجاه <see cref="swingDirection"/>.
    /// كل جزيئة تأخذ موضعها على القوس المائل — الجزيئات ليست ساكنة عند الإطلاق.
    /// PreviousPosition = Position (سرعة ابتدائية = صفر) — الجاذبية تبدأ الحركة.
    /// </summary>
    void ApplyInitialSwing()
    {
        if (_rope.Particles.Count < 2) return;

        Vector3 anchor     = _rope.Particles[0].Position;
        float   angleRad   = initialSwingAngle * Mathf.Deg2Rad;
        Vector3 dir2D      = new Vector3(swingDirection.x, 0f, swingDirection.y).normalized;
        float   segLen     = ropeLength / segmentCount;

        for (int i = 1; i < _rope.Particles.Count; i++)
        {
            // كل جزيئة على قوس دائري مائل بالزاوية
            // الجزيئة i تبعد (i × segLen) عن نقطة التعليق على الحبل
            float dist = i * segLen;

            // الموضع على القوس المائل:
            // X (أو Z) = dist × sin(angle) في اتجاه الإمالة
            // Y        = -dist × cos(angle) للأسفل
            Vector3 newPos = anchor
                + dir2D   * (dist * Mathf.Sin(angleRad))
                + Vector3.down * (dist * Mathf.Cos(angleRad));

            _rope.Particles[i].Position         = newPos;
            _rope.Particles[i].PreviousPosition = newPos; // سرعة ابتدائية = صفر
        }
    }

    // ── FixedUpdate ───────────────────────────────────────────────
    void FixedUpdate()
    {
        if (fluidSimulation != null && fluidSimulation.isPaused) return;

        // تحديث خصائص الحبل من Inspector في كل إطار
        _rope.SolverIterations = solverIterations;
        _rope.SetEndpointMass(TotalBucketMass);

        // نفس معامل التخميد على جميع الجزيئات — موحّد لمنع الالتواء
        foreach (var p in _rope.Particles)
            p.DampingFactor = airDamping;

        float dt    = Time.fixedDeltaTime;
        float subDt = dt / ropeSubSteps;

        for (int step = 0; step < ropeSubSteps; step++)
        {
            // ── السحب بالماوس ──────────────────────────────────────
            if (_isDragging && _rope.Particles.Count > 0)
            {
                var tip = _rope.Particles[_rope.Particles.Count - 1];

                float   k          = 5000f;
                Vector3 tipVel     = (tip.Position - tip.PreviousPosition) / subDt;
                float   c          = 2f * Mathf.Sqrt(k * tip.Mass);
                Vector3 accel      = ((_dragTarget - tip.Position) * k - tipVel * c) / tip.Mass;

                tip.PreviousPosition -= accel * (subDt * subDt);
            }

            // ── محاكاة الحبل ───────────────────────────────────────
            _rope.Simulate(subDt, Physics.gravity);

            // ── ربط الدلو بالحبل — ربط مباشر بدون Spring ─────────
            if (_rope.Particles.Count > 0)
            {
                var tip = _rope.Particles[_rope.Particles.Count - 1];

                // اتجاه الحبل عند طرفه (من الجزيئة قبل الأخيرة للأخيرة)
                Vector3 ropeDir = Vector3.down; // افتراضي إذا لم يتوفر جزيئتان
                if (_rope.Particles.Count >= 2)
                {
                    var prev = _rope.Particles[_rope.Particles.Count - 2];
                    Vector3 d = tip.Position - prev.Position;
                    if (d.sqrMagnitude > 0.0001f)
                        ropeDir = d.normalized;
                }

                // الإزاحة على طول اتجاه الحبل (بدلاً من Vector3.down الثابت)
                // bucketOffset.y = المسافة من طرف الحبل لمركز الدلو على المحور المحلي
                Vector3 newPos = tip.Position + ropeDir * Mathf.Abs(bucketOffset.y);

                // السرعة المشتقة من حركة طرف الحبل
                _bucketVel = (newPos - _bucketPos) / subDt;
                _bucketPos = newPos;

                // ── دوران الدلو مع الحبل ─────────────────────────────
                if (fluidSimulation != null && ropeDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.FromToRotation(Vector3.down, ropeDir);
                    fluidSimulation.transform.rotation = targetRot;
                }

                if (fluidSimulation != null)
                {
                    Vector3 startCentre = GetFluidCentre(_bucketPos);
                    Vector3 endCentre   = GetFluidCentre(newPos);

                    fluidSimulation.transform.position = newPos;
                    fluidSimulation.SimulateSubstep(subDt, _bucketVel, startCentre, endCentre);
                }
            }
        }

        // ── رسم الحبل ─────────────────────────────────────────────
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = _rope.Particles.Count;
            for (int i = 0; i < _rope.Particles.Count; i++)
                lineRenderer.SetPosition(i, _rope.Particles[i].Position);
        }

        // ── ما بعد المحاكاة ────────────────────────────────────────
        if (fluidSimulation != null)
            fluidSimulation.PostSimulationFrame(dt);
    }

    // ── Mouse drag API (يُستدعى من CylinderDragger) ──────────────

    public void BeginDrag()
    {
        if (_rope == null || _rope.Particles.Count == 0) return;
        _isDragging = true;
        _dragTarget = _rope.Particles[_rope.Particles.Count - 1].Position;
    }

    public void UpdateDragPosition(Vector3 deltaPos)
    {
        _dragTarget += deltaPos;

        if (_rope != null && _rope.Particles.Count > 0)
        {
            Vector3 anchor = _rope.Particles[0].Position;
            Vector3 offset = _dragTarget - anchor;
            float   maxLen = ropeLength * 1.5f;
            if (offset.magnitude > maxLen)
                _dragTarget = anchor + offset.normalized * maxLen;
        }
    }

    public void EndDrag()
    {
        _isDragging = false;
    }

    // ── helpers ───────────────────────────────────────────────────

    private Vector3 GetFluidCentre(Vector3 basePos)
    {
        if (fluidSimulation == null) return basePos;
        if (fluidSimulation.bucketRenderer != null)
        {
            float simHalfH     = fluidSimulation.Scale.y * 0.5f;
            float bucketTop    =  simHalfH * fluidSimulation.bucketRenderer.heightScale;
            float bucketBottom = -simHalfH - fluidSimulation.bucketRenderer.bottomPadding;
            float centreY      = (bucketTop + bucketBottom) * 0.5f;
            // نستخدم محور Y المحلي للدلو (بعد الدوران) بدلاً من Vector3.up العالمي
            Vector3 localUp = fluidSimulation.transform.up;
            return basePos + localUp * centreY;
        }
        return basePos;
    }
}
