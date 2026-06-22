using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConstraintTest : MonoBehaviour
{
    public Transform PointA;
    public Transform PointB;

    VerletParticle particleA;
    VerletParticle particleB;

    DistanceConstraint constraint;

    void Start()
    {
        particleA =
            new VerletParticle(
                PointA.position,
                true);

        particleB =
            new VerletParticle(
                PointB.position);

        constraint =
            new DistanceConstraint(
                particleA,
                particleB);
    }

    void Update()
    {
        particleB.UpdateParticle(
            Time.deltaTime,
            Physics.gravity);

        constraint.Solve();

        PointA.position =
            particleA.Position;

        PointB.position =
            particleB.Position;
    }
}
