using UnityEngine;

public class VerletParticle
{

    public Vector3 Position;
    public Vector3 PreviousPosition;
    public bool IsPinned;
    
    // إضافة الكتلة
    public float Mass;
    float dampingFactor = 0.1f;
    // حساب الكتلة العكسية (مهمة جداً في الفيزياء لحساب التأثير)
    public float InverseMass
    {
        get
        {
            // إذا كان الكائن مثبتاً، فكتلته تعتبر لا نهائية (لا يتأثر)، لذا كتلته العكسية = 0
            if (IsPinned)
                return 0f;

            return 1f / Mass;
        }
    }

    // تحديث دالة البناء لاستقبال الكتلة
    public VerletParticle(Vector3 startPosition, bool pinned = false, float mass = 1f)
    {
        Position = startPosition;
        PreviousPosition = startPosition;
        IsPinned = pinned;
        Mass = mass;
    }

    public void UpdateParticle(float deltaTime, Vector3 gravity)
    {
        if (IsPinned)
            return;


    //v = (P_current - P_previous) (1 - dampingFactor * Delta t)
        Vector3 velocity = (Position - PreviousPosition) * (1f - dampingFactor * deltaTime);

        PreviousPosition = Position;

        //P_new = P_current + v + a c . Delta t^2
        
        Position += velocity;
        
        Position += gravity * deltaTime * deltaTime;
    }
}