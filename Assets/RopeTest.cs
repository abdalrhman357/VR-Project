using UnityEngine;

public class RopeTest : MonoBehaviour
{
    private Rope rope;
    public LineRenderer lineRenderer;

    [Header("settings material")]
    public RopeMaterial ropeMaterial = RopeMaterial.Rubber;
    void Start()
    {
        // إنشاء حبل طوله 10 وحدات، ومقسم إلى 15 جزيء
        rope = new Rope(transform.position, 10f, 15,ropeMaterial);

        // VerletParticle bucket = rope.Particles[15];

        // bucket.Position += new Vector3(4f, 0f, 0f);
        // Vector3 initialVelocity = new Vector3(0f, 0f, 0.15f);
        // bucket.PreviousPosition = bucket.Position - initialVelocity;
        // إعطاء دفعة ابتدائية (سرعة) لآخر جزيء في الحبل ليتأرجح مثل البندول
        rope.Particles[rope.Particles.Count - 1].PreviousPosition += (Vector3.left * 0.2f);

        // VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];

        // // رفعنا الدلو للأعلى ولليسار لكي يسقط بقوة ونرى تمدد المطاط!
        // bucket.Position += new Vector3(4f, 4f, 0f);
    }   

    void Update()
    {   HandleMouseInteraction();
        // تشغيل المحاكاة الفيزيائية
        rope.Simulate(Time.deltaTime, Physics.gravity);

        // رسم الحبل باستخدام Line Renderer
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = rope.Particles.Count;
            for(int i = 0; i < rope.Particles.Count; i++)
            {
                lineRenderer.SetPosition(i, rope.Particles[i].Position);
            }
        }
    }
        // دالة جديدة لمعالجة الماوس
    private void HandleMouseInteraction()
    {
        // التحقق مما إذا كان الزر الأيسر للماوس مضغوطاً (رقم 0)
        if (Input.GetMouseButton(0))
        {
            // أخذ آخر جزيء في الحبل (الذي يمثل الدلو)
            VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];

            // قراءة موقع الماوس على الشاشة
            Vector3 mouseScreenPosition = Input.mousePosition;
            
            // تحديد العمق (Z) بالنسبة للكاميرا لكي يتم التحويل بشكل صحيح
            // نستخدم المسافة بين الكاميرا وموقع الحبل الأصلي
            mouseScreenPosition.z = Mathf.Abs(Camera.main.transform.position.z - transform.position.z);

            // تحويل موقع الماوس من الشاشة (2D) إلى العالم (3D)
            Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(mouseScreenPosition);

            // إجبار الدلو على الذهاب لموقع الماوس
            bucket.Position = mouseWorldPosition;

            // خدعة فيزيائية هامة جداً:
            // نجعل الموقع السابق يساوي الموقع الحالي أثناء الإمساك به
            // لكي لا تتراكم السرعة وينطلق الحبل كالمقلاع عند إفلات الماوس
            bucket.PreviousPosition = mouseWorldPosition; 
        }
    }
}
    