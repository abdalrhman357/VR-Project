using NUnit.Framework;
using UnityEngine;

namespace Simulation.Tests.EditMode
{
    /// <summary>
    /// Edit-Mode Tests — FluidParticleData
    ///
    /// Tests the data class used by PaintCanvas.TryPaint in isolation.
    /// No MonoBehaviour, no scene — pure unit tests.
    /// </summary>
    [TestFixture]
    [Category("PaintCanvas")]
    public class FluidParticleDataTests
    {
        private const float DT = 0.02f; // 50 Hz fixed timestep

        // ── Construction ─────────────────────────────────────────────────────

        [Test]
        [Description("A new particle constructed with an initial velocity should encode it in PrevPosition")]
        public void Constructor_WithInitialVelocity_PrevPositionIsCorrect()
        {
            Vector3 startPos        = new Vector3(1f, 2f, 3f);
            Vector3 initialVelocity = new Vector3(0f, -5f, 0f);

            var p = new FluidParticleData(startPos, initialVelocity,
                                          Color.red, 1f, DT);

            Vector3 expected = startPos - initialVelocity * DT;
            Assert.AreEqual(expected, p.PrevPosition,
                "PrevPosition should be startPos - velocity * dt");
        }

        [Test]
        [Description("A newly created particle is Active")]
        public void Constructor_NewParticle_IsActive()
        {
            var p = new FluidParticleData(Vector3.zero, Vector3.zero, Color.blue, 1f, DT);
            Assert.IsTrue(p.IsActive, "Newly created particle must be Active");
        }

        [Test]
        [Description("PaintColor is stored correctly")]
        public void Constructor_Color_StoredCorrectly()
        {
            Color c = new Color(0.3f, 0.6f, 0.9f, 1f);
            var p   = new FluidParticleData(Vector3.zero, Vector3.zero, c, 1f, DT);
            Assert.AreEqual(c, p.PaintColor);
        }

        // ── GetVelocity ───────────────────────────────────────────────────────

        [Test]
        [Description("GetVelocity returns the Verlet velocity (pos - prevPos) / dt")]
        public void GetVelocity_MatchesVerletFormula()
        {
            Vector3 expectedVelocity = new Vector3(0f, -9.8f * DT, 0f);
            var p = new FluidParticleData(
                new Vector3(0f, 10f, 0f),
                expectedVelocity,
                Color.white, 1f, DT);

            Vector3 computed = p.GetVelocity(DT);
            Assert.AreEqual(expectedVelocity.x, computed.x, 1e-4f);
            Assert.AreEqual(expectedVelocity.y, computed.y, 1e-4f);
            Assert.AreEqual(expectedVelocity.z, computed.z, 1e-4f);
        }

        [Test]
        [Description("A stationary particle has velocity magnitude = 0")]
        public void GetVelocity_StationaryParticle_IsZero()
        {
            var p = new FluidParticleData(Vector3.zero, Vector3.zero, Color.white, 1f, DT);
            Assert.AreEqual(0f, p.GetVelocity(DT).magnitude, 1e-6f);
        }

        // ── State Machine ─────────────────────────────────────────────────────

        [Test]
        [Description("Setting State to Splattered makes IsActive return false")]
        public void State_SetSplattered_IsActiveReturnsFalse()
        {
            var p = new FluidParticleData(Vector3.zero, Vector3.zero, Color.white, 1f, DT);
            p.State = FluidParticleData.ParticleState.Splattered;
            Assert.IsFalse(p.IsActive, "Splattered particle should not be Active");
        }

        [Test]
        [Description("Integrate does nothing if the particle is Splattered")]
        public void Integrate_SplatteredParticle_PositionUnchanged()
        {
            var p = new FluidParticleData(
                new Vector3(0f, 5f, 0f), Vector3.zero, Color.white, 1f, DT);
            p.State = FluidParticleData.ParticleState.Splattered;

            Vector3 beforePos = p.Position;
            p.Integrate(DT, Physics.gravity, 0.99f);

            Assert.AreEqual(beforePos, p.Position,
                "Splattered particles must not move");
        }

        // ── Verlet Integration ────────────────────────────────────────────────

        [Test]
        [Description("Integrate with gravity should move the particle downward")]
        public void Integrate_WithGravity_MovesDownward()
        {
            var p = new FluidParticleData(
                new Vector3(0f, 10f, 0f), Vector3.zero, Color.white, 1f, DT);

            float startY = p.Position.y;
            for (int i = 0; i < 10; i++)
                p.Integrate(DT, new Vector3(0f, -9.81f, 0f), 0.99f);

            Assert.Less(p.Position.y, startY,
                "Particle should fall under gravity");
        }

        [Test]
        [Description("AddForce increases acceleration proportional to 1/mass")]
        public void AddForce_IncreasesAcceleration()
        {
            var p = new FluidParticleData(Vector3.zero, Vector3.zero, Color.white, 1f, DT);
            p.Mass = 2f;

            p.AddForce(new Vector3(10f, 0f, 0f));

            Assert.AreEqual(5f, p.Acceleration.x, 1e-5f,
                "Force=10, Mass=2 → Acceleration.x should be 5");
        }

        // ── Viscosity ─────────────────────────────────────────────────────────

        [Test]
        [Description("Viscosity is stored exactly as supplied in the constructor")]
        public void Constructor_Viscosity_StoredCorrectly()
        {
            float vis = 2.5f;
            var p = new FluidParticleData(Vector3.zero, Vector3.zero, Color.white, vis, DT);
            Assert.AreEqual(vis, p.Viscosity, 1e-6f);
        }
    }
}
