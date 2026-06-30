using UnityEngine;

public class VerletParticle
{

    public Vector3 Position;
    public Vector3 PreviousPosition;
    public bool IsPinned;
    
    // إضافة الكتلة
    public float Mass;

    // ──────────────────────────────────────────────────────────────────
    // Damping coefficient (exponential decay, frame-rate independent).
    //
    // The formula:  retain = Pow(1 - d, dt * 60)
    //
    // This means the *per-second* velocity retention is  Pow(1-d, 60).
    // Examples with our sub-step dt = 1/240s:
    //
    //   d = 0.0005  →  retain/sec ≈ 0.970  →  3% loss/sec  (very light air drag)
    //   d = 0.002   →  retain/sec ≈ 0.887  → 11% loss/sec  (gentle damping)
    //   d = 0.005   →  retain/sec ≈ 0.740  → 26% loss/sec  (noticeable damping)
    //   d = 0.02    →  retain/sec ≈ 0.296  → 70% loss/sec  (WAY too heavy!)
    //
    // For a pendulum (T ≈ 6s) to swing back and forth many times:
    //   d ≈ 0.001–0.003 is the sweet spot.
    // ──────────────────────────────────────────────────────────────────
    public float DampingCoefficient = 0.002f;

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

        // Frame-rate-independent exponential damping.
        // Pow(1 - d, dt * 60) normalises so that the *per-second* energy loss is constant
        // regardless of whether we run 30, 60, or 240 substeps per second.
        // With d = 0.002: retain/sec ≈ 0.887 → the pendulum swings many times before stopping.
        float retain = Mathf.Pow(1f - DampingCoefficient, deltaTime * 60f);

        Vector3 velocity = (Position - PreviousPosition) * retain;

        PreviousPosition = Position;

        //P_new = P_current + v + a · Δt²
        Position += velocity;
        
        Position += gravity * deltaTime * deltaTime;
    }
}