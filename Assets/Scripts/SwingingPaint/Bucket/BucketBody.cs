using UnityEngine;
using SwingingPaint.Core;

namespace SwingingPaint.Bucket
{
    /// <summary>
    /// A hand-written 6-DOF rigid body for the paint bucket. No Rigidbody, no
    /// collider, no PhysX — the Newton–Euler equations are integrated directly:
    ///
    ///   Linear:   M·a = ΣF                    ⇒  v += (ΣF/M)·h ,  x += v·h
    ///   Angular:  I·α + ω×(I·ω) = Στ          ⇒  ω updated, q integrated
    ///
    /// It also exposes an XPBD-style positional-correction API so the pendulum
    /// solver can attach the rope to a point on the bucket. Because that point is
    /// offset from the centre of mass, the correction produces a torque — which
    /// is precisely what makes the bucket swing *and* rotate (circular /
    /// elliptical / spiral / chaotic motion) instead of translating rigidly.
    ///
    /// Inertia of the bucket ≈ solid cylinder about its centre of mass:
    ///   I_axis  = ½·M·R²                    (about the cylinder's own axis, local Y)
    ///   I_perp  = (1/12)·M·(3R² + H²)       (about X and Z through the COM)
    /// A hollow-shell blend is provided so a thin metal pail (mass in the wall)
    /// reads differently from a solid block.
    /// </summary>
    public class BucketBody
    {
        // ── Linear state (world space) ───────────────────────────────────────
        public Vector3 Position;        // centre of mass, world
        public Vector3 Velocity;

        // ── Angular state ────────────────────────────────────────────────────
        public Quaternion Orientation = Quaternion.identity;
        public Vector3 AngularVelocity;  // world space [rad/s]

        // ── XPBD history ─────────────────────────────────────────────────────
        Vector3 _prevPosition;
        Quaternion _prevOrientation = Quaternion.identity;

        // ── Mass properties ──────────────────────────────────────────────────
        public float EmptyMass = 1.2f;   // kg, the pail itself
        public float PaintMass = 0f;     // kg, current paint load (set by fluid/UI)
        public float Radius    = 0.15f;  // m
        public float Height    = 0.25f;  // m
        public float WallThickness = 0.01f; // m

        [Range(0f, 1f)]
        public float HollowFactor = 0.6f; // 0 = solid, 1 = thin shell

        public float TotalMass => Mathf.Max(1e-4f, EmptyMass + PaintMass);
        public float InverseMass => 1f / TotalMass;

        // Local principal inverse inertia (diagonal): (x, y=axis, z)
        Vector3 _invInertiaLocal;

        // ── Persistent external load (set by the controller each frame, NOT
        //     cleared by Predict, so a "continuous force" applies to every
        //     sub-step). One-shot hits use the ApplyImpulse* methods instead. ──
        public Vector3 ExternalForce;
        public Vector3 ExternalTorque;

        // ── Attachment / geometry offsets (local space, relative to COM) ─────
        /// <summary>Where the rope grips the bucket (top rim / handle), local space.</summary>
        public Vector3 AttachLocal = new Vector3(0f, 0.14f, 0f);
        /// <summary>Where paint leaves the bucket (the outlet), local space.</summary>
        public Vector3 OutletLocal = new Vector3(0f, -0.14f, 0f);

        public BucketBody()
        {
            RecomputeInertia();
        }

        /// <summary>
        /// Rebuild the inertia tensor from the current mass/size. Call whenever
        /// the paint load or dimensions change (the "current paint mass" affects
        /// how the bucket swings, exactly as the spec requires).
        /// </summary>
        public void RecomputeInertia()
        {
            float M = TotalMass;
            float R = Mathf.Max(1e-3f, Radius);
            float H = Mathf.Max(1e-3f, Height);

            // Solid cylinder.
            float solidAxis = 0.5f * M * R * R;
            float solidPerp = (1f / 12f) * M * (3f * R * R + H * H);

            // Thin shell (mass on the lateral wall): I_axis = M·R², I_perp ≈ M(R²/2 + H²/12).
            float shellAxis = M * R * R;
            float shellPerp = M * (0.5f * R * R + (H * H) / 12f);

            float k = Mathf.Clamp01(HollowFactor);
            float iAxis = Mathf.Lerp(solidAxis, shellAxis, k);
            float iPerp = Mathf.Lerp(solidPerp, shellPerp, k);

            _invInertiaLocal = new Vector3(
                iPerp > 1e-9f ? 1f / iPerp : 0f,
                iAxis > 1e-9f ? 1f / iAxis : 0f,
                iPerp > 1e-9f ? 1f / iPerp : 0f);
        }

        // ── Frame helpers ────────────────────────────────────────────────────
        public Vector3 LocalToWorld(Vector3 local) => Position + Orientation * local;
        public Vector3 WorldAttachPoint => LocalToWorld(AttachLocal);
        public Vector3 WorldOutletPoint => LocalToWorld(OutletLocal);

        /// <summary>Velocity of a world-space point on the body: v + ω × r.</summary>
        public Vector3 PointVelocity(Vector3 worldPoint)
            => Velocity + Vector3.Cross(AngularVelocity, worldPoint - Position);

        /// <summary>Apply the world inverse inertia tensor to a world vector: I⁻¹·w.</summary>
        public Vector3 ApplyWorldInverseInertia(Vector3 w)
        {
            // Rotate into body frame, scale by diagonal invInertia, rotate back.
            Quaternion inv = Quaternion.Inverse(Orientation);
            Vector3 local = inv * w;
            local = new Vector3(local.x * _invInertiaLocal.x,
                                local.y * _invInertiaLocal.y,
                                local.z * _invInertiaLocal.z);
            return Orientation * local;
        }

        // ── Force / torque / impulse API (user interaction + gravity) ────────
        /// <summary>Reset the persistent external load; call once per frame before re-adding it.</summary>
        public void ClearExternalLoad() { ExternalForce = Vector3.zero; ExternalTorque = Vector3.zero; }

        public void AddForce(Vector3 forceWorld) => ExternalForce += forceWorld;

        public void AddForceAtPoint(Vector3 forceWorld, Vector3 worldPoint)
        {
            ExternalForce  += forceWorld;
            ExternalTorque += Vector3.Cross(worldPoint - Position, forceWorld);
        }

        public void AddTorque(Vector3 torqueWorld) => ExternalTorque += torqueWorld;

        /// <summary>Instantaneous change in momentum (a "hit"): Δv = J/M, Δω = I⁻¹(r×J).</summary>
        public void ApplyImpulseAtPoint(Vector3 impulseWorld, Vector3 worldPoint)
        {
            Velocity        += InverseMass * impulseWorld;
            AngularVelocity += ApplyWorldInverseInertia(Vector3.Cross(worldPoint - Position, impulseWorld));
        }

        public void ApplyImpulse(Vector3 impulseWorld) => Velocity += InverseMass * impulseWorld;
        public void ApplyAngularImpulse(Vector3 angImpulseWorld)
            => AngularVelocity += ApplyWorldInverseInertia(angImpulseWorld);

        // ── XPBD phase 1: predict ────────────────────────────────────────────
        /// <summary>
        /// Save history, then advance linear and angular state by h under gravity,
        /// accumulated forces/torques, and (optional) drag. The gyroscopic term
        /// ω×(I·ω) is included for physical correctness of a spinning bucket.
        /// </summary>
        public void Predict(float h, Vector3 gravity, float linearDrag, float angularDrag)
        {
            _prevPosition    = Position;
            _prevOrientation = Orientation;

            // Linear: a = g + F/M  (+ linear drag as an acceleration).
            Vector3 a = gravity + ExternalForce * InverseMass;
            a += -linearDrag * Velocity;                 // simple linear air resistance
            Velocity += a * h;
            Position += Velocity * h;

            // Angular: I·α = τ − ω×(I·ω).
            Vector3 Iw = InertiaTimes(AngularVelocity);
            Vector3 gyro = Vector3.Cross(AngularVelocity, Iw);
            Vector3 angAcc = ApplyWorldInverseInertia(ExternalTorque - gyro);
            AngularVelocity += angAcc * h;
            AngularVelocity *= NumericalIntegrator.DampingFactor(angularDrag, h);

            Orientation = NumericalIntegrator.IntegrateOrientation(Orientation, AngularVelocity, h);
            // NB: ExternalForce/ExternalTorque are intentionally NOT cleared here —
            // the controller owns their lifetime (reset once per frame).
        }

        /// <summary>World-space I·w (forward inertia application), for the gyroscopic term.</summary>
        Vector3 InertiaTimes(Vector3 w)
        {
            Quaternion inv = Quaternion.Inverse(Orientation);
            Vector3 local = inv * w;
            local = new Vector3(
                _invInertiaLocal.x > 0f ? local.x / _invInertiaLocal.x : 0f,
                _invInertiaLocal.y > 0f ? local.y / _invInertiaLocal.y : 0f,
                _invInertiaLocal.z > 0f ? local.z / _invInertiaLocal.z : 0f);
            return Orientation * local;
        }

        // ── XPBD phase 2: positional correction at a point ───────────────────
        /// <summary>
        /// Generalized inverse mass for a unit correction direction n applied at
        /// worldPoint:  w = 1/M + (r×n)ᵀ·I⁻¹·(r×n),  r = worldPoint − COM.
        /// This is what the attachment/grab constraints use to distribute a
        /// positional correction between the rope node and the bucket.
        /// </summary>
        public float GeneralizedInverseMass(Vector3 worldPoint, Vector3 dirUnit)
        {
            Vector3 r = worldPoint - Position;
            Vector3 rn = Vector3.Cross(r, dirUnit);
            return InverseMass + Vector3.Dot(rn, ApplyWorldInverseInertia(rn));
        }

        /// <summary>
        /// Apply an XPBD position impulse P at worldPoint:
        ///   Δx = (1/M)·P ,  Δq from I⁻¹(r×P). Moves and rotates the body so the
        ///   attachment point tracks its target.
        /// </summary>
        public void ApplyPositionalImpulse(Vector3 P, Vector3 worldPoint)
        {
            Position += InverseMass * P;

            Vector3 r = worldPoint - Position;
            Vector3 dOmega = ApplyWorldInverseInertia(Vector3.Cross(r, P));

            // Δq = ½·(dOmega as quaternion)·q ; renormalise.
            Quaternion dq = new Quaternion(dOmega.x, dOmega.y, dOmega.z, 0f) * Orientation;
            Orientation = NumericalIntegrator.Normalize(new Quaternion(
                Orientation.x + 0.5f * dq.x,
                Orientation.y + 0.5f * dq.y,
                Orientation.z + 0.5f * dq.z,
                Orientation.w + 0.5f * dq.w));
        }

        // ── XPBD phase 3: derive velocities from the positional change ───────
        public void FinalizeVelocities(float h)
        {
            float invH = 1f / h;
            Velocity = (Position - _prevPosition) * invH;

            // Angular velocity from the delta rotation q·q_prevᐨ (small-angle form).
            Quaternion dq = Orientation * Quaternion.Inverse(_prevOrientation);
            if (dq.w < 0f) { dq.x = -dq.x; dq.y = -dq.y; dq.z = -dq.z; dq.w = -dq.w; }
            Vector3 axis = new Vector3(dq.x, dq.y, dq.z) * 2f * invH;
            AngularVelocity = axis;
        }
    }
}
