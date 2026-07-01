using System;
using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Single source of truth for every runtime-tunable parameter (Part 7 control panel).
    /// Plain [Serializable] class — NOT a ScriptableObject — so the auto scene builder can create
    /// it in code with zero asset wiring, the control panel can mutate it live, and the save/load
    /// system can round-trip it through JsonUtility. Grouped to keep the inspector/UI readable.
    /// </summary>
    [Serializable]
    public class SimulationConfig
    {
        [Serializable]
        public class WorldSettings
        {
            [Tooltip("Downward acceleration (m/s^2). Configurable per Part 5.")]
            public float gravity = 9.81f;
            [Tooltip("Linear air drag for the rope/pendulum (0 = none, 1 = heavy).")]
            [Range(0f, 1f)] public float airResistance = 0.04f;
            [Tooltip("Constant horizontal wind acceleration (m/s^2), world space.")]
            public Vector3 wind = Vector3.zero;
            [Tooltip("Global time multiplier. 1 = real time, <1 = slow-mo.")]
            [Range(0.05f, 2f)] public float simulationSpeed = 1f;
            [Tooltip("Fixed physics sub-step (s). Smaller = more stable, more cost.")]
            public float fixedStep = 1f / 120f;
            [Tooltip("Safety cap on sub-steps per frame so a lag spike can't spiral.")]
            public int maxSubStepsPerFrame = 8;
        }

        [Serializable]
        public class RopeSettings
        {
            public Vector3 anchorPosition = new Vector3(0f, 14f, 0f);
            [Range(0.5f, 16f)] public float length = 8.0f;
            [Range(2, 40)] public int segmentCount = 16;
            [Range(1, 30)] public int constraintIterations = 16;
            [Range(0f, 1f)] public float bendingStiffness = 0.25f;
            [Range(0f, 1f)] public float linearDamping = 0.035f;   // low → spiral makes many loops
            public RopeMaterialKind material = RopeMaterialKind.Cotton;
            [Header("Initial pendulum state (Part 5)")]
            [Tooltip("Initial release angle around X (deg), swings left/right.")]
            [Range(-80f, 80f)] public float initialAngleX = 24f;
            [Tooltip("Initial release angle around Z (deg), swings forward/back.")]
            [Range(-80f, 80f)] public float initialAngleZ = 0f;
            [Tooltip("Initial sideways push (m/s) on the bucket. PERPENDICULAR to the release angle = " +
                     "conical pendulum → circle/ellipse → spirals inward as it damps (the painting pattern!).")]
            public Vector2 initialTipVelocity = new Vector2(0f, 2.6f);
        }

        [Serializable]
        public class BucketSettings
        {
            [Range(0.05f, 10f)] public float emptyMass = 2f;      // bucket shell mass (kg)
            [Range(0.2f, 3f)] public float radius = 1.5f;         // inner radius (world units)
            [Range(0f, 1.2f)] public float holeRadius = 0.28f;    // outlet → flowing stream (smaller = slower/thinner)
            public Vector2 holeOffset = Vector2.zero;             // outlet XZ offset from axis
            [Range(0.1f, 1f)] public float heightScale = 0.6f;
        }

        [Serializable]
        public class PaintSettings
        {
            [Range(0f, 1f)] public float fillAmount = 0.85f;      // 0..1 of bucket volume
            [Range(800f, 1600f)] public float density = 1300f;    // acrylic ~1.2-1.4 g/cm^3
            [Range(0f, 30f)] public float viscosity = 12f;        // high → coherent stream
            [Range(0.1f, 30f)] public float flowRate = 8f;        // outlet throughput scalar
            public Color color = new Color(0.10f, 0.25f, 0.85f);  // default blue
            public bool randomColorPerRefill = false;
            [Tooltip("true = continuous source (recycles, never empties, keeps painting); false = drains and empties.")]
            public bool continuousSource = true;
        }

        [Serializable]
        public class CanvasSettings
        {
            public Vector2 size = new Vector2(8f, 8f);            // world units
            public int textureResolution = 1024;
            [Range(0f, 1f)] public float absorptionRate = 0.85f; // 0=glass, 1=canvas
            [Range(0f, 1f)] public float humidity = 0.25f;       // 0=distinct layered rings, 1=colours bleed/merge
            public bool autoTilt = true;
        }

        [Serializable]
        public class QualitySettings
        {
            [Tooltip("Target particle budget for the fluid spawner.")]
            public int particleBudget = 60000;
            [Range(1, 8)] public int solverIterationsPerFrame = 5;   // more = calmer, more settled fluid
            [Tooltip("0=Low,1=Medium,2=High,3=Ultra — drives render + post settings.")]
            [Range(0, 3)] public int renderingQuality = 2;
            [Tooltip("0=Off,1=Balanced,2=Aggressive optimization profile.")]
            [Range(0, 2)] public int optimizationLevel = 1;
        }

        public WorldSettings world = new WorldSettings();
        public RopeSettings rope = new RopeSettings();
        public BucketSettings bucket = new BucketSettings();
        public PaintSettings paint = new PaintSettings();
        public CanvasSettings canvas = new CanvasSettings();
        public QualitySettings quality = new QualitySettings();

        public enum RopeMaterialKind { Rigid, Cotton, Nylon, Rubber }

        /// <summary>Deep copy via JSON — used by Save/Load and "Compare Experiments".</summary>
        public SimulationConfig Clone() =>
            JsonUtility.FromJson<SimulationConfig>(JsonUtility.ToJson(this));
    }
}
