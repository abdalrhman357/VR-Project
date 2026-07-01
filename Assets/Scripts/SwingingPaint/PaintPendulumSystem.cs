using UnityEngine;
using SwingingPaint.Core;
using SwingingPaint.Rope;
using SwingingPaint.Bucket;
using SwingingPaint.Suspension;

namespace SwingingPaint
{
    /// <summary>
    /// Orchestrates the whole "swinging paint bucket": suspension anchor → rope →
    /// bucket, all integrated by hand with XPBD (no Unity physics). It owns a
    /// fixed-time-step loop for determinism, couples the rope tail to the bucket
    /// handle (which generates the pendulum torque), drives the fluid container
    /// transform, and turns raw user input into real forces/impulses/torques.
    ///
    /// Assign <see cref="simulationContainer"/> to the Transform that carries the
    /// fluid simulation; this component then fully controls its position and
    /// rotation, so the paint sloshes and pours according to the physics here.
    ///
    /// Controls (all resolve to physics, never scripted animation):
    ///   • Left mouse (hold)  : grab & pull/move the bucket (soft handle)
    ///   • Left mouse + Shift : grab & twist (adds torque from cursor motion)
    ///   • P                  : push (impulse along the view direction)
    ///   • F (hold)           : apply a continuous force along the view direction
    ///   • Q / E              : apply torque (spin) about world up
    ///   • Space              : apply an upward kick impulse
    /// </summary>
    [DefaultExecutionOrder(-90)] // step physics before renderers/fluid read the transform
    public class PaintPendulumSystem : MonoBehaviour
    {
        // ── References ───────────────────────────────────────────────────────
        [Header("References")]
        [Tooltip("Transform that carries the fluid simulation (its position & rotation are driven by this system).")]
        public Transform simulationContainer;

        [Tooltip("Camera used for mouse interaction. Defaults to Camera.main.")]
        public Camera interactionCamera;

        // ── Suspension ───────────────────────────────────────────────────────
        [Header("Suspension")]
        public SuspensionAnchor anchor = new SuspensionAnchor();

        // ── Rope ─────────────────────────────────────────────────────────────
        [Header("Rope")]
        public RopeType ropeType = RopeType.Nylon;
        [Tooltip("Rope rest length in metres.")]
        public float ropeLength = 2.0f;
        [Range(2, 128)]
        public int ropeNodes = 24;
        [Tooltip("Overrides the preset when RopeType = Custom, or after 'Apply Material'.")]
        public RopeMaterial customMaterial = new RopeMaterial();

        // ── Bucket ───────────────────────────────────────────────────────────
        [Header("Bucket")]
        public float emptyMass = 1.2f;
        [Tooltip("Current paint mass [kg]. Feed this from the fluid load; it changes the swing.")]
        public float paintMass = 0.5f;
        [Tooltip("If >0, overrides bucket radius/height; otherwise read from container localScale.")]
        public float bucketRadiusOverride = 0f;
        public float bucketHeightOverride = 0f;
        [Range(0f, 1f)] public float hollowFactor = 0.6f;
        [Tooltip("Where the rope grips the bucket, as a fraction of half-height (1 = top rim).")]
        [Range(-1f, 1.5f)] public float attachHeightFactor = 1.0f;
        [Tooltip("Sideways offset of the handle grip (breaks symmetry → richer motion).")]
        public Vector2 attachLateralOffset = Vector2.zero;
        [Tooltip("Initial velocity given to the bucket on build/reset (for a lively start).")]
        public Vector3 initialBucketVelocity = Vector3.zero;

        // ── Solver ───────────────────────────────────────────────────────────
        [Header("Solver")]
        [Tooltip("Fixed simulation rate [Hz] for the accumulator.")]
        public float simulationHz = 60f;
        [Range(1, 32)]
        [Tooltip("XPBD sub-steps per fixed step. More = stiffer ropes stay stable.")]
        public int substeps = 8;
        [Range(1, 16)]
        public int solverIterations = 2;
        [Tooltip("Max fixed steps processed per frame (prevents the spiral of death).")]
        public int maxStepsPerFrame = 8;
        public float timeScale = 1f;

        // ── Environment ──────────────────────────────────────────────────────
        [Header("Environment")]
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        [Tooltip("Air density [kg/m³] used for aerodynamic drag.")]
        public float airDensity = 1.225f;
        [Tooltip("Bucket linear air-resistance coefficient [1/s].")]
        public float bucketLinearDrag = 0.15f;
        [Tooltip("Bucket angular air-resistance coefficient [1/s].")]
        public float bucketAngularDrag = 0.4f;

        // ── Coupling / interaction stiffness ────────────────────────────────
        [Header("Coupling")]
        [Tooltip("Compliance of the rope↔bucket attachment (0 = rigid handle).")]
        public float attachmentCompliance = 0.0f;
        [Tooltip("Compliance of the grab handle when the user drags (higher = softer).")]
        public float grabCompliance = 2e-4f;
        [Tooltip("Cap the rope at a material-derived maximum length (stops a heavy bucket sagging on a stiff rope; elastic ropes still stretch up to that cap).")]
        public bool enforceRopeLimit = true;
        [Tooltip("Dynamic overshoot allowed beyond the analytic static stretch before the hard cap engages.")]
        public float ropeStretchMargin = 3f;
        [Tooltip("Keep the bucket from tilting (spherical-pendulum mode). Needed while the SPH fluid collision is a non-rotating cylinder, so the paint stays inside and pours from the bottom. Real tilting comes with the fluid-core rewrite.")]
        public bool keepBucketUpright = false;
        public float pushImpulse = 3f;
        public float continuousForce = 20f;
        public float torqueStrength = 2.5f;
        public float kickImpulse = 4f;

        [Header("Container follow")]
        [Tooltip("Local offset from the bucket COM to the container origin.")]
        public Vector3 containerLocalOffset = Vector3.zero;

        [Header("Debug")]
        public bool drawGizmos = true;

        // ── Runtime state ────────────────────────────────────────────────────
        RopeSimulator _rope;
        BucketBody _bucket;
        RopeMaterial _material;
        float[] _stretchLambda;
        float _accumulator;
        float _limitLength;   // material-derived max rope length (inextensible cap)

        // grab state
        bool _grabbing;
        Vector3 _grabLocal;      // grabbed point in bucket local space
        Vector3 _grabTarget;     // world target the grip is pulled toward
        Vector3 _grabPlanePoint; // point defining the drag plane
        Vector3 _grabPlaneNormal;
        Vector2 _lastMouse;

        // ── Public read-only stats (for the future GUI) ─────────────────────
        public BucketBody Bucket => _bucket;
        public RopeSimulator Rope => _rope;
        public float BucketSpeed => _bucket != null ? _bucket.Velocity.magnitude : 0f;
        public float AngularSpeed => _bucket != null ? _bucket.AngularVelocity.magnitude : 0f;
        public float RopeTension => _rope != null ? _rope.MaxTensionNow() : 0f;
        public float RopeStretch => _rope != null ? _rope.CurrentLength() - _rope.RestLength : 0f;
        public bool RopeBroken => _rope != null && _rope.IsBroken();
        public Vector3 AttachmentPoint => _bucket != null ? _bucket.WorldAttachPoint : Vector3.zero;
        public Vector3 OutletPoint => _bucket != null ? _bucket.WorldOutletPoint : Vector3.zero;

        /// <summary>Translational + rotational kinetic energy of the bucket [J], for the stats panel.</summary>
        public float KineticEnergy
        {
            get
            {
                if (_bucket == null) return 0f;
                float linear = 0.5f * _bucket.TotalMass * _bucket.Velocity.sqrMagnitude;
                float angular = 0.5f * Vector3.Dot(_bucket.AngularVelocity, _bucket.AngularVelocity); // I-weighted below is minor; approx
                return linear + angular;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            if (interactionCamera == null) interactionCamera = Camera.main;
            DisableLegacyDragger();
            BuildSystem();
        }

        /// <summary>The old scripted dragger fights us for the transform — turn it off if present.</summary>
        void DisableLegacyDragger()
        {
            if (simulationContainer == null) return;
            var legacy = simulationContainer.GetComponent("CylinderDragger") as Behaviour;
            if (legacy != null)
            {
                legacy.enabled = false;
                Debug.Log("[PaintPendulumSystem] Disabled CylinderDragger — physics now controls the container.");
            }
        }

        /// <summary>(Re)build the rope + bucket from the current inspector values.</summary>
        public void BuildSystem()
        {
            // Material: preset unless Custom.
            _material = ropeType == RopeType.Custom ? customMaterial.Clone()
                                                    : RopeMaterial.CreatePreset(ropeType);

            // Bucket dimensions from container scale (unless overridden).
            float radius = bucketRadiusOverride > 0f ? bucketRadiusOverride
                         : (simulationContainer != null ? simulationContainer.localScale.x * 0.5f : 0.15f);
            float height = bucketHeightOverride > 0f ? bucketHeightOverride
                         : (simulationContainer != null ? simulationContainer.localScale.y : 0.25f);

            _bucket = new BucketBody
            {
                EmptyMass = emptyMass,
                PaintMass = paintMass,
                Radius = radius,
                Height = height,
                HollowFactor = hollowFactor,
                AttachLocal = new Vector3(attachLateralOffset.x, height * 0.5f * attachHeightFactor, attachLateralOffset.y),
                OutletLocal = new Vector3(0f, -height * 0.5f, 0f)
            };
            _bucket.RecomputeInertia();

            // Place the rig: head at anchor, bucket hanging below so the handle
            // meets the rope tail.
            Vector3 anchorPos = anchor.GetWorldPosition();
            float dropToCOM = ropeLength + _bucket.AttachLocal.y;
            _bucket.Position = anchorPos - Vector3.up * dropToCOM;
            _bucket.Orientation = Quaternion.identity;
            _bucket.Velocity = initialBucketVelocity;
            _bucket.AngularVelocity = Vector3.zero;

            Vector3 tailStart = _bucket.WorldAttachPoint;
            _rope = new RopeSimulator(anchorPos, tailStart, ropeNodes, _material);
            _rope.RestSegmentLengthOverride(ropeLength);   // pin rest length to the requested value
            _stretchLambda = new float[Mathf.Max(1, _rope.SegmentCount)];

            ComputeLimitLength();
            SyncContainer();
        }

        /// <summary>
        /// Derive the inextensible cap from the material: static strain under the
        /// current load, ε = (M·g)/(E·A), then allow a dynamic overshoot margin.
        /// Steel → cap ≈ rest length (taut); rubber/elastic → looser cap.
        /// </summary>
        void ComputeLimitLength()
        {
            if (_rope == null || _bucket == null) { _limitLength = ropeLength; return; }
            float A = _material.CrossSectionArea;
            float E = Mathf.Max(1e-3f, _material.youngsModulus);
            float staticStrain = (_bucket.TotalMass * Mathf.Max(0.01f, gravity.magnitude)) / (E * A);
            float allow = Mathf.Clamp(staticStrain * Mathf.Max(0f, ropeStretchMargin), 0.0005f, 0.6f);
            _limitLength = _rope.RestLength * (1f + allow);
        }

        // ─────────────────────────────────────────────────────────────────────
        void Update()
        {
            if (_rope == null || _bucket == null) return;

            // Keep live-editable values flowing into the sim.
            SyncEditableParams();

            HandleInput();

            // Fixed-step accumulator → deterministic, frame-rate-independent physics.
            float fixedDt = 1f / Mathf.Max(1f, simulationHz);
            _accumulator += Mathf.Max(0f, Time.deltaTime) * Mathf.Max(0f, timeScale);
            _accumulator = Mathf.Min(_accumulator, fixedDt * maxStepsPerFrame);

            int steps = 0;
            while (_accumulator >= fixedDt && steps < maxStepsPerFrame)
            {
                StepSimulation(fixedDt);
                _accumulator -= fixedDt;
                steps++;
            }

            SyncContainer();
        }

        void SyncEditableParams()
        {
            if (Mathf.Abs(_bucket.PaintMass - paintMass) > 1e-5f ||
                Mathf.Abs(_bucket.EmptyMass - emptyMass) > 1e-5f)
            {
                _bucket.PaintMass = paintMass;
                _bucket.EmptyMass = emptyMass;
                _bucket.RecomputeInertia();
            }
        }

        // ── Core fixed step: run the XPBD sub-steps ──────────────────────────
        void StepSimulation(float dt)
        {
            float h = dt / Mathf.Max(1, substeps);
            Vector3 anchorPos = anchor.GetWorldPosition();
            ComputeLimitLength();   // adapt the cap to live paint-mass / material changes

            for (int s = 0; s < substeps; s++)
            {
                // Phase 1 — predict.
                _rope.Predict(h, gravity, airDensity);
                _rope.DriveNode(0, anchorPos);                       // pin head to anchor
                _bucket.Predict(h, gravity, bucketLinearDrag, bucketAngularDrag);

                // XPBD λ accumulates within a sub-step, reset before iterating.
                for (int i = 0; i < _stretchLambda.Length; i++) _stretchLambda[i] = 0f;

                // Phase 2 — solve constraints (all together for stability).
                for (int it = 0; it < solverIterations; it++)
                {
                    _rope.SolveStretch(h, _stretchLambda);
                    _rope.SolveBending(h);
                    SolveAttachment(h);
                    SolveRopeLimit(anchorPos);
                    if (_grabbing) SolveGrab(h);
                    _rope.DriveNode(0, anchorPos);                   // keep head exact
                }

                // Phase 3 — derive velocities.
                _rope.FinalizeVelocities(h);
                _bucket.FinalizeVelocities(h);

                // Spherical-pendulum mode: discard tilt so the (non-rotating) fluid
                // container stays vertical and the paint keeps pouring from the base.
                // The swing still traces circular / elliptical / precessing orbits.
                if (keepBucketUpright)
                {
                    _bucket.Orientation = Quaternion.identity;
                    _bucket.AngularVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>
        /// Couple the rope tail to the bucket handle with a point-to-point XPBD
        /// constraint. The correction is applied at an offset from the COM, so it
        /// injects the torque that turns translation into swing+spin.
        /// </summary>
        void SolveAttachment(float h)
        {
            int tail = _rope.TailIndex;
            Vector3 attach = _bucket.WorldAttachPoint;
            Vector3 diff = _rope.pos[tail] - attach;     // drive to zero
            float c = diff.magnitude;
            if (c < 1e-9f) return;
            Vector3 n = diff / c;

            float wRope = _rope.invMass[tail];
            float wBody = _bucket.GeneralizedInverseMass(attach, n);
            float aTilde = attachmentCompliance / (h * h);
            float denom = wRope + wBody + aTilde;
            if (denom <= 1e-12f) return;

            float lambda = c / denom;
            _rope.pos[tail] -= wRope * lambda * n;                 // rope tail → attach
            _bucket.ApplyPositionalImpulse(lambda * n, attach);    // attach → rope tail (+torque)
        }

        /// <summary>
        /// Unilateral inextensible cap: an ideal rope can pull but not push, so we
        /// only correct when the bucket handle is *farther* from the anchor than
        /// the material-derived limit. Below the limit the rope is slack and the
        /// bucket is free (e.g. after an upward kick).
        /// </summary>
        void SolveRopeLimit(Vector3 anchorPos)
        {
            if (!enforceRopeLimit) return;
            Vector3 attach = _bucket.WorldAttachPoint;
            Vector3 d = attach - anchorPos;
            float dist = d.magnitude;
            if (dist <= _limitLength || dist < 1e-6f) return;

            Vector3 n = d / dist;
            float c = dist - _limitLength;
            float w = _bucket.GeneralizedInverseMass(attach, n);
            if (w <= 1e-12f) return;

            float lambda = c / w;
            _bucket.ApplyPositionalImpulse(-lambda * n, attach);   // pull bucket back toward anchor
        }

        /// <summary>Pull the grabbed point on the bucket toward the mouse target (soft handle).</summary>
        void SolveGrab(float h)
        {
            Vector3 gp = _bucket.LocalToWorld(_grabLocal);
            Vector3 diff = _grabTarget - gp;
            float c = diff.magnitude;
            if (c < 1e-9f) return;
            Vector3 n = diff / c;

            float w = _bucket.GeneralizedInverseMass(gp, n);
            float aTilde = grabCompliance / (h * h);
            float denom = w + aTilde;
            if (denom <= 1e-12f) return;

            float lambda = c / denom;
            _bucket.ApplyPositionalImpulse(lambda * n, gp);        // gp → target
        }

        // ── Drive the fluid container from the bucket state ──────────────────
        void SyncContainer()
        {
            if (simulationContainer == null || _bucket == null) return;
            Vector3 worldPos = _bucket.Position + _bucket.Orientation * containerLocalOffset;
            simulationContainer.SetPositionAndRotation(worldPos, _bucket.Orientation);
        }

        // ── User interaction (hand-rolled ray math, no Physics.Raycast) ──────
        void HandleInput()
        {
            if (interactionCamera == null) interactionCamera = Camera.main;

            // Persistent external load lasts exactly one frame: reset, then re-add
            // whatever is being held this frame. One-shot impulses bypass this.
            _bucket.ClearExternalLoad();

            if (interactionCamera == null) return;

            Vector2 mouse = Input.mousePosition;

            // Grab.
            if (Input.GetMouseButtonDown(0)) TryBeginGrab(mouse);
            if (Input.GetMouseButtonUp(0)) _grabbing = false;

            if (_grabbing)
            {
                UpdateGrabTarget(mouse);

                // Twist while grabbing with Shift held → torque from cursor motion.
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    Vector2 dm = mouse - _lastMouse;
                    _bucket.AddTorque(interactionCamera.transform.forward * (dm.x * torqueStrength * 0.02f));
                }
            }
            _lastMouse = mouse;

            Vector3 viewDir = interactionCamera.transform.forward;

            // Push (impulse).
            if (Input.GetKeyDown(KeyCode.P))
                _bucket.ApplyImpulseAtPoint(viewDir.normalized * pushImpulse, _bucket.Position);

            // Continuous force while held.
            if (Input.GetKey(KeyCode.F))
                _bucket.AddForce(viewDir.normalized * continuousForce);

            // Torque (spin) about world up.
            if (Input.GetKey(KeyCode.Q)) _bucket.AddTorque(Vector3.up * torqueStrength);
            if (Input.GetKey(KeyCode.E)) _bucket.AddTorque(Vector3.down * torqueStrength);

            // Upward kick impulse.
            if (Input.GetKeyDown(KeyCode.Space))
                _bucket.ApplyImpulse(Vector3.up * kickImpulse);

            // R also resets the fluid (FluidSim), which respawns the paint block at
            // its authored position — so return the bucket to rest to catch it.
            if (Input.GetKeyDown(KeyCode.R))
                BuildSystem();
        }

        void TryBeginGrab(Vector2 mouse)
        {
            Ray ray = interactionCamera.ScreenPointToRay(mouse);
            float grabRadius = Mathf.Max(_bucket.Radius, _bucket.Height * 0.5f) * 1.1f;
            if (RaySphere(ray.origin, ray.direction.normalized, _bucket.Position, grabRadius, out float t))
            {
                Vector3 hit = ray.origin + ray.direction.normalized * t;
                _grabbing = true;
                _grabLocal = Quaternion.Inverse(_bucket.Orientation) * (hit - _bucket.Position);
                _grabPlanePoint = hit;
                _grabPlaneNormal = -interactionCamera.transform.forward; // face the camera
                _grabTarget = hit;
            }
        }

        void UpdateGrabTarget(Vector2 mouse)
        {
            Ray ray = interactionCamera.ScreenPointToRay(mouse);
            if (RayPlane(ray.origin, ray.direction.normalized, _grabPlanePoint, _grabPlaneNormal, out Vector3 p))
                _grabTarget = p;
        }

        // ── Ray helpers (manual analytic intersection) ───────────────────────
        static bool RaySphere(Vector3 ro, Vector3 rd, Vector3 centre, float radius, out float t)
        {
            Vector3 oc = ro - centre;
            float b = Vector3.Dot(oc, rd);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) { t = 0f; return false; }
            float s = Mathf.Sqrt(disc);
            t = -b - s;
            if (t < 0f) t = -b + s;
            return t >= 0f;
        }

        static bool RayPlane(Vector3 ro, Vector3 rd, Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
        {
            float denom = Vector3.Dot(planeNormal, rd);
            if (Mathf.Abs(denom) < 1e-6f) { hit = ro; return false; }
            float t = Vector3.Dot(planeNormal, planePoint - ro) / denom;
            hit = ro + rd * t;
            return t >= 0f;
        }

        // ── Public API used by the (future) GUI ──────────────────────────────
        public void SetRopeType(RopeType t) { ropeType = t; BuildSystem(); }
        public void SetPaintMass(float kg) { paintMass = Mathf.Max(0f, kg); }
        public void ResetSystem() { BuildSystem(); }

        // ── Gizmos ────────────────────────────────────────────────────────────
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            anchor?.DrawGizmo();

            if (_rope != null)
            {
                for (int s = 0; s < _rope.SegmentCount; s++)
                {
                    Gizmos.color = _rope.segmentBroken[s]
                        ? Color.red
                        : Color.Lerp(Color.green, Color.yellow,
                            Mathf.Clamp01(_rope.segmentTension[s] / Mathf.Max(1e-3f, _material.MaxTension)));
                    Gizmos.DrawLine(_rope.pos[s], _rope.pos[s + 1]);
                }
                for (int i = 0; i < _rope.NodeCount; i++)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawSphere(_rope.pos[i], _material.diameter * 0.5f + 0.005f);
                }
            }

            if (_bucket != null)
            {
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
                Gizmos.DrawWireSphere(_bucket.Position, 0.02f);
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_bucket.WorldAttachPoint, 0.015f);
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(_bucket.WorldOutletPoint, 0.015f);
            }
        }
    }
}
