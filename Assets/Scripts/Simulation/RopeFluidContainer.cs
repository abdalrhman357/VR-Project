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
    [Tooltip("الكتلة الفيزيائية للجزيء الواحد (تعتمد على الكثافة). سيُحسب وزن الطلاء بضربها في عدد الجزيئات.")]
    public float fluidParticleMass = 0.012f;

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
    public Vector3 pullOffset = new Vector3(2.5f, 0f, 0f); 

    [Tooltip("Push the bucket sideways as it's released to create a circular/spiral motion (e.g. Z axis).")]
    public Vector3 pushTangential = new Vector3(0f, 0f, -0.05f);

    [Header("References")]
    public FluidSim fluidSimulation;
    public LineRenderer lineRenderer;

    private Rope rope;

    void Start()
    {
        // حساب الوزن الأولي للسطل مع السائل قبل بدء المحاكاة
        float initialFluidMass = 0f;
        if (fluidSimulation != null && fluidSimulation.spawner != null)
        {
            initialFluidMass = fluidSimulation.spawner.spawnPoints.Length * fluidParticleMass;
        }
        else if (fluidSimulation != null)
        {
            initialFluidMass = 5000 * fluidParticleMass; // قيمة افتراضية إذا لم يتم جلب الجزيئات بعد
        }

        float startingBucketMass = emptyBucketMass + initialFluidMass;

        // Initialize the rope physics starting from this object's position (fixed point in air)
        rope = new Rope(transform.position, ropeLength, segmentCount, ropeMaterial, bendingStiffness, startingBucketMass);

        // Auto-scale the bucket (fluidSimulation) to make it smaller
        if (fluidSimulation != null)
        {
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
                        centre = fluidSimulation.transform.position,
                        size = 1.6f, // حجم مناسب لكي لا يخرج عن حواف السطل (نصف القطر 1)
                        debugDisplayCol = Color.cyan
                    }
                };
                
                spawner.useExactCount = true;
                spawner.exactParticleCount = 5000;
            }
        }
        
        // ---------------------------------------------------------------
        // Apply an initial push at simulation start by offsetting the
        // bucket's previous position. Verlet integration will convert
        // this displacement into velocity automatically on the first step.
        // ---------------------------------------------------------------
        VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];
        
        // 1. Pull the bucket to the side (Displacement)
        bucket.Position = bucket.Position + pullOffset;
        
        // 2. Push it tangentially to create a circular/spiral motion (Velocity)
        // In Verlet: Velocity = Position - PreviousPosition
        // So PreviousPosition = Position - Velocity
        bucket.PreviousPosition = bucket.Position - pushTangential;
        // PreviousPosition is already set to the original position by the Rope constructor.

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
        // No mouse interaction — simulation runs on its own.

        if (rope != null)
        {
            // محاكاة نقصان وزن الدلو تدريجياً بدقة متناهية بناءً على عدد الجزيئات الفعلي المتبقي!
            if (fluidSimulation != null)
            {
                VerletParticle endBucket = rope.Particles[rope.Particles.Count - 1];
                
                // الوزن الكلي = السطل الفارغ + (عدد الجزيئات الفعلي × كتلة الجزيء الواحد)
                float currentFluidMass = fluidSimulation.ActiveParticleCount * fluidParticleMass;
                endBucket.Mass = emptyBucketMass + currentFluidMass;
            }

            // Pin the anchor (first particle) to this transform every frame so it never drifts.
            rope.Particles[0].Position = transform.position;
            rope.Particles[0].PreviousPosition = transform.position;

            rope.Simulate(Time.deltaTime, Physics.gravity);

            // Draw the rope
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = rope.Particles.Count;
                for (int i = 0; i < rope.Particles.Count; i++)
                {
                    lineRenderer.SetPosition(i, rope.Particles[i].Position);
                }
            }

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
