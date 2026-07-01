using UnityEngine;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// A point mass integrated with Verlet (velocity is implicit in position history).
    /// No Rigidbody, no Unity physics — fully custom, per project constraints.
    ///
    /// Verlet step:  xₙ₊₁ = xₙ + (xₙ − xₙ₋₁)·(1 − c·dt) + a·dt²
    /// where (xₙ − xₙ₋₁) is the implicit velocity·dt and c is a linear-drag coefficient.
    /// With a FIXED dt (driven by FixedStepClock) this is deterministic across frame rates,
    /// fixing the frame-rate-dependent damping of the original implementation.
    /// </summary>
    public sealed class VerletParticle
    {
        public Vector3 Position;
        public Vector3 PreviousPosition;
        public bool IsPinned;
        public float Mass;

        /// <summary>Pinned particles are immovable ⇒ infinite mass ⇒ inverse mass 0.</summary>
        public float InverseMass => IsPinned ? 0f : 1f / Mathf.Max(Mass, 1e-6f);

        public VerletParticle(Vector3 startPosition, bool pinned = false, float mass = 1f)
        {
            Position = startPosition;
            PreviousPosition = startPosition;
            IsPinned = pinned;
            Mass = mass;
        }

        /// <summary>Set position and velocity (velocity encoded as a back-dated previous position).</summary>
        public void SetState(Vector3 position, Vector3 velocity, float dt)
        {
            Position = position;
            PreviousPosition = position - velocity * dt;
        }

        public void Integrate(float dt, Vector3 acceleration, float linearDamping)
        {
            if (IsPinned) return;

            // Per-step damping fraction derived from a per-second coefficient → frame-rate independent.
            float damp = 1f - Mathf.Clamp01(linearDamping * dt);
            Vector3 velocity = (Position - PreviousPosition) * damp;

            PreviousPosition = Position;
            Position += velocity + acceleration * (dt * dt);
        }

        public Vector3 GetVelocity(float dt) => dt > 0f ? (Position - PreviousPosition) / dt : Vector3.zero;
    }
}
