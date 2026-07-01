using UnityEngine;
using Seb.Fluid.Simulation;
using SwingingPaint.Core;
using SwingingPaint.Rope;

namespace SwingingPaint.Bucket
{
    /// <summary>
    /// Couples the Verlet pendulum to the GPU fluid. Each step it hangs the fluid simulation volume
    /// (which is also the bucket interior that <see cref="Seb.Fluid.Rendering.BucketRenderer"/> draws
    /// around) from the rope tip: positioned half a bucket-height below the attach point and kept
    /// UPRIGHT (it only translates, never tilts). Staying upright matches the fluid's axis-aligned
    /// collision volume, so the outlet always points straight down and its drain point traces a clean
    /// Lissajous path as the pendulum swings. It also sizes the bucket from config, sets the outlet
    /// hole, and feeds the remaining-paint mass back to the rope so the pendulum period changes as the
    /// bucket drains (Part 5).
    ///
    /// No Rigidbody — the bucket is a kinematic body fully determined by the rope tip position.
    /// </summary>
    public sealed class BucketSimulator : MonoBehaviour
    {
        public RopeSimulator rope;
        public FluidSim fluidSim;

        [SerializeField] SimulationConfig _config;
        public SimulationConfig Config { get => _config; set => _config = value; }

        [Tooltip("Maps config hole radius (m) into the fluid sim's local hole units. Tuned with the compute hole.")]
        public float holeRadiusSimScale = 1f;

        [Tooltip("Bucket height as a multiple of its diameter.")]
        public float heightOverDiameter = 1.3f;

        /// <summary>0..1 paint remaining; updated by FluidManager from live particle counts. Drives payload mass.</summary>
        public float RemainingPaintFraction { get; set; } = 1f;

        public Vector3 BucketCentre { get; private set; }
        public Quaternion BucketRotation { get; private set; } = Quaternion.identity;

        public void Step(float dt)
        {
            if (fluidSim == null || rope == null || _config == null) return;

            var b = _config.bucket;
            float diameter = Mathf.Max(0.02f, b.radius * 2f);
            float height = diameter * Mathf.Max(0.2f, heightOverDiameter);

            // Size the sim volume (FluidSim reads transform.localScale as its bounds every frame).
            fluidSim.transform.localScale = new Vector3(diameter, height, diameter);

            // Bucket stays UPRIGHT and only translates with the rope tip. This matches the fluid's
            // axis-aligned collision volume, so the outlet always points straight down and its drain
            // point traces a clean Lissajous path across the canvas (the desired pendulum-paint look).
            BucketRotation = Quaternion.identity;
            Vector3 tip = rope.TipPosition;
            BucketCentre = tip - Vector3.up * (height * 0.5f);
            fluidSim.transform.SetPositionAndRotation(BucketCentre, Quaternion.identity);

            // Outlet hole from config.
            fluidSim.holeRadius = Mathf.Max(0f, b.holeRadius * holeRadiusSimScale);
            fluidSim.holeOffset = b.holeOffset;

            // Mass feedback: empty shell + remaining paint → rope payload (changes the pendulum period).
            float paintMass = PaintMassFull() * Mathf.Clamp01(RemainingPaintFraction);
            rope.Rope?.SetPayloadMass(b.emptyMass + paintMass);
        }

        /// <summary>
        /// Place the fluid volume at the bucket's INITIAL (release) pose and set the spawn region to fill
        /// its lower part. Shared by the scene builder and by Reset so the bucket always refills correctly
        /// even after the user changes bucket/rope settings.
        /// </summary>
        public static void ConfigureInitialFluidPose(SimulationConfig cfg, FluidSim fluid, Spawner3D spawner,
                                                     float heightOverDiameter)
        {
            Vector3 anchor = cfg.rope.anchorPosition;
            Quaternion rot = Quaternion.AngleAxis(cfg.rope.initialAngleX, Vector3.forward)
                           * Quaternion.AngleAxis(cfg.rope.initialAngleZ, Vector3.right);
            Vector3 ropeDown = (rot * Vector3.down).normalized;
            Vector3 tip = anchor + ropeDown * cfg.rope.length;
            float diameter = Mathf.Max(0.05f, cfg.bucket.radius * 2f);
            float height = diameter * Mathf.Max(0.2f, heightOverDiameter);
            Vector3 centre = tip - Vector3.up * (height * 0.5f);   // upright bucket

            fluid.transform.SetPositionAndRotation(centre, Quaternion.identity);
            fluid.transform.localScale = new Vector3(diameter, height, diameter);

            if (spawner != null)
            {
                // A cube of paint resting on the bucket floor (fits inside the cylinder radius).
                // fillAmount scales the cube so more fill = more paint, while it always sits on the bottom.
                float fill = Mathf.Clamp01(cfg.paint.fillAmount);
                float spawnSize = diameter * Mathf.Lerp(0.45f, 0.7f, fill);
                float bottomY = centre.y - height * 0.5f;
                Vector3 spawnCentre = new Vector3(centre.x, bottomY + spawnSize * 0.5f + height * 0.03f, centre.z);
                spawner.spawnRegions = new[]
                {
                    new Spawner3D.SpawnRegion
                    {
                        centre = spawnCentre,
                        size = spawnSize,
                        debugDisplayCol = new Color(0.2f, 0.5f, 1f, 0.5f)
                    }
                };
            }
        }

        /// <summary>Paint mass (kg) when the bucket is full at the configured fill amount and density.</summary>
        public float PaintMassFull()
        {
            var b = _config.bucket;
            var p = _config.paint;
            float diameter = b.radius * 2f;
            float height = diameter * heightOverDiameter;
            float interiorVolume = Mathf.PI * b.radius * b.radius * height; // m^3 (cylinder approx)
            return interiorVolume * Mathf.Clamp01(p.fillAmount) * (p.density / 1000f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.5f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(BucketCentre, 0.05f);
        }
    }
}
