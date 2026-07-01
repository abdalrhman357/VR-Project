using UnityEngine;

public class VerletParticle
{
    public Vector3 Position;
    public Vector3 PreviousPosition;
    public bool IsPinned;
    public float Mass;
    
    /// <summary>
    /// عامل التخميد الطبيعي (يمثل مقاومة الهواء والاحتكاك الداخلي).
    /// القيم الواقعية: 0.1 - 1.0 (قيم أعلى تجعل الحركة تتوقف أسرع).
    /// الصيغة الأسية exp(-d*dt) تضمن استقلالية التخميد عن معدل الإطارات.
    /// </summary>
    public float DampingFactor = 0.5f;
    
    public float InverseMass
    {
        get
        {
            if (IsPinned)
                return 0f;
            return 1f / Mass;
        }
    }

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

        // السرعة = الفرق بين الموقع الحالي والسابق (خاصية Verlet Integration)
        Vector3 velocity = Position - PreviousPosition;
        
        // تطبيق التخميد الأسي — نربطه بالكتلة (InverseMass) 
        // لكي تحتفظ الأجسام الثقيلة (كالدلو) بزخمها وتتأرجح واقعياً لمدة أطول
        float actualDamping = DampingFactor * InverseMass;
        velocity *= Mathf.Exp(-actualDamping * deltaTime);

        PreviousPosition = Position;

        // P_new = P_current + velocity + gravity * dt²
        Position += velocity;
        Position += gravity * (deltaTime * deltaTime);
    }
}