using System.Collections.Generic;
using UnityEngine;
using SwingingPaint.Core;

namespace SwingingPaint.Rope
{
    /// <summary>
    /// A Verlet pendulum rope: anchor pinned at the top, bucket hung from the bottom particle.
    /// Owns its particles and constraints; iteration count and stiffness come from config, not
    /// hard-coded constants (the original used a magic "13"). The bottom particle's mass is the
    /// bucket+paint payload, so as paint drains the pendulum period changes (Part 5).
    /// </summary>
    public sealed class Rope
    {
        public readonly List<VerletParticle> Particles = new();
        public readonly List<DistanceConstraint> Distance = new();
        public readonly List<BendingConstraint> Bending = new();

        public VerletParticle Anchor => Particles[0];
        public VerletParticle Tip => Particles[Particles.Count - 1];

        public int ConstraintIterations = 16;

        public Rope(Vector3 anchorPosition, float length, int segmentCount,
                    SimulationConfig.RopeMaterialKind material, float bendingStiffness,
                    float payloadMass)
        {
            (float stiffness, float maxStretch) = MaterialParams(material);
            int segments = Mathf.Max(2, segmentCount);
            float segLen = length / segments;

            // Particles hang straight down; anchor pinned, tip carries the payload mass.
            for (int i = 0; i <= segments; i++)
            {
                Vector3 pos = anchorPosition + Vector3.down * (segLen * i);
                bool pinned = i == 0;
                float mass = (i == segments) ? Mathf.Max(payloadMass, 0.01f) : 1f;
                Particles.Add(new VerletParticle(pos, pinned, mass));
            }

            for (int i = 0; i < Particles.Count - 1; i++)
                Distance.Add(new DistanceConstraint(Particles[i], Particles[i + 1], stiffness, maxStretch));

            for (int i = 0; i < Particles.Count - 2; i++)
                Bending.Add(new BendingConstraint(Particles[i], Particles[i + 2], bendingStiffness));
        }

        /// <summary>Update the hung payload mass (bucket shell + remaining paint).</summary>
        public void SetPayloadMass(float mass) => Tip.Mass = Mathf.Max(mass, 0.01f);

        public void Step(float dt, Vector3 acceleration, float linearDamping)
        {
            foreach (var p in Particles)
                p.Integrate(dt, acceleration, linearDamping);

            // Gauss-Seidel constraint relaxation: more iterations → stiffer, less stretch.
            int iters = Mathf.Max(1, ConstraintIterations);
            for (int it = 0; it < iters; it++)
            {
                foreach (var c in Distance) c.Solve();
                foreach (var b in Bending) b.Solve();
            }
        }

        static (float stiffness, float maxStretch) MaterialParams(SimulationConfig.RopeMaterialKind m) => m switch
        {
            SimulationConfig.RopeMaterialKind.Rigid  => (1.00f, 1.00f),
            SimulationConfig.RopeMaterialKind.Cotton => (0.85f, 1.05f),
            SimulationConfig.RopeMaterialKind.Nylon  => (0.65f, 1.15f),
            SimulationConfig.RopeMaterialKind.Rubber => (0.25f, 1.50f),
            _ => (0.85f, 1.05f)
        };
    }
}
