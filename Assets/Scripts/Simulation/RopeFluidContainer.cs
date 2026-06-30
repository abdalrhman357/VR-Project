using UnityEngine;
using Seb.Fluid.Simulation;

public class RopeFluidContainer : MonoBehaviour
{
    [Header("Rope Settings")]
    public float ropeLength = 10f;
    public int segmentCount = 15;
    public RopeMaterial ropeMaterial = RopeMaterial.Cotton; // تم تغييرها إلى القطن ليكون مشدوداً
    public float bendingStiffness = 0.2f;
    [Tooltip("وزن السطل البلاستيكي وهو فارغ تماماً.")]
    public float emptyBucketMass = 5f;
    [Tooltip("الكتلة الكلية للحبل بأكمله. في الواقع الحبل خفيف جداً (مثلاً 0.5) لكي لا يؤثر على حركة السطل الثقيل.")]
    public float ropeTotalMass = 0.5f;
    [Tooltip("الكتلة الفيزيائية للجزيء الواحد (تعتمد على الكثافة). سيُحسب وزن الطلاء بضربها في عدد الجزيئات.")]
    public float fluidParticleMass = 0.012f;

    [Tooltip("Air-drag damping coefficient. Controls how many swings the pendulum makes before stopping.\n\n"
           + "0.0005 → ~3% loss/sec (very long swing, near-vacuum)\n"
           + "0.002  → ~11% loss/sec (realistic air drag, many swings)\n"
           + "0.005  → ~26% loss/sec (heavy air drag, fewer swings)\n"
           + "0.02   → ~70% loss/sec (too heavy, stops almost immediately)")]
    [Range(0.0001f, 0.01f)]
    public float ropeDamping = 0.002f;

    public Color ropeColor = Color.white;
    public float ropeWidth = 0.05f;

    [Header("Camera Setup")]
    public bool applyOverheadCamera = true;
    [Tooltip("If true, the camera smoothly tracks the bucket. If false, it stays static for a cinematic view.")]
    public bool followBucket = false;
    /// <summary>
    /// Cinematic Offset: Placed to the side and slightly elevated to see the bucket, falling fluid, and canvas clearly.
    /// </summary>
    public Vector3 cameraOffset = new Vector3(15f, -2f, -15f);

    [Header("Pendulum Swing (applied at simulation start)")]
    [Tooltip("Pull the bucket to the side before releasing it (e.g. X axis).")]
    public Vector3 pullOffset = new Vector3(1f, 0f,0f); 

    [Tooltip("Push the bucket sideways as it's released to create a circular/spiral motion (e.g. Z axis). Set to zero for one-directional swing.")]
    public Vector3 pushTangential = Vector3.zero;

    [Header("Hold & Release")]
    [Tooltip("How long (seconds) to hold the rope at the displaced position before releasing. " +
             "During this time the fluid settles inside the bucket so it doesn't scatter on release.")]
    public float holdDuration = 2.0f;

    [Header("References")]
    public FluidSim fluidSimulation;
    public LineRenderer lineRenderer;

    private Rope rope;

    // ── Hold & Release state ──
    // The rope starts in "held" state: all particles are frozen at the displaced
    // pose so the fluid can settle inside the bucket. After holdDuration elapses,
    // the rope is released and swings naturally under gravity.
    bool isHeld = true;
    float holdTimer;

    // Cached displaced bucket position (computed once in Start)
    Vector3 displacedBucketPosition;

    void Start()
    {
        // حساب الوزن الأولي للسطل مع السائل قبل بدء المحاكاة
        float initialFluidMass = 0f;
        if (fluidSimulation != null && fluidSimulation.spawner != null)
        {
            initialFluidMass = fluidSimulation.spawner.exactParticleCount * fluidParticleMass;
        }
        else if (fluidSimulation != null)
        {
            initialFluidMass = 5000 * fluidParticleMass; // قيمة افتراضية إذا لم يتم جلب الجزيئات بعد
        }

        float startingBucketMass = emptyBucketMass + initialFluidMass;

        // Initialize the rope physics starting from this object's position (fixed point in air)
        rope = new Rope(transform.position, ropeLength, segmentCount, ropeMaterial, bendingStiffness, startingBucketMass, ropeTotalMass, ropeDamping);

        // ---------------------------------------------------------------
        // Compute the displaced bucket position and pose the ENTIRE rope
        // along a taut straight line from anchor to that point.
        // This ensures all constraints are already satisfied — no first-frame
        // snap, no violent correction, no fluid scatter.
        // ---------------------------------------------------------------
        Vector3 anchor = transform.position;
        
        // The bucket hangs at anchor + down*ropeLength in equilibrium.
        // pullOffset displaces it sideways. We compute the actual position
        // along the rope's arc (preserving ropeLength distance from anchor).
        Vector3 equilibrium = anchor + Vector3.down * ropeLength;
        Vector3 target = equilibrium + pullOffset;
        
        // Preserve the rope's actual length (the bucket stays on the arc)
        Vector3 dirFromAnchor = (target - anchor).normalized;
        displacedBucketPosition = anchor + dirFromAnchor * ropeLength;
        
        // Position ALL particles along the straight line from anchor to displaced bucket.
        // Every particle is at rest (zero velocity), constraints satisfied.
        rope.SetDisplacedPose(anchor, displacedBucketPosition);

        // Auto-scale the bucket (fluidSimulation) to make it smaller
        if (fluidSimulation != null)
        {
            // Move the bucket to the displaced position immediately so fluid spawns inside it
            fluidSimulation.transform.position = displacedBucketPosition;
            
            // تصغير حجم السطل الخارجي (0.85 هو حجم ممتاز، ليس صغيراً جداً وليس كبيراً)
            fluidSimulation.transform.localScale = new Vector3(2f, 5f, 2f);
            
            // تكبير الفتحة قليلاً (0.2) لتجنب الضغط العالي جداً الذي يسبب تناثر الجزيئات (الانفجار)
            fluidSimulation.holeRadius = 0.08f;
            
            // وضع قيمة لزوجة صغيرة جداً (0.05) لكي يتماسك السائل بلطف دون أن تسبب القيمة العالية عدم استقرار فيزيائي
            fluidSimulation.viscosityStrength = 0.05f;
            
            // جعل السطل أبيض تماماً بدون إضاءة
            MeshRenderer bucketRenderer = fluidSimulation.GetComponentInChildren<MeshRenderer>();
            if (bucketRenderer != null)
            {
                bucketRenderer.material = new Material(Shader.Find("Unlit/Color"));
                bucketRenderer.material.color = Color.white;
            }

            // ضبط مساحة توليد السائل لكي تناسب السطل وتكون كتلة واحدة
            var spawner = fluidSimulation.GetComponent<Seb.Fluid.Simulation.Spawner3D>();
            if (spawner != null)
            {
                // استبدال المربعات المتعددة بمكعب واحد فقط متمركز داخل السطل
                spawner.spawnRegions = new Seb.Fluid.Simulation.Spawner3D.SpawnRegion[]
                {
                    new Seb.Fluid.Simulation.Spawner3D.SpawnRegion
                    {
                        centre = displacedBucketPosition, // Spawn fluid at the displaced bucket position
                        size = 1.6f, // حجم مناسب لكي لا يخرج عن حواف السطل (نصف القطر 1)
                        debugDisplayCol = Color.cyan
                    }
                };
                
                spawner.useExactCount = true;
                spawner.exactParticleCount = 5000;
            }
        }

        // Initialize hold state
        isHeld = true;
        holdTimer = 0f;

        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        if (lineRenderer != null)
        {
            lineRenderer.startColor = ropeColor;
            lineRenderer.endColor = ropeColor;
            lineRenderer.startWidth = ropeWidth;
            lineRenderer.endWidth = ropeWidth;

            if (lineRenderer.sharedMaterial == null)
            {
                lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            }
            
            // Force the material color to match our chosen ropeColor, 
            // otherwise the default black material from scene setup will override it.
            lineRenderer.material.color = ropeColor;
        }

        SetupCamera();
    }

    void Update()
    {
        if (rope == null)
            return;

        // ─── Hold Phase ───────────────────────────────────────────────
        // During the hold phase the rope is frozen at the displaced position.
        // The fluid simulation runs on the GPU so the fluid settles naturally
        // inside the stationary bucket. No rope physics runs.
        if (isHeld)
        {
            holdTimer += Time.deltaTime;

            // Keep all particles frozen at the displaced pose (zero velocity)
            rope.FreezeAllParticles();

            // Keep the bucket at the displaced position
            if (fluidSimulation != null)
            {
                fluidSimulation.transform.position = displacedBucketPosition;
            }

            // Draw the rope even during hold
            DrawRope();

            // Release when hold duration expires
            if (holdTimer >= holdDuration)
            {
                Release();
            }

            return; // Skip physics this frame
        }

        // ─── Swing Phase (after release) ──────────────────────────────
        // محاكاة نقصان وزن الدلو تدريجياً بدقة متناهية بناءً على عدد الجزيئات الفعلي المتبقي!
        if (fluidSimulation != null)
        {
            VerletParticle endBucket = rope.Particles[rope.Particles.Count - 1];
            
            // الوزن الكلي = السطل الفارغ + (عدد الجزيئات الفعلي × كتلة الجزيء الواحد)
            float currentFluidMass = fluidSimulation.ActiveParticleCount * fluidParticleMass;
            endBucket.Mass = emptyBucketMass + currentFluidMass;
        }

        // Anchor pinning is now handled inside Rope.Simulate's sub-step loop.
        // We just pass the anchor position so the rope knows where to pin.
        // Clamping deltaTime prevents spike frames from destabilizing.
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        rope.Simulate(dt, Physics.gravity, transform.position);

        // Draw the rope
        DrawRope();

        // Follow the fluid container (bucket) to the rope's end particle
        if (fluidSimulation != null)
        {
            VerletParticle endBucket = rope.Particles[rope.Particles.Count - 1];
            fluidSimulation.transform.position = endBucket.Position;

            if (applyOverheadCamera && followBucket && Camera.main != null)
            {
                Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, endBucket.Position + cameraOffset, Time.deltaTime * 5f);
                // Look midway between bucket and canvas
                Camera.main.transform.LookAt(endBucket.Position + Vector3.down * 7.5f);
            }
        }
    }

    /// <summary>
    /// Release the rope from the held position. Applies the tangential push
    /// (if any) to create circular/spiral motion, then lets physics take over.
    /// Called automatically after holdDuration, but can also be called manually.
    /// </summary>
    void Release()
    {
        isHeld = false;

        // Apply tangential push for circular/spiral motion (optional).
        // In Verlet: Velocity = Position - PreviousPosition
        // So: PreviousPosition = Position - Velocity
        if (pushTangential.sqrMagnitude > 0f)
        {
            VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];
            bucket.PreviousPosition = bucket.Position - pushTangential;
        }
    }

    /// <summary>
    /// Draw the rope using the LineRenderer.
    /// Extracted into its own method to avoid duplication between hold and swing phases.
    /// </summary>
    void DrawRope()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = rope.Particles.Count;
            for (int i = 0; i < rope.Particles.Count; i++)
            {
                lineRenderer.SetPosition(i, rope.Particles[i].Position);
            }
        }
    }

    private void SetupCamera()
    {
        if (!applyOverheadCamera || Camera.main == null) return;

        Vector3 pivotPos = transform.position;
        Vector3 bucketRestPos = pivotPos + Vector3.down * ropeLength;

        Camera.main.transform.position = bucketRestPos + cameraOffset;
        // Look exactly at the halfway point between the bucket and the canvas (assuming canvas is ~15 units below)
        Camera.main.transform.LookAt(bucketRestPos + Vector3.down * 7.5f);
    }
}
