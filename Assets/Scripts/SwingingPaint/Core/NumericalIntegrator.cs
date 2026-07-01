using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Hand-written, allocation-free numerical integrators used across the
    /// SwingingPaint module. No Unity physics is involved — these are the raw
    /// time-stepping schemes the rope and bucket build on.
    ///
    /// We expose several schemes on purpose: the project spec asks for "stable
    /// numerical integration" and an explanation of *why* one method beats
    /// another, so having them side by side documents that trade-off.
    ///
    /// Notation:  x = position, v = velocity, a = acceleration, h = time step.
    /// </summary>
    public static class NumericalIntegrator
    {
        // ────────────────────────────────────────────────────────────────────
        //  Semi-implicit (symplectic) Euler
        //  v_{n+1} = v_n + h·a(x_n)
        //  x_{n+1} = x_n + h·v_{n+1}      ← uses the NEW velocity
        //
        //  O(h) accuracy, but symplectic: it conserves a *nearby* energy, so a
        //  frictionless pendulum does not gain energy and blow up the way plain
        //  (explicit) Euler does. This is the default for real-time physics and
        //  is exactly the "predict" step of XPBD.
        //  Cost: 1 acceleration evaluation per step — O(1).
        // ────────────────────────────────────────────────────────────────────
        public static void SemiImplicitEuler(ref Vector3 x, ref Vector3 v, Vector3 a, float h)
        {
            v += a * h;
            x += v * h;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Position (Störmer–)Verlet, velocity-less form.
        //  x_{n+1} = 2·x_n − x_{n-1} + a·h²
        //  Velocity is recovered as (x_{n+1} − x_{n-1}) / (2h) when needed.
        //
        //  Time-reversible and symplectic; the workhorse of cloth/rope solvers
        //  because it stays stable even when we clamp positions (constraints)
        //  between steps. Returns the new position; caller rotates the history.
        //  Cost: O(1).
        // ────────────────────────────────────────────────────────────────────
        public static Vector3 Verlet(Vector3 x, Vector3 xPrev, Vector3 a, float h)
        {
            return 2f * x - xPrev + a * (h * h);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Classic 4th-order Runge–Kutta for x'' = a(x, v).
        //  O(h⁴) accuracy — used only where we want a high-fidelity reference
        //  (e.g. validating the pendulum against the analytic small-angle
        //  solution). Four acceleration evaluations per step, so ~4× the cost of
        //  Euler; not symplectic, so it slowly bleeds energy over very long runs.
        //
        //  'accel' is a callback a(x, v) so the caller can plug in any force law.
        // ────────────────────────────────────────────────────────────────────
        public static void RK4(ref Vector3 x, ref Vector3 v,
                               System.Func<Vector3, Vector3, Vector3> accel, float h)
        {
            Vector3 k1x = v;
            Vector3 k1v = accel(x, v);

            Vector3 k2x = v + 0.5f * h * k1v;
            Vector3 k2v = accel(x + 0.5f * h * k1x, v + 0.5f * h * k1v);

            Vector3 k3x = v + 0.5f * h * k2v;
            Vector3 k3v = accel(x + 0.5f * h * k2x, v + 0.5f * h * k2v);

            Vector3 k4x = v + h * k3v;
            Vector3 k4v = accel(x + h * k3x, v + h * k3v);

            x += (h / 6f) * (k1x + 2f * k2x + 2f * k3x + k4x);
            v += (h / 6f) * (k1v + 2f * k2v + 2f * k3v + k4v);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Rigid-body orientation update.
        //  q̇ = ½ · ω_quat ⊗ q   (ω_quat = (ω, 0), ω = world angular velocity)
        //  q_{n+1} = normalize(q_n + h·q̇)
        //
        //  First-order but re-normalised every step, which keeps the quaternion
        //  on the unit sphere. This is the standard explicit attitude update and
        //  is what the bucket uses for its spin. Cost: O(1).
        // ────────────────────────────────────────────────────────────────────
        public static Quaternion IntegrateOrientation(Quaternion q, Vector3 omega, float h)
        {
            // ω_quat ⊗ q  (Hamilton product with a pure-vector quaternion)
            Quaternion wq = new Quaternion(omega.x, omega.y, omega.z, 0f) * q;
            q.x += 0.5f * h * wq.x;
            q.y += 0.5f * h * wq.y;
            q.z += 0.5f * h * wq.z;
            q.w += 0.5f * h * wq.w;
            return Normalize(q);
        }

        /// <summary>Numerically safe quaternion normalisation (falls back to identity).</summary>
        public static Quaternion Normalize(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-12f) return Quaternion.identity;
            float inv = 1f / mag;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        /// <summary>
        /// Frame-rate-independent exponential damping factor.
        /// Multiplying a velocity by this each step gives the same decay whether
        /// the step is large or small: v *= DampingFactor(rate, h).
        /// Derived from the continuous law v̇ = −rate·v ⇒ v(t) = v₀·e^(−rate·t).
        /// </summary>
        public static float DampingFactor(float rate, float h)
        {
            return Mathf.Exp(-Mathf.Max(0f, rate) * h);
        }
    }
}
