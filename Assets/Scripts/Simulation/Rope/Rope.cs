using System.Collections.Generic;
using UnityEngine;

public enum RopeMaterial
{
    Rigid, Cotton, Nylon, Rubber
}

public class Rope
{
    public List<VerletParticle> Particles = new List<VerletParticle>();
    public List<DistanceConstraint> Constraints = new List<DistanceConstraint>();
    
    // القائمة الجديدة لقيود الانثناء
    public List<BendingConstraint> BendingConstraints = new List<BendingConstraint>();

    // Fixed sub-step size for deterministic, stable simulation.
    // 1/240s is the industry standard for rope/cloth physics — small enough for
    // stability yet large enough to stay within CPU budget.
    const float FixedSubStep = 1f / 240f;

    // Maximum accumulated time to prevent spiral-of-death on big frame spikes.
    const float MaxAccumulation = 1f / 30f;

    // Number of constraint-solver iterations per substep.
    // 20 gives a good balance between stiffness and performance for 15-segment ropes.
    const int SolverIterations = 20;

    // Accumulated time from previous frames (left-over fractional substep).
    float timeAccumulator;

    public Rope(Vector3 startPosition, float ropeLength, int segmentCount, RopeMaterial material, float bendingStiffness = 0.2f, float bucketMass = 10f, float ropeTotalMass = 0.5f, float damping = 0.002f)
    {
        float stiffness = 1.0f;
        float maxStretch = 1.0f;

        switch (material)
        {
            case RopeMaterial.Rigid:  stiffness = 1.00f; maxStretch = 1.00f; break;
            case RopeMaterial.Cotton: stiffness = 0.85f; maxStretch = 1.05f; break;
            case RopeMaterial.Nylon:  stiffness = 0.65f; maxStretch = 1.15f; break;
            case RopeMaterial.Rubber: stiffness = 0.25f; maxStretch = 1.50f; break;
        }

        float segmentLength = ropeLength / segmentCount;
        
        // حساب كتلة الجزء الواحد من الحبل (توزيع الوزن الكلي للحبل بالتساوي على الأجزاء)
        float segmentMass = ropeTotalMass / segmentCount;

        // 1. توليد الجزيئات
        for(int i = 0; i <= segmentCount; i++)
        {
            Vector3 position = startPosition + (Vector3.down * segmentLength * i);
            bool pinned = (i == 0);
            
            // الجزيء الأخير هو الدلو فيأخذ وزنه بالكامل، أما بقية الأجزاء فهي تمثل نسيج الحبل فتأخذ وزن الجزء الصغير
            float mass = (i == segmentCount) ? bucketMass : segmentMass; 
            Particles.Add(new VerletParticle(position, pinned, mass));
        }

        // Apply the damping coefficient to every particle so the swing duration
        // is controlled by a single value exposed in the Inspector.
        SetDamping(damping);

        // 2. توليد قيود المسافة (ربط المتجاورين: 0 مع 1، 1 مع 2...)
        for(int i = 0; i < Particles.Count - 1; i++)
        {
            Constraints.Add(new DistanceConstraint(Particles[i], Particles[i+1], stiffness, maxStretch));
        }

        // 3. توليد قيود الانثناء (تخطي جزيء: 0 مع 2، 1 مع 3...)
        // نستخدم Count - 2 لأننا نحتاج لجزيئين للأمام
        for(int i = 0; i < Particles.Count - 2; i++)
        {
            BendingConstraints.Add(new BendingConstraint(Particles[i], Particles[i+2], bendingStiffness));
        }

        timeAccumulator = 0f;
    }

    /// <summary>
    /// Set the damping coefficient for all particles.
    /// Can be called at runtime to change how quickly the pendulum loses energy.
    /// </summary>
    public void SetDamping(float damping)
    {
        foreach (var p in Particles)
        {
            p.DampingCoefficient = damping;
        }
    }

    /// <summary>
    /// Run the physics simulation for this frame.
    /// Uses fixed-size sub-stepping (1/240s) for deterministic, stable results
    /// regardless of the application's frame rate.
    /// </summary>
    /// <param name="deltaTime">The frame's Time.deltaTime (variable).</param>
    /// <param name="gravity">World gravity vector.</param>
    /// <param name="anchorPosition">World position to pin the first particle to (the fixed point).</param>
    public void Simulate(float deltaTime, Vector3 gravity, Vector3 anchorPosition)
    {
        // Clamp to prevent spiral-of-death when the game hitches
        timeAccumulator += Mathf.Min(deltaTime, MaxAccumulation);

        while (timeAccumulator >= FixedSubStep)
        {
            timeAccumulator -= FixedSubStep;
            SubStep(FixedSubStep, gravity, anchorPosition);
        }
    }

    /// <summary>
    /// One fixed-size physics sub-step: integrate → solve constraints → re-pin anchor.
    /// </summary>
    void SubStep(float dt, Vector3 gravity, Vector3 anchorPosition)
    {
        // 1. Pin anchor BEFORE integration so it never gets a velocity
        PinAnchor(anchorPosition);

        // 2. Verlet integration for all free particles
        foreach (var particle in Particles)
        {
            particle.UpdateParticle(dt, gravity);
        }

        // 3. Iterative constraint solving
        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            // Pin anchor at the start of each solver iteration to prevent drift
            PinAnchor(anchorPosition);

            foreach (var constraint in Constraints)
            {
                constraint.Solve();
            }

            foreach (var bending in BendingConstraints)
            {
                bending.Solve();
            }
        }

        // 4. Final anchor pin to guarantee zero drift
        PinAnchor(anchorPosition);
    }

    /// <summary>
    /// Force the first particle (anchor/pivot) to the given position with zero velocity.
    /// Called multiple times per substep to ensure constraints never pull it away.
    /// </summary>
    void PinAnchor(Vector3 anchorPosition)
    {
        Particles[0].Position = anchorPosition;
        Particles[0].PreviousPosition = anchorPosition;
    }

    /// <summary>
    /// Position the entire rope along a taut straight line from <paramref name="anchorPosition"/>
    /// toward <paramref name="bucketTarget"/>, distributing particles evenly along the
    /// rope's full length. Every particle is placed at rest (PreviousPosition = Position)
    /// so no Verlet velocity exists — the rope is perfectly still.
    ///
    /// Call this ONCE before the hold phase so the rope hangs in the displaced pose
    /// with all constraints already satisfied. When released, gravity naturally pulls
    /// the bucket into a pendulum swing with zero jerk.
    /// </summary>
    /// <param name="anchorPosition">World position of the fixed pivot point.</param>
    /// <param name="bucketTarget">World position where the bucket should be held.</param>
    public void SetDisplacedPose(Vector3 anchorPosition, Vector3 bucketTarget)
    {
        Vector3 direction = (bucketTarget - anchorPosition).normalized;
        int segmentCount = Particles.Count - 1;
        float segmentLength = Vector3.Distance(anchorPosition, bucketTarget) / segmentCount;

        for (int i = 0; i <= segmentCount; i++)
        {
            Vector3 pos = anchorPosition + direction * (segmentLength * i);
            Particles[i].Position = pos;
            Particles[i].PreviousPosition = pos; // Zero velocity
        }
    }

    /// <summary>
    /// Freeze all particles in place (set PreviousPosition = Position to zero out velocity).
    /// Used during the hold phase so the rope doesn't accumulate motion.
    /// </summary>
    public void FreezeAllParticles()
    {
        foreach (var p in Particles)
        {
            p.PreviousPosition = p.Position;
        }
    }

    // Backward-compatible overload for RopeTest or other callers that don't pass an anchor.
    public void Simulate(float deltaTime, Vector3 gravity)
    {
        Simulate(deltaTime, gravity, Particles[0].Position);
    }
}
