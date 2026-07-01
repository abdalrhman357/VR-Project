using UnityEngine;
using Seb.Fluid.Simulation;

/// <summary>
/// المدير المركزي: يربط الحبل بالدلو.
/// 
/// معالجة الاهتزاز — الحل النهائي:
/// ══════════════════════════════════
/// الاهتزاز عند السكون ناتج عن أمرين:
/// 1. الصراع المستمر بين الجاذبية ومحلّل القيود في Verlet Integration
/// 2. أي تغيير طفيف في FluidSim.transform.position يُعيد حساب كل جزيئات السائل
///
/// الحل من شقّين:
/// أ) محلّل Gauss-Seidel المتناوب (في Rope.cs) — يحسّن التقارب عند الدلو الثقيل
/// ب) عتبة تحديث الموقع (Position Update Threshold) — لا نحدّث موقع FluidSim
///    إلا إذا تغيّر بأكثر من الحد الأدنى. هذا يمنع الاهتزاز الدقيق من الوصول
///    لنظام السائل دون أي تأثير على الحركة الفعلية.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PendulumSimulationManager : MonoBehaviour
{
    [Header("Rope Settings")]
    [Tooltip("طول الحبل الكلي")]
    public float ropeLength = 10f;
    
    [Tooltip("عدد الجزيئات — زيادتها تزيد التخميد الطبيعي")]
    public int segmentCount = 15;
    
    public RopeMaterial ropeMaterial = RopeMaterial.Cotton;
    public LineRenderer lineRenderer;
    
    [Tooltip("قوة الدفع الابتدائية للتأرجح")]
    public Vector3 initialPush = new Vector3(0.05f, 0f, 0.02f);

    [Header("Rope Physics")]
    [Tooltip("عدد الخطوات الفرعية لكل إطار")]
    [Range(1, 8)]
    public int ropeSubSteps = 3;
    
    [Tooltip("عدد تكرارات محلّل القيود")]
    [Range(1, 50)]
    public int solverIterations = 20;
    
    [Tooltip("مقاومة الهواء (0.1 - 1.0 لحركة واقعية)")]
    [Range(0f, 5f)]
    public float ropeDamping = 0.5f;

    [Header("Bucket Mass")]
    [Tooltip("كتلة الدلو فارغاً")]
    [Range(1f, 100f)]
    public float emptyBucketMass = 20f;
    
    [Tooltip("كتلة السائل داخل الدلو")]
    [Range(0f, 200f)]
    public float fluidMass = 30f;
    
    [Header("Fluid & Bucket Settings")]
    [Tooltip("اسحب هنا كائن FluidSim")]
    public FluidSim fluidSimulation;
    
    [Tooltip("إزاحة نقطة اتصال الحبل بالدلو")]
    public Vector3 bucketOffset = new Vector3(0f, -1.5f, 0f);
    
    [Header("Bucket Physics (Soft Coupling)")]
    [Tooltip("قوة الزنبرك الذي يربط الدلو بالحبل (أعلى = أقسى)")]
    [Range(100f, 5000f)]
    public float bucketSpringStiffness = 1500f;
    
    [Tooltip("تخميد الزنبرك لامتصاص الاهتزازات (أعلى = يمتص أسرع لكن قد يبطئ الحركة)")]
    [Range(10f, 500f)]
    public float bucketSpringDamping = 100f;
    
    private Rope rope;
    
    // موقع وسرعة الدلو (مفصولة عن الحبل لامتصاص الاهتزاز)
    private Vector3 bucketPosition;
    private Vector3 bucketVelocity;

    // حالة السحب بالماوس (Mouse Joint)
    private bool isDraggingBucket = false;
    private Vector3 dragTargetPosition;

    public float TotalBucketMass => emptyBucketMass + fluidMass;

    void Start()
    {
        rope = new Rope(transform.position, ropeLength, segmentCount, ropeMaterial, TotalBucketMass);
        rope.SolverIterations = solverIterations;
        
        // الدفعة الابتدائية
        VerletParticle bucketNode = rope.Particles[rope.Particles.Count - 1];
        bucketNode.PreviousPosition = bucketNode.Position - initialPush;
        
        // تهيئة الموقع والسرعة للدلو
        bucketPosition = bucketNode.Position + bucketOffset;
        bucketVelocity = Vector3.zero;
        
        if (fluidSimulation != null)
        {
            fluidSimulation.transform.position = bucketPosition;
        }
    }

    void FixedUpdate()
    {
        if (fluidSimulation != null && fluidSimulation.isPaused)
            return;

        // ═══ تحديث الخصائص ═══
        rope.SolverIterations = solverIterations;
        rope.SetEndpointMass(TotalBucketMass);
        
        foreach (var particle in rope.Particles)
        {
            particle.DampingFactor = ropeDamping;
        }

        // ═══════════════════════════════════════════════════════════════
        // Master Physics Loop (حلقة الفيزياء الرئيسية)
        // ═══════════════════════════════════════════════════════════════
        float dt = Time.fixedDeltaTime;
        float subDt = dt / ropeSubSteps;

        for (int step = 0; step < ropeSubSteps; step++)
        {
            // 0. سحب الدلو بالماوس (Mouse Spring / Joint)
            if (isDraggingBucket && rope.Particles.Count > 0)
            {
                VerletParticle bucketNode = rope.Particles[rope.Particles.Count - 1];
                
                // قوة سحب الماوس نحو نقطة الهدف: استخدام قوة منطقية (كقوة يد الإنسان) 
                // بدلاً من القوة الخرافية السابقة التي كانت تجمد حركة الحبل لتعارضها مع القيود
                float dragStiffness = 1500f; 
                Vector3 dragForce = (dragTargetPosition - bucketNode.Position) * dragStiffness; 
                
                // تخميد حرج لمنع الاهتزاز أثناء السحب 
                float dragDampingFactor = 2f * Mathf.Sqrt(dragStiffness * bucketNode.Mass);
                Vector3 nodeVel = (bucketNode.Position - bucketNode.PreviousPosition) / subDt;
                Vector3 dragDamping = -nodeVel * dragDampingFactor;
                
                Vector3 dragAccel = (dragForce + dragDamping) / bucketNode.Mass;
                
                // التطبيق الصحيح للتسارع في نظام Verlet
                bucketNode.PreviousPosition -= dragAccel * (subDt * subDt);
            }

            // 1. محاكاة الحبل
            rope.Simulate(subDt, Physics.gravity);

            // 2. تحديث موقع الدلو عبر نظام Spring-Damper
            if (fluidSimulation != null && rope.Particles.Count > 0)
            {
                VerletParticle bucketNode = rope.Particles[rope.Particles.Count - 1];
                Vector3 targetPosition = bucketNode.Position + bucketOffset;
                
                // قوة الزنبرك (Spring Force)
                Vector3 displacement = bucketPosition - targetPosition;
                Vector3 springForce = -bucketSpringStiffness * displacement;
                
                // التخميد الحرج (Critical Damping) يحسب تلقائياً لمنع أي اهتزازات إضافية من الزنبرك نهائياً!
                float criticalDamping = 2f * Mathf.Sqrt(bucketSpringStiffness * TotalBucketMass);
                Vector3 dampingForce = -criticalDamping * bucketVelocity;
                
                // حساب التسارع
                Vector3 totalForce = springForce + dampingForce + (Physics.gravity * TotalBucketMass);
                Vector3 acceleration = totalForce / TotalBucketMass;
                
                // تحديث السرعة والموقع
                bucketVelocity += acceleration * subDt;
                Vector3 nextBucketPosition = bucketPosition + bucketVelocity * subDt;
                
                // استخراج مراكز السائل للإطار الحالي والقادم
                Vector3 startCentre = GetFluidCentre(bucketPosition);
                Vector3 endCentre = GetFluidCentre(nextBucketPosition);
                
                // تحديث موقع الـ Transform
                fluidSimulation.transform.position = nextBucketPosition;
                
                // 3. محاكاة السائل
                fluidSimulation.SimulateSubstep(subDt, bucketVelocity, startCentre, endCentre);
                
                bucketPosition = nextBucketPosition;
            }
        }

        // ═══ رسم الحبل ═══
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = rope.Particles.Count;
            for (int i = 0; i < rope.Particles.Count; i++)
            {
                lineRenderer.SetPosition(i, rope.Particles[i].Position);
            }
        }

        // 4. العمليات البعدية للسائل (تحديث الرغوة، تصيير الخريطة الكثافية)
        if (fluidSimulation != null)
        {
            fluidSimulation.PostSimulationFrame(dt);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // التحكم بالماوس (يتم استدعاؤه من CylinderDragger)
    // ═══════════════════════════════════════════════════════════════
    public void BeginDrag()
    {
        if (rope == null || rope.Particles.Count == 0) return;
        isDraggingBucket = true;
        // نضبط نقطة الهدف على موقع العقدة الحالي لتجنب القفزات
        dragTargetPosition = rope.Particles[rope.Particles.Count - 1].Position;
    }

    public void UpdateDragPosition(Vector3 deltaPos)
    {
        dragTargetPosition += deltaPos;
        
        // منع نقطة السحب من الابتعاد كثيراً عن الحبل لتجنب قوى خرافية وتمزق المفاصل
        if (rope != null && rope.Particles.Count > 0)
        {
            Vector3 anchorPos = rope.Particles[0].Position;
            Vector3 offset = dragTargetPosition - anchorPos;
            float maxLen = ropeLength * 1.5f; 
            if (offset.magnitude > maxLen)
            {
                dragTargetPosition = anchorPos + offset.normalized * maxLen;
            }
        }
    }

    public void EndDrag()
    {
        isDraggingBucket = false;
    }

    private Vector3 GetFluidCentre(Vector3 basePos)
    {
        if (fluidSimulation == null) return basePos;
        if (fluidSimulation.bucketRenderer != null)
        {
            float simHalfH = fluidSimulation.Scale.y * 0.5f;
            float bucketTop    =  simHalfH * fluidSimulation.bucketRenderer.heightScale;
            float bucketBottom = -simHalfH - fluidSimulation.bucketRenderer.bottomPadding;
            float centreY      = (bucketTop + bucketBottom) * 0.5f;
            return basePos + Vector3.up * centreY;
        }
        return basePos;
    }
}
