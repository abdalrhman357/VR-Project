using UnityEngine;
using Seb.Fluid.Simulation;

public class RopeFluidContainer : MonoBehaviour
{
    [Header("Rope Settings")]
    public float ropeLength = 10f;
    public int segmentCount = 15;
    public RopeMaterial ropeMaterial = RopeMaterial.Rubber;
    public float bendingStiffness = 0.2f;

    public Color ropeColor = Color.white;
    public float ropeWidth = 0.05f;

    [Header("Camera Setup")]
    public bool applyOverheadCamera = true;
    public Vector3 cameraPos = new Vector3(6f, 10f, 6f);

    [Header("Interaction")]
    public float initialPushX = 0.05f;
    public float initialPushZ = 0.02f;

    [Header("References")]
    public FluidSim fluidSimulation;
    public LineRenderer lineRenderer;

    private Rope rope;

    void Start()
    {
        // Initialize the rope physics starting from this object's position (fixed point in air)
        rope = new Rope(transform.position, ropeLength, segmentCount, ropeMaterial, bendingStiffness);

        // Give an initial push to the bucket (last particle)
        VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];
        Vector3 initialPush = new Vector3(initialPushX, 0f, initialPushZ);
        bucket.PreviousPosition = bucket.Position - initialPush;

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
        }

        if (applyOverheadCamera && Camera.main != null)
        {
            Camera.main.transform.position = cameraPos;
            Camera.main.transform.LookAt(transform.position + Vector3.down * (ropeLength * 0.5f));
        }
    }

    void Update()
    {
        HandleMouseInteraction();

        // Simulate rope physics
        if (rope != null)
        {
            // Pin the first particle to this transform every frame.
            // Without this, the rope's anchor drifts under gravity over time.
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

            // Attach the fluid container to the end of the rope (bucket)
            if (fluidSimulation != null)
            {
                VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];
                fluidSimulation.transform.position = bucket.Position;
            }
        }
    }

    private void HandleMouseInteraction()
    {
        if (Input.GetMouseButton(0) && rope != null)
        {
            VerletParticle bucket = rope.Particles[rope.Particles.Count - 1];
            // Convert the bucket's CURRENT world position to screen space.
            // The .z of that is the correct depth value ScreenToWorldPoint expects.
            Vector3 bucketScreenPos = Camera.main.WorldToScreenPoint(bucket.Position);

            Vector3 mouseScreenPosition = Input.mousePosition;
            mouseScreenPosition.z = bucketScreenPos.z; // Use bucket's depth, not pivot's
            
            Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(mouseScreenPosition);

            // Force bucket to mouse position
            bucket.Position = mouseWorldPosition;

            // Prevent velocity accumulation while holding
            bucket.PreviousPosition = mouseWorldPosition;
        }
    }
}
