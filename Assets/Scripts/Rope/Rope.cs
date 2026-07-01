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
    public List<BendingConstraint> BendingConstraints = new List<BendingConstraint>();

    public int SolverIterations = 15;

    public Rope(Vector3 startPosition, float ropeLength, int segmentCount, RopeMaterial material, float endpointMass = 10f, float bendingStiffness = 0.2f)
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

        // لتجنب بطء حركة البندول (Sluggishness) الناتج عن حل القيود بين كتلة ضخمة (الدلو)
        // وكتل خفيفة جداً (الحبل)، نرفع كتلة جزيئات الحبل لتكون متناسبة مع الدلو.
        // نسبة 1:4 هي نسبة مثالية تضمن سرعة استجابة وتأرجح واقعي وسريع.
        float ropeNodeMass = Mathf.Max(1f, endpointMass / 4f);

        for(int i = 0; i <= segmentCount; i++)
        {
            Vector3 position = startPosition + (Vector3.down * segmentLength * i);
            bool pinned = (i == 0);
            float mass = (i == segmentCount) ? endpointMass : ropeNodeMass; 
            Particles.Add(new VerletParticle(position, pinned, mass));
        }

        for(int i = 0; i < Particles.Count - 1; i++)
        {
            Constraints.Add(new DistanceConstraint(Particles[i], Particles[i+1], stiffness, maxStretch));
        }

        for(int i = 0; i < Particles.Count - 2; i++)
        {
            BendingConstraints.Add(new BendingConstraint(Particles[i], Particles[i+2], bendingStiffness));
        }
    }

    public void SetEndpointMass(float mass)
    {
        if (Particles.Count > 0)
            Particles[Particles.Count - 1].Mass = mass;
    }

    public float GetEndpointMass()
    {
        if (Particles.Count > 0)
            return Particles[Particles.Count - 1].Mass;
        return 0f;
    }

    /// <summary>
    /// حساب السرعة القصوى بين كل الجزيئات (مربع السرعة لتجنب sqrt)
    /// </summary>
    public float GetMaxVelocitySqr()
    {
        float maxSqr = 0f;
        foreach (var p in Particles)
        {
            if (p.IsPinned) continue;
            float sqr = (p.Position - p.PreviousPosition).sqrMagnitude;
            if (sqr > maxSqr) maxSqr = sqr;
        }
        return maxSqr;
    }

    public void Simulate(float deltaTime, Vector3 gravity)
    {
        // ═══ خطوة 1: تطبيق القوى (الجاذبية + السرعة) ═══
        foreach(var particle in Particles)
        {
            particle.UpdateParticle(deltaTime, gravity);
        }

        // ═══ خطوة 2: حل القيود بأسلوب Gauss-Seidel المتناوب ═══
        // ─────────────────────────────────────────────────────────
        // المشكلة: الحل باتجاه واحد (0→N) يجعل التقارب ممتازاً عند القمة
        //          لكن ضعيفاً عند القاعدة (حيث الدلو الثقيل).
        //
        // الحل: نتناوب الاتجاه كل تكرار:
        //   - التكرار الزوجي: من الأعلى للأسفل (0→N)
        //   - التكرار الفردي: من الأسفل للأعلى (N→0)
        //
        // هذا يحسّن التقارب بشكل كبير عند كلا الطرفين ويقلل الاهتزاز.
        // ─────────────────────────────────────────────────────────
        for(int iteration = 0; iteration < SolverIterations; iteration++)
        {
            if (iteration % 2 == 0)
            {
                // Forward pass: أعلى → أسفل
                for (int i = 0; i < Constraints.Count; i++)
                    Constraints[i].Solve();
                for (int i = 0; i < BendingConstraints.Count; i++)
                    BendingConstraints[i].Solve();
            }
            else
            {
                // Backward pass: أسفل → أعلى
                for (int i = Constraints.Count - 1; i >= 0; i--)
                    Constraints[i].Solve();
                for (int i = BendingConstraints.Count - 1; i >= 0; i--)
                    BendingConstraints[i].Solve();
            }
        }
    }
}
