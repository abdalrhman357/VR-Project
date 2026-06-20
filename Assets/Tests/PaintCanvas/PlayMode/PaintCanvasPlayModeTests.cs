using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PaintCanvasComp = PaintCanvas;

namespace Simulation.Tests.PlayMode
{
    /// <summary>
    /// Play-Mode Tests — PaintCanvas MonoBehaviour
    ///
    /// Play-Mode tests run inside a real Unity runtime loop (Awake, Start, Update fire).
    /// This lets us test the full TryPaint pipeline including:
    ///   • Texture initialisation in Awake
    ///   • TryPaint hit / miss paths
    ///   • Clear()
    ///   • Wet/dry pixel state via WaitForSeconds
    ///
    /// Fixture helper: each test creates a minimal "canvas" GameObject with
    ///   • a MeshRenderer + MeshFilter (required by PaintCanvas.Awake)
    ///   • the PaintCanvas component itself
    /// and destroys it in TearDown.
    /// </summary>
    [TestFixture]
    [Category("PaintCanvas")]
    public class PaintCanvasPlayModeTests
    {
        // ── Scene objects shared by every test ───────────────────────────────

        private GameObject  _canvasGO;
        private PaintCanvasComp _canvas;
        private const float DT = 0.02f;

        // ── Setup / TearDown ──────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _canvasGO = new GameObject("TestCanvas");

            var mf      = _canvasGO.AddComponent<MeshFilter>();
            mf.mesh     = BuildQuadMesh();
            var mr      = _canvasGO.AddComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Standard"));

            _canvas = _canvasGO.AddComponent<PaintCanvasComp>();

            _canvas.TextureRes       = 128;
            _canvas.CanvasWidth      = 2f;
            _canvas.CanvasHeight     = 2f;
            _canvas.HitThreshold     = 0.05f;
            _canvas.Humidity         = 0.5f;
            _canvas.TiltAngle        = 0f;
            _canvas.PaintNormalLocal = Vector3.forward;
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGO != null)
                Object.DestroyImmediate(_canvasGO);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh();
            mesh.vertices  = new[] {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv        = new[] {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            return mesh;
        }

        private static FluidParticleData MakeParticle(
            Vector3 position, Vector3 velocity = default, Color color = default)
        {
            if (color == default) color = Color.red;
            return new FluidParticleData(position, velocity, color, 1f, DT)
            {
                Mass = 0.012f
            };
        }

        // ── Texture Initialization ────────────────────────────────────────────

        [UnityTest]
        [Description("After one frame, the canvas renderer has a non-null mainTexture")]
        public IEnumerator Awake_RendererHasTexture()
        {
            yield return null;

            var tex = _canvasGO.GetComponent<Renderer>().material.mainTexture;
            Assert.IsNotNull(tex, "mainTexture should be set after Awake");
        }

        [UnityTest]
        [Description("Initial canvas texture is white at UV (0.5, 0.5)")]
        public IEnumerator Awake_InitialPixel_IsWhite()
        {
            yield return null;

            var tex = _canvasGO.GetComponent<Renderer>().material.mainTexture as Texture2D;
            Assert.IsNotNull(tex);

            int cx = tex.width  / 2;
            int cy = tex.height / 2;
            Color pixel = tex.GetPixel(cx, cy);

            Assert.AreEqual(Color.white, pixel, "Initial centre pixel must be white");
        }

        // ── TryPaint — Hit Path ───────────────────────────────────────────────

        [UnityTest]
        [Description("TryPaint returns true for a particle on the canvas surface")]
        public IEnumerator TryPaint_ParticleOnSurface_ReturnsTrue()
        {
            yield return null;

            var p   = MakeParticle(new Vector3(0f, 0f, 0f));
            bool hit = _canvas.TryPaint(p, DT);
            Assert.IsTrue(hit, "Particle on the surface must be painted");
        }

        [UnityTest]
        [Description("TryPaint marks the particle as Splattered on success")]
        public IEnumerator TryPaint_OnHit_ParticleIsSplattered()
        {
            yield return null;

            var p = MakeParticle(new Vector3(0f, 0f, 0f));
            _canvas.TryPaint(p, DT);

            Assert.AreEqual(FluidParticleData.ParticleState.Splattered, p.State,
                "Particle must be Splattered after a successful TryPaint");
        }

        [UnityTest]
        [Description("After TryPaint hits the centre, the centre pixel is no longer white")]
        public IEnumerator TryPaint_Hit_ChangesCentrePixel()
        {
            yield return null;

            var p = MakeParticle(new Vector3(0f, 0f, 0f),
                                 velocity: new Vector3(0f, 0f, -2f),
                                 color:    Color.red);
            _canvas.TryPaint(p, DT);

            yield return null; // let LateUpdate flush the texture

            var tex = _canvasGO.GetComponent<Renderer>().material.mainTexture as Texture2D;
            int cx  = tex.width  / 2;
            int cy  = tex.height / 2;
            Color pixel = tex.GetPixel(cx, cy);

            Assert.AreNotEqual(Color.white, pixel,
                "Centre pixel should change after a paint hit");
        }

        // ── TryPaint — Miss Paths ─────────────────────────────────────────────

        [UnityTest]
        [Description("TryPaint returns false for an inactive particle")]
        public IEnumerator TryPaint_InactiveParticle_ReturnsFalse()
        {
            yield return null;

            var p = MakeParticle(new Vector3(0f, 0f, 0f));
            p.State = FluidParticleData.ParticleState.Splattered;

            bool hit = _canvas.TryPaint(p, DT);
            Assert.IsFalse(hit, "Inactive particle must not paint");
        }

        [UnityTest]
        [Description("TryPaint returns false when the particle is too far from the surface")]
        public IEnumerator TryPaint_TooFarFromSurface_ReturnsFalse()
        {
            yield return null;

            var p   = MakeParticle(new Vector3(0f, 0f, 0.2f));
            bool hit = _canvas.TryPaint(p, DT);
            Assert.IsFalse(hit, "Particle too far from surface should be rejected");
        }

        [UnityTest]
        [Description("TryPaint returns false when the particle is outside the canvas bounds")]
        public IEnumerator TryPaint_OutsideCanvasBounds_ReturnsFalse()
        {
            yield return null;

            var p   = MakeParticle(new Vector3(1.5f, 0f, 0f));
            bool hit = _canvas.TryPaint(p, DT);
            Assert.IsFalse(hit, "Particle outside canvas bounds must be rejected");
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Description("Clear() resets all pixels back to white after painting")]
        public IEnumerator Clear_AfterPainting_PixelsAreWhite()
        {
            yield return null;

            var p = MakeParticle(new Vector3(0f, 0f, 0f),
                                 velocity: new Vector3(0f, 0f, -2f),
                                 color:    Color.blue);
            _canvas.TryPaint(p, DT);
            yield return null;

            _canvas.Clear();
            yield return null;

            var tex       = _canvasGO.GetComponent<Renderer>().material.mainTexture as Texture2D;
            Color[] all   = tex.GetPixels();
            bool allWhite = true;
            foreach (var c in all)
                if (c != Color.white) { allWhite = false; break; }

            Assert.IsTrue(allWhite, "All pixels must be white after Clear()");
        }

        // ── Near-Threshold Edge Cases ─────────────────────────────────────────

        [UnityTest]
        [Description("Particle exactly at HitThreshold distance should be rejected (boundary condition)")]
        public IEnumerator TryPaint_ExactlyAtThreshold_IsRejected()
        {
            yield return null;

            var p   = MakeParticle(new Vector3(0f, 0f, _canvas.HitThreshold));
            bool hit = _canvas.TryPaint(p, DT);
            Assert.IsFalse(hit,
                "Particle exactly at the boundary should not paint");
        }

        [UnityTest]
        [Description("Two consecutive hits at the same UV produce wet-on-wet blending")]
        public IEnumerator TryPaint_TwoHitsSameUV_BlendedResult()
        {
            yield return null;

            var p1 = MakeParticle(Vector3.zero, color: Color.red);
            var p2 = MakeParticle(Vector3.zero, color: Color.blue);

            _canvas.TryPaint(p1, DT);
            _canvas.TryPaint(p2, DT);
            yield return null;

            var tex   = _canvasGO.GetComponent<Renderer>().material.mainTexture as Texture2D;
            Color pixel = tex.GetPixel(tex.width / 2, tex.height / 2);

            bool isPureRed  = Mathf.Approximately(pixel.r, 1f) &&
                              Mathf.Approximately(pixel.g, 0f) &&
                              Mathf.Approximately(pixel.b, 0f);
            bool isPureBlue = Mathf.Approximately(pixel.r, 0f) &&
                              Mathf.Approximately(pixel.g, 0f) &&
                              Mathf.Approximately(pixel.b, 1f);

            Assert.IsFalse(isPureRed || isPureBlue,
                $"Second paint should blend with first. Pixel = {pixel}");
        }

        // ── Multiple Particles ────────────────────────────────────────────────

        [UnityTest]
        [Description("Many particles at different positions all register hits")]
        public IEnumerator TryPaint_ManyParticles_AllHit()
        {
            yield return null;

            Vector3[] positions = {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0f,    0f,   0f)
            };

            int hits = 0;
            foreach (var pos in positions)
            {
                var p = MakeParticle(pos, color: Color.green);
                if (_canvas.TryPaint(p, DT)) hits++;
            }

            Assert.AreEqual(positions.Length, hits,
                "All in-bounds particles should register hits");
        }
    }
}
