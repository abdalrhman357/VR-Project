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

    public Rope(Vector3 startPosition, float ropeLength, int segmentCount, RopeMaterial material, float bendingStiffness = 0.2f)
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

        // 1. توليد الجزيئات
        for(int i = 0; i <= segmentCount; i++)
        {
            Vector3 position = startPosition + (Vector3.down * segmentLength * i);
            bool pinned = (i == 0);
            float mass = (i == segmentCount) ? 10f : 1f; 
            Particles.Add(new VerletParticle(position, pinned, mass));
        }

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
    }

    public void Simulate(float deltaTime, Vector3 gravity)
    {
        foreach(var particle in Particles)
        {
            particle.UpdateParticle(deltaTime, gravity);
        }

        for(int iteration = 0; iteration < 13; iteration++)
        {
      
            foreach(var constraint in Constraints)
            {
                constraint.Solve();
            }

       
            foreach(var bending in BendingConstraints)
            {
                bending.Solve();
            }
        }
    }
}
