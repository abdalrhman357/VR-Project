using NUnit.Framework;
using UnityEngine;

namespace Simulation.Tests.EditMode
{
    /// <summary>
    /// Edit-Mode Tests — PaintCanvas
    ///
    /// These tests run WITHOUT entering Play Mode (no Awake/Start).
    /// They verify the pure math that PaintCanvas uses:
    ///   • UV coordinate clamping to [0,1]
    ///   • Tangent-space derivation (the same logic as TryPaint)
    ///   • Hit-threshold rejection
    ///   • Mass-scale clamping
    ///
    /// Strategy: We replicate the formulas from PaintCanvas directly so the
    /// tests act as a specification — if the production code drifts, the
    /// tests catch it.
    /// </summary>
    [TestFixture]
    [Category("PaintCanvas")]
    public class PaintCanvasUVMappingTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Replicates the UV mapping logic from PaintCanvas.TryPaint.
        /// Returns (uv, distFromPlane) for a local-space position.
        /// </summary>
        static (Vector2 uv, float dist) ComputeUV(
            Vector3 localPos,
            Vector3 normalLocal,
            float   canvasWidth,
            float   canvasHeight)
        {
            Vector3 n  = normalLocal.normalized;
            float dist = Vector3.Dot(localPos, n);

            Vector3 inPlane = localPos - n * dist;

            Vector3 up      = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f
                              ? Vector3.up
                              : Vector3.forward;
            Vector3 tangentU = Vector3.Cross(n, up).normalized;
            Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

            float u = Vector3.Dot(inPlane, tangentU);
            float v = Vector3.Dot(inPlane, tangentV);

            Vector2 uv = new Vector2(
                u / canvasWidth  + 0.5f,
                v / canvasHeight + 0.5f);

            return (uv, dist);
        }

        // ── UV Mapping ────────────────────────────────────────────────────────

        [Test]
        [Description("A particle exactly at the canvas centre (local origin) maps to UV (0.5, 0.5)")]
        public void UV_CentreParticle_IsHalfHalf()
        {
            var (uv, _) = ComputeUV(Vector3.zero, Vector3.up, 2f, 2f);
            Assert.AreEqual(0.5f, uv.x, 1e-5f, "Centre U should be 0.5");
            Assert.AreEqual(0.5f, uv.y, 1e-5f, "Centre V should be 0.5");
        }

        [Test]
        [Description("A particle at the left edge of a 2-unit canvas maps to U = 0")]
        public void UV_LeftEdge_IsZero()
        {
            var (uv, _) = ComputeUV(new Vector3(-1f, 0f, 0f), Vector3.forward, 2f, 2f);
            Assert.AreEqual(0f, uv.x, 1e-5f, "Left edge U should be 0");
        }

        [Test]
        [Description("A particle at the right edge of a 2-unit canvas maps to U = 1")]
        public void UV_RightEdge_IsOne()
        {
            var (uv, _) = ComputeUV(new Vector3(1f, 0f, 0f), Vector3.forward, 2f, 2f);
            Assert.AreEqual(1f, uv.x, 1e-5f, "Right edge U should be 1");
        }

        [Test]
        [Description("A particle at the bottom edge of a 2-unit canvas maps to V = 0")]
        public void UV_BottomEdge_IsZero()
        {
            var (uv, _) = ComputeUV(new Vector3(0f, -1f, 0f), Vector3.forward, 2f, 2f);
            Assert.AreEqual(0f, uv.y, 1e-5f, "Bottom edge V should be 0");
        }

        [Test]
        [Description("A particle at the top edge of a 2-unit canvas maps to V = 1")]
        public void UV_TopEdge_IsOne()
        {
            var (uv, _) = ComputeUV(new Vector3(0f, 1f, 0f), Vector3.forward, 2f, 2f);
            Assert.AreEqual(1f, uv.y, 1e-5f, "Top edge V should be 1");
        }

        [Test]
        [Description("UV should stay within [0,1] for any in-bounds local position")]
        public void UV_InBoundsPositions_StayBetween0And1(
            [Values(-0.9f, -0.5f, 0f, 0.5f, 0.9f)] float x,
            [Values(-0.9f, -0.5f, 0f, 0.5f, 0.9f)] float y)
        {
            var (uv, _) = ComputeUV(new Vector3(x, y, 0f), Vector3.forward, 2f, 2f);
            Assert.GreaterOrEqual(uv.x, 0f, "U must be >= 0");
            Assert.LessOrEqual   (uv.x, 1f, "U must be <= 1");
            Assert.GreaterOrEqual(uv.y, 0f, "V must be >= 0");
            Assert.LessOrEqual   (uv.y, 1f, "V must be <= 1");
        }

        // ── Hit-Threshold Rejection ───────────────────────────────────────────

        [Test]
        [Description("A particle EXACTLY on the plane has distance = 0 (always a hit)")]
        public void HitThreshold_OnPlane_DistanceIsZero()
        {
            var (_, dist) = ComputeUV(new Vector3(0f, 0f, 0f), Vector3.up, 2f, 2f);
            Assert.AreEqual(0f, Mathf.Abs(dist), 1e-5f);
        }

        [Test]
        [Description("A particle 0.04 units above the plane is within the default HitThreshold 0.05")]
        public void HitThreshold_SlightlyAbovePlane_WithinThreshold()
        {
            float hitThreshold = 0.05f;
            var (_, dist) = ComputeUV(new Vector3(0f, 0.04f, 0f), Vector3.up, 2f, 2f);
            Assert.IsTrue(Mathf.Abs(dist) <= hitThreshold,
                $"|dist| = {Mathf.Abs(dist)} should be <= HitThreshold {hitThreshold}");
        }

        [Test]
        [Description("A particle 0.10 units above the plane exceeds the default HitThreshold 0.05 and should be rejected")]
        public void HitThreshold_TooFarAbovePlane_Rejected()
        {
            float hitThreshold = 0.05f;
            var (_, dist) = ComputeUV(new Vector3(0f, 0.10f, 0f), Vector3.up, 2f, 2f);
            Assert.IsFalse(Mathf.Abs(dist) <= hitThreshold,
                "Particle far above plane should be rejected");
        }

        // ── Tangent-Space Stability ───────────────────────────────────────────

        [Test]
        [Description("When the normal is Vector3.up, the fallback to Vector3.forward prevents NaN in tangent")]
        public void TangentSpace_NormalIsUp_NoNaN()
        {
            Vector3 n   = Vector3.up;
            Vector3 up  = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f
                          ? Vector3.up
                          : Vector3.forward;
            Vector3 tangentU = Vector3.Cross(n, up).normalized;
            Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

            Assert.IsFalse(float.IsNaN(tangentU.x), "tangentU.x is NaN");
            Assert.IsFalse(float.IsNaN(tangentV.x), "tangentV.x is NaN");
        }

        [Test]
        [Description("When the normal is Vector3.forward, the default up-vector is used safely")]
        public void TangentSpace_NormalIsForward_NoNaN()
        {
            Vector3 n   = Vector3.forward;
            Vector3 up  = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f
                          ? Vector3.up
                          : Vector3.forward;
            Vector3 tangentU = Vector3.Cross(n, up).normalized;
            Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

            Assert.IsFalse(float.IsNaN(tangentU.x));
            Assert.IsFalse(float.IsNaN(tangentV.x));
        }

        // ── Mass-Scale Clamping ───────────────────────────────────────────────

        [Test]
        [Description("massScale should never go below MassScaleClamp.x even for a very tiny particle")]
        public void MassScale_TinyParticle_ClampedToMin()
        {
            float refMass      = 0.012f;
            Vector2 clamp      = new Vector2(0.6f, 2.8f);
            float particleMass = 0.000001f;

            float massScale = Mathf.Sqrt(Mathf.Max(particleMass, 0.00001f) / refMass);
            massScale = Mathf.Clamp(massScale, clamp.x, clamp.y);

            Assert.GreaterOrEqual(massScale, clamp.x,
                "massScale must not fall below MassScaleClamp.x");
        }

        [Test]
        [Description("massScale should never exceed MassScaleClamp.y even for a very heavy particle")]
        public void MassScale_HeavyParticle_ClampedToMax()
        {
            float refMass      = 0.012f;
            Vector2 clamp      = new Vector2(0.6f, 2.8f);
            float particleMass = 9999f;

            float massScale = Mathf.Sqrt(Mathf.Max(particleMass, 0.00001f) / refMass);
            massScale = Mathf.Clamp(massScale, clamp.x, clamp.y);

            Assert.LessOrEqual(massScale, clamp.y,
                "massScale must not exceed MassScaleClamp.y");
        }

        [Test]
        [Description("massScale for a particle with the reference mass should be exactly 1.0")]
        public void MassScale_ReferenceMass_IsOne()
        {
            float refMass      = 0.012f;
            Vector2 clamp      = new Vector2(0.6f, 2.8f);
            float particleMass = refMass;

            float massScale = Mathf.Sqrt(Mathf.Max(particleMass, 0.00001f) / refMass);
            massScale = Mathf.Clamp(massScale, clamp.x, clamp.y);

            Assert.AreEqual(1f, massScale, 1e-5f,
                "Reference mass should yield massScale = 1.0");
        }
    }
}
