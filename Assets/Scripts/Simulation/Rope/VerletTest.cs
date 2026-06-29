using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class VerletTest : MonoBehaviour
{
    private VerletParticle particle;

    void Start()
    {
        particle = new VerletParticle(
            new Vector3(0, 5, 0)
        );
    }

    void Update()
    {
        particle.UpdateParticle(
            Time.deltaTime,
            Physics.gravity
        );

        transform.position = particle.Position;
    }
    
}