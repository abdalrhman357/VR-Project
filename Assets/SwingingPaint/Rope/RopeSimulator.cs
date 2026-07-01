using UnityEngine;
using SwingingPaint.Core;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// MonoBehaviour driver for the Verlet pendulum rope. Normally stepped by SimulationManager
    /// inside its fixed-step loop (selfDrive = false). If dropped into a scene alone with
    /// selfDrive = true it runs on its own FixedStepClock so the subsystem is demonstrable in
    /// isolation. Exposes the bucket attach point (tip position/velocity) for BucketSimulator.
    /// </summary>
    public sealed class RopeSimulator : MonoBehaviour
    {
        [Tooltip("Run standalone on an internal clock (true), or be stepped by SimulationManager (false).")]
        public bool selfDrive = false;

        [SerializeField] SimulationConfig _config = new SimulationConfig();
        public SimulationConfig Config { get => _config; set => _config = value; }

        public Rope Rope { get; private set; }
        public bool IsInitialized => Rope != null;

        public Vector3 TipPosition => Rope != null ? Rope.Tip.Position : transform.position;
        public Vector3 TipVelocity(float dt) => Rope != null ? Rope.Tip.GetVelocity(dt) : Vector3.zero;

        /// <summary>Unit vector pointing down the rope at the tip (tip ← previous particle).</summary>
        public Vector3 TipDownDirection
        {
            get
            {
                if (Rope == null || Rope.Particles.Count < 2) return Vector3.down;
                int n = Rope.Particles.Count;
                Vector3 d = Rope.Particles[n - 1].Position - Rope.Particles[n - 2].Position;
                return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.down;
            }
        }

        FixedStepClock _clock;
        float _lastStepDt;

        // Build only if the manager (which runs earlier) hasn't already initialized us.
        void Awake() { if (Rope == null) Initialize(); }

        void OnEnable() { if (Rope == null) Initialize(); }

        /// <summary>(Re)build the rope from the current config and apply the initial release state.</summary>
        public void Initialize()
        {
            var r = _config.rope;
            float payload = _config.bucket.emptyMass + PaintMassEstimate();
            Rope = new Rope(r.anchorPosition, r.length, r.segmentCount, r.material, r.bendingStiffness, payload)
            {
                ConstraintIterations = r.constraintIterations
            };
            ApplyInitialRelease(r);

            _clock = new FixedStepClock(_config.world.fixedStep, _config.world.maxSubStepsPerFrame);
            _lastStepDt = _config.world.fixedStep;
        }

        float PaintMassEstimate()
        {
            // Rough payload contribution from paint; refined later when the fluid reports volume.
            var b = _config.bucket;
            float bucketVolume = Mathf.PI * b.radius * b.radius * (b.heightScale * 0.4f);
            return bucketVolume * _config.paint.fillAmount * (_config.paint.density / 1000f);
        }

        /// <summary>Rotate the hanging rope to the release angle and seed the tip's initial velocity.</summary>
        void ApplyInitialRelease(SimulationConfig.RopeSettings r)
        {
            // Rotation about Z tilts the down-vector into ±X (left/right = initialAngleX);
            // rotation about X tilts it into ±Z (forward/back = initialAngleZ).
            Quaternion rot = Quaternion.AngleAxis(r.initialAngleX, Vector3.forward)
                           * Quaternion.AngleAxis(r.initialAngleZ, Vector3.right);
            Vector3 anchor = Rope.Anchor.Position;
            foreach (var p in Rope.Particles)
            {
                if (p.IsPinned) continue;
                p.Position = anchor + rot * (p.Position - anchor);
                p.PreviousPosition = p.Position; // start at rest in the rotated pose
            }

            // Conical-pendulum kick: convert the desired tip velocity into a rigid angular velocity
            // about the anchor and apply it to EVERY particle (∝ distance). A push perpendicular to the
            // release angle makes the bucket orbit (circle/ellipse); damping then spirals it inward —
            // this is what turns the straight-line swing into the spiral/flower painting pattern.
            Vector3 vTip = new Vector3(r.initialTipVelocity.x, 0f, r.initialTipVelocity.y);
            if (vTip.sqrMagnitude > 1e-6f)
            {
                Vector3 rTip = Rope.Tip.Position - anchor;
                float rLen2 = Mathf.Max(rTip.sqrMagnitude, 1e-6f);
                Vector3 omega = Vector3.Cross(rTip, vTip) / rLen2;     // ω = (r × v)/|r|²
                float dt = _config.world.fixedStep;
                foreach (var p in Rope.Particles)
                {
                    if (p.IsPinned) continue;
                    Vector3 vel = Vector3.Cross(omega, p.Position - anchor);
                    p.PreviousPosition = p.Position - vel * dt;
                }
            }
        }

        /// <summary>One fixed sub-step. Called by SimulationManager (or self-drive).</summary>
        public void Step(float dt)
        {
            if (Rope == null) return;
            var w = _config.world;
            // Total acceleration = gravity (−Y) + wind (world-space), per Part 5 configurability.
            Vector3 accel = new Vector3(w.wind.x, -w.gravity + w.wind.y, w.wind.z);
            Rope.Step(dt, accel, w.airResistance);
            _lastStepDt = dt;
        }

        void Update()
        {
            if (!selfDrive || Rope == null) return;
            _clock.Configure(_config.world.fixedStep, _config.world.maxSubStepsPerFrame);
            _clock.Accumulate(Time.deltaTime * _config.world.simulationSpeed);
            while (_clock.TryConsumeStep(out float dt)) Step(dt);
        }

        public float LastStepDt => _lastStepDt;

        void OnDrawGizmos()
        {
            if (Rope == null)
            {
                // Editor preview of the anchor before play.
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_config.rope.anchorPosition, 0.05f);
                return;
            }
            Gizmos.color = new Color(0.4f, 0.7f, 1f);
            for (int i = 0; i < Rope.Particles.Count - 1; i++)
                Gizmos.DrawLine(Rope.Particles[i].Position, Rope.Particles[i + 1].Position);
            Gizmos.color = new Color(1f, 0.8f, 0f);
            Gizmos.DrawSphere(TipPosition, 0.05f);
        }
    }
}
