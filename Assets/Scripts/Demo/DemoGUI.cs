using UnityEngine;
using SwingingPaint.Rope;

namespace SwingingPaint.Demo
{
    /// <summary>
    /// Runtime control panel + live statistics, drawn with IMGUI so it needs no
    /// UI assets. A first cut of the "professional control panel" the brief asks
    /// for; it reads the stats already exposed by <see cref="PaintPendulumSystem"/>.
    /// </summary>
    public class DemoGUI : MonoBehaviour
    {
        public PaintPendulumSystem pendulum;
        public PaintDripSystem drip;
        public PaintCanvas canvas;

        string[] _ropeNames;
        int _ropeIdx;
        float _ropeLen = 2f;
        int _ropeNodes = 24;
        float _fps;

        void Start()
        {
            _ropeNames = System.Enum.GetNames(typeof(RopeType));
            if (pendulum != null)
            {
                _ropeIdx = (int)pendulum.ropeType;
                _ropeLen = pendulum.ropeLength;
                _ropeNodes = pendulum.ropeNodes;
            }
        }

        void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(1e-5f, Time.unscaledDeltaTime), 0.1f);
        }

        void OnGUI()
        {
            if (pendulum == null) return;

            // ── Controls panel ───────────────────────────────────────────────
            GUILayout.BeginArea(new Rect(10, 10, 300, 620), GUI.skin.box);
            GUILayout.Label("<b>Swinging Paint Bucket — Controls</b>");

            GUILayout.Space(4);
            GUILayout.Label("Rope type");
            int newRope = GUILayout.SelectionGrid(_ropeIdx, _ropeNames, 3);
            if (newRope != _ropeIdx)
            {
                _ropeIdx = newRope;
                pendulum.SetRopeType((RopeType)_ropeIdx);
            }

            GUILayout.Label($"Rope length: {_ropeLen:0.00} m");
            _ropeLen = GUILayout.HorizontalSlider(_ropeLen, 0.5f, 5f);
            GUILayout.Label($"Rope nodes: {_ropeNodes}");
            _ropeNodes = Mathf.RoundToInt(GUILayout.HorizontalSlider(_ropeNodes, 4, 64));
            if (GUILayout.Button("Apply rope length / nodes"))
            {
                pendulum.ropeLength = _ropeLen;
                pendulum.ropeNodes = _ropeNodes;
                pendulum.BuildSystem();
            }

            GUILayout.Space(4);
            GUILayout.Label($"Paint mass: {pendulum.paintMass:0.000} kg");
            pendulum.paintMass = GUILayout.HorizontalSlider(pendulum.paintMass, 0f, 3f);

            GUILayout.Label($"Gravity: {pendulum.gravity.y:0.0}");
            pendulum.gravity.y = GUILayout.HorizontalSlider(pendulum.gravity.y, -30f, 0f);

            GUILayout.Label($"Sub-steps: {pendulum.substeps}");
            pendulum.substeps = Mathf.RoundToInt(GUILayout.HorizontalSlider(pendulum.substeps, 1, 24));

            if (drip != null)
            {
                GUILayout.Space(4);
                drip.pouring = GUILayout.Toggle(drip.pouring, " Pour paint");
                GUILayout.Label($"Outlet diameter: {drip.outletDiameter*100f:0.0} cm");
                drip.outletDiameter = GUILayout.HorizontalSlider(drip.outletDiameter, 0.005f, 0.08f);
                GUILayout.Label($"Viscosity: {drip.viscosity:0.00}");
                drip.viscosity = GUILayout.HorizontalSlider(drip.viscosity, 0f, 1f);

                GUILayout.Label("Paint colour (switch live → layers)");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < drip.palette.Length; i++)
                {
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = drip.palette[i];
                    string lbl = (i == drip.currentColor) ? "◉" : " ";
                    if (GUILayout.Button(lbl, GUILayout.Height(24))) drip.currentColor = i;
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset rig")) { pendulum.ResetSystem(); if (drip) drip.ClearDroplets(); }
            if (canvas != null && GUILayout.Button("Clear canvas")) canvas.ClearCanvas();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Controls</b>\nLMB: grab/pull  ·  Shift+LMB: twist\nRMB: orbit cam  ·  wheel: zoom\nP: push  ·  F: force  ·  Q/E: spin  ·  Space: kick");
            GUILayout.EndArea();

            // ── Stats panel ──────────────────────────────────────────────────
            GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 230), GUI.skin.box);
            GUILayout.Label("<b>Statistics</b>");
            GUILayout.Label($"FPS: {_fps:0}");
            GUILayout.Label($"Rope tension: {pendulum.RopeTension:0.0} N");
            GUILayout.Label($"Rope stretch: {pendulum.RopeStretch*100f:0.0} cm");
            GUILayout.Label($"Rope broken: {(pendulum.RopeBroken ? "<color=red>YES</color>" : "no")}");
            GUILayout.Label($"Bucket speed: {pendulum.BucketSpeed:0.00} m/s");
            GUILayout.Label($"Angular speed: {pendulum.AngularSpeed:0.00} rad/s");
            GUILayout.Label($"Kinetic energy: {pendulum.KineticEnergy:0.00} J");
            if (drip != null)
            {
                GUILayout.Label($"Droplets: {drip.ActiveDroplets}");
                GUILayout.Label($"Paint used: {drip.PaintConsumed*1000f:0.0} g");
            }
            GUILayout.EndArea();
        }
    }
}
