using UnityEngine;

/// <summary>
/// بيانات جسيمة الطلاء — struct للأداء
/// لا Rigidbody، لا Collider — كل شيء محسوب يدوياً
/// </summary>
public class FluidParticleData
{
    // ── الحركة ──
    public Vector3 Position;
    public Vector3 PrevPosition;     // Verlet: لا سرعة صريحة
    public Vector3 Acceleration;

    // ── خصائص الجسيمة ──
    public float Mass        = 0.001f;
    public float Radius      = 0.015f;   // نصف قطر التأثير لـ SPH
    public float Viscosity;              // يُرث من الطلاء
    public Color PaintColor;

    // ── SPH ──
    public float Density;
    public float Pressure;

    // ── الحالة ──
    public enum ParticleState { Active, Splattered }
    public ParticleState State = ParticleState.Active;

    // ── الـ Visual Object في Unity ──
    public GameObject VisualObject;      // sphere صغيرة للعرض

    public FluidParticleData(Vector3 startPos, Vector3 initialVelocity,
                              Color color, float viscosity, float dt)
    {
        Position  = startPos;
        PrevPosition = startPos - initialVelocity * dt;   // Verlet: V مضمّنة
        PaintColor   = color;
        Viscosity    = viscosity;
    }

    // ── Verlet Integration (بدون Unity Physics) ──
    public void Integrate(float dt, Vector3 gravity, float airDrag)
    {
        if (State != ParticleState.Active) return;

        // الجاذبية دائماً مضافة
        Acceleration += gravity;

        Vector3 velocity    = (Position - PrevPosition) * airDrag;
        Vector3 newPosition = Position + velocity + Acceleration * dt * dt;

        PrevPosition = Position;
        Position     = newPosition;
        Acceleration = Vector3.zero;
    }

    public void AddForce(Vector3 force)
    {
        Acceleration += force / Mass;
    }

    public Vector3 GetVelocity(float dt) => (Position - PrevPosition) / dt;

    public bool IsActive => State == ParticleState.Active;
}
