using UnityEngine;
using UnityEngine.Rendering;
using Seb.Fluid.Simulation;
using SwingingPaint.Demo;

namespace SwingingPaint.Integration
{
    /// <summary>
    /// Links the REAL SPH paint to the canvas. Every few frames it asynchronously
    /// reads back the fluid's particle positions from the GPU and deposits the ones
    /// crossing a thin band just above the canvas plane, using the currently
    /// selected palette colour. Switching colour mid-run therefore produces the
    /// layered, non-blended look required by the brief.
    ///
    /// It reads the fluid only (never writes), so the fluid simulation is untouched.
    /// The "band" method avoids needing per-particle tracking (particle indices are
    /// reshuffled every frame by the spatial-hash sort): a particle passes through
    /// the band in ~1 frame, so it deposits roughly once as it falls past the canvas.
    /// </summary>
    public class FluidCanvasDepositor : MonoBehaviour
    {
        public FluidSim fluid;
        public PaintCanvas canvas;

        [Header("Palette (switch live → layers)")]
        public Color[] palette =
        {
            new Color(0.90f, 0.15f, 0.15f),
            new Color(0.15f, 0.35f, 0.90f),
            new Color(0.98f, 0.80f, 0.10f),
            new Color(0.15f, 0.70f, 0.30f),
            new Color(0.10f, 0.10f, 0.12f),
            new Color(0.95f, 0.95f, 0.97f),
        };
        public int currentColor = 0;

        [Header("Deposition")]
        public bool depositing = true;
        [Tooltip("World-Y thickness of the deposition band above the canvas.")]
        public float bandThickness = 0.08f;
        [Tooltip("Read the GPU every N frames (throttling; readback is async anyway).")]
        public int frameInterval = 2;
        [Tooltip("Max particles deposited per readback (cost cap).")]
        public int maxDepositsPerBatch = 6000;
        [Tooltip("Per-particle coverage amount.")]
        public float depositAmount = 0.7f;

        bool _pending;
        int _frame;

        public int LastDeposited { get; private set; }

        void Update()
        {
            if (!depositing || fluid == null || canvas == null) return;

            var buf = fluid.positionBuffer;
            if (buf == null || buf.count == 0) return;

            _frame++;
            if (_pending) return;
            if (frameInterval > 1 && (_frame % frameInterval) != 0) return;

            _pending = true;
            AsyncGPUReadback.Request(buf, OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            _pending = false;
            if (req.hasError || canvas == null || !depositing) return;

            var data = req.GetData<Vector3>();   // particle positions in WORLD space
            float y0 = canvas.SurfaceY;
            float loY = y0 - bandThickness;
            float hiY = y0 + bandThickness;
            Color col = palette.Length > 0 ? palette[Mathf.Clamp(currentColor, 0, palette.Length - 1)] : Color.white;

            int deposits = 0;
            int count = data.Length;
            for (int i = 0; i < count && deposits < maxDepositsPerBatch; i++)
            {
                Vector3 p = data[i];
                if (p.y < loY || p.y > hiY) continue;         // not near the canvas plane
                if (!canvas.ContainsXZ(p)) continue;          // outside the canvas footprint
                canvas.Deposit(new Vector3(p.x, y0, p.z), col, depositAmount);
                deposits++;
            }
            LastDeposited = deposits;
        }
    }
}
