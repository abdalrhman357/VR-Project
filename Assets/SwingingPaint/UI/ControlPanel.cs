using UnityEngine;
using SwingingPaint.Core;
using SwingingPaint.Managers;
using SwingingPaint.IO;

namespace SwingingPaint.UI
{
    /// <summary>
    /// Full real-time control panel (Part 7), drawn with IMGUI so it needs zero scene setup, zero
    /// prefabs/references, and no EventSystem — it just works on Play. Mutates the one shared
    /// SimulationConfig live; structural changes (rope length/segments/material, canvas size, particle
    /// budget) take effect on Reset, which rebuilds those subsystems. Press H to hide/show.
    /// </summary>
    public sealed class ControlPanel : MonoBehaviour
    {
        public SimulationManager sim;
        public StudioIO io;

        public bool visible = true;
        public float panelWidth = 300f;

        Rect _winRect = new Rect(8, 8, 300, 600);
        Vector2 _scroll;
        bool _sWorld = true, _sRope, _sBucket, _sPaint = true, _sCanvas, _sQuality;
        GUIStyle _header;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.H)) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || sim == null || sim.Config == null) return;
            if (_header == null)
                _header = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };

            _winRect.width = panelWidth;
            _winRect.height = Mathf.Min(Screen.height - 16, 660);
            // Draggable window → reachable even if the Game-view Scale is zoomed in.
            _winRect = GUI.Window(73210, _winRect, DrawWindow, "Swinging Paint Studio — Controls (H hide · drag)");
            _winRect.x = Mathf.Clamp(_winRect.x, -panelWidth + 70f, Screen.width - 70f);
            _winRect.y = Mathf.Clamp(_winRect.y, 0f, Screen.height - 40f);
        }

        void DrawWindow(int id)
        {
            var cfg = sim.Config;

            DrawActions(cfg);

            _scroll = GUILayout.BeginScrollView(_scroll);

            if (Section("World", ref _sWorld))
            {
                cfg.world.gravity = Slider("Gravity", cfg.world.gravity, 0f, 30f);
                cfg.world.airResistance = Slider("Air resistance", cfg.world.airResistance, 0f, 1f);
                cfg.world.wind.x = Slider("Wind X", cfg.world.wind.x, -10f, 10f);
                cfg.world.wind.z = Slider("Wind Z", cfg.world.wind.z, -10f, 10f);
                cfg.world.simulationSpeed = Slider("Sim speed", cfg.world.simulationSpeed, 0.05f, 2f);
            }

            if (Section("Rope / Pendulum  (Reset to apply *)", ref _sRope))
            {
                cfg.rope.length = Slider("Length *", cfg.rope.length, 0.5f, 16f);
                cfg.rope.segmentCount = IntSlider("Segments *", cfg.rope.segmentCount, 2, 40);
                cfg.rope.constraintIterations = IntSlider("Iterations", cfg.rope.constraintIterations, 1, 30);
                cfg.rope.bendingStiffness = Slider("Bend stiffness *", cfg.rope.bendingStiffness, 0f, 1f);
                cfg.rope.linearDamping = Slider("Rope damping", cfg.rope.linearDamping, 0f, 1f);
                cfg.rope.initialAngleX = Slider("Init angle X *", cfg.rope.initialAngleX, -80f, 80f);
                cfg.rope.initialAngleZ = Slider("Init angle Z *", cfg.rope.initialAngleZ, -80f, 80f);
                GUILayout.Label("<size=10>Sideways push ⟂ to angle = circle/spiral (0 = straight line)</size>");
                cfg.rope.initialTipVelocity.x = Slider("Push X *", cfg.rope.initialTipVelocity.x, -8f, 8f);
                cfg.rope.initialTipVelocity.y = Slider("Push Z *", cfg.rope.initialTipVelocity.y, -8f, 8f);
                cfg.rope.material = (SimulationConfig.RopeMaterialKind)GUILayout.SelectionGrid(
                    (int)cfg.rope.material, new[] { "Rigid", "Cotton", "Nylon", "Rubber" }, 4);
            }

            if (Section("Bucket", ref _sBucket))
            {
                cfg.bucket.radius = Slider("Radius *", cfg.bucket.radius, 0.2f, 3f);
                cfg.bucket.holeRadius = Slider("Hole radius", cfg.bucket.holeRadius, 0f, 1.2f);
                cfg.bucket.holeOffset.x = Slider("Hole offset X", cfg.bucket.holeOffset.x, -0.5f, 0.5f);
                cfg.bucket.holeOffset.y = Slider("Hole offset Z", cfg.bucket.holeOffset.y, -0.5f, 0.5f);
                cfg.bucket.emptyMass = Slider("Empty mass", cfg.bucket.emptyMass, 0.05f, 10f);
            }

            if (Section("Paint", ref _sPaint))
            {
                cfg.paint.fillAmount = Slider("Fill amount", cfg.paint.fillAmount, 0f, 1f);
                cfg.paint.viscosity = Slider("Viscosity", cfg.paint.viscosity, 0f, 30f);
                cfg.paint.flowRate = Slider("Flow rate", cfg.paint.flowRate, 0.1f, 30f);
                cfg.paint.density = Slider("Density", cfg.paint.density, 800f, 1600f);
                GUILayout.Label("Colour");
                cfg.paint.color.r = Slider("  R", cfg.paint.color.r, 0f, 1f);
                cfg.paint.color.g = Slider("  G", cfg.paint.color.g, 0f, 1f);
                cfg.paint.color.b = Slider("  B", cfg.paint.color.b, 0f, 1f);
                GUI.color = cfg.paint.color; GUILayout.Box("   "); GUI.color = Color.white;
            }

            if (Section("Canvas  (Reset to apply *)", ref _sCanvas))
            {
                cfg.canvas.size.x = Slider("Width *", cfg.canvas.size.x, 0.5f, 14f);
                cfg.canvas.size.y = Slider("Height *", cfg.canvas.size.y, 0.5f, 14f);
                cfg.canvas.absorptionRate = Slider("Absorption", cfg.canvas.absorptionRate, 0f, 1f);
                cfg.canvas.humidity = Slider("Humidity", cfg.canvas.humidity, 0f, 1f);
            }

            if (Section("Quality", ref _sQuality))
            {
                cfg.quality.particleBudget = IntSlider("Particles * (restart)", cfg.quality.particleBudget, 1000, 120000);
                cfg.quality.solverIterationsPerFrame = IntSlider("Solver iters/frame", cfg.quality.solverIterationsPerFrame, 1, 6);
                cfg.quality.renderingQuality = IntSlider("Render quality", cfg.quality.renderingQuality, 0, 3);
            }

            GUILayout.Space(6);
            if (io != null && !string.IsNullOrEmpty(io.LastMessage))
                GUILayout.Label("<size=10>" + io.LastMessage + "</size>");

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 100000, 20));   // drag by the title bar
        }

        void DrawActions(SimulationConfig cfg)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(sim.IsPaused ? "▶ Play" : "❚❚ Pause")) sim.TogglePause();
            if (GUILayout.Button("⟳ Reset")) sim.ResetSimulation();
            GUILayout.EndHorizontal();

            if (io != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save")) io.SaveConfig();
                if (GUILayout.Button("Load")) io.LoadConfig();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Export PNG")) io.ExportPainting();
                if (GUILayout.Button("Screenshot")) io.CaptureScreenshot();
                if (GUILayout.Button("Folder")) io.OpenFolder();
                GUILayout.EndHorizontal();
            }
        }

        // ── IMGUI helpers ────────────────────────────────────────────

        bool Section(string title, ref bool open)
        {
            if (GUILayout.Button((open ? "▼ " : "► ") + title, _header)) open = !open;
            return open;
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value.ToString("0.###"), GUILayout.Width(130));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        static int IntSlider(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value, GUILayout.Width(130));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
