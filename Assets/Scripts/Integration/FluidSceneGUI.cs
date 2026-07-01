using UnityEngine;
using Seb.Fluid.Simulation;
using SwingingPaint;
using SwingingPaint.Rope;

namespace SwingingPaint.Integration
{
    /// <summary>
    /// Runtime control panel + live stats for the consolidated (fluid) scene.
    /// Controls the pendulum and the SPH→canvas paint colour, and reports both
    /// pendulum stats and the real fluid particle count. IMGUI = no UI assets.
    /// </summary>
    public class FluidSceneGUI : MonoBehaviour
    {
        public PaintPendulumSystem pendulum;
        public FluidCanvasDepositor depositor;
        public Demo.PaintCanvas canvas;
        public FluidSim fluid;

        [Tooltip("Rope-length slider bounds (set proportionally by the binder).")]
        public float ropeLenMin = 0.5f;
        public float ropeLenMax = 5f;

        string[] _ropeNames;
        int _ropeIdx;
        float _ropeLen = 2f;
        float _fps;

        void Start()
        {
            _ropeNames = System.Enum.GetNames(typeof(RopeType));
            if (pendulum != null) { _ropeIdx = (int)pendulum.ropeType; _ropeLen = pendulum.ropeLength; }
        }

        void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(1e-5f, Time.unscaledDeltaTime), 0.1f);
        }

        void OnGUI()
        {
            if (pendulum == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 560), GUI.skin.box);
            GUILayout.Label("<b>Swinging Paint Bucket — Integrated</b>");

            GUILayout.Space(4);
            GUILayout.Label("Rope type");
            int newRope = GUILayout.SelectionGrid(_ropeIdx, _ropeNames, 3);
            if (newRope != _ropeIdx) { _ropeIdx = newRope; pendulum.SetRopeType((RopeType)_ropeIdx); }

            GUILayout.Label($"Rope length: {_ropeLen:0.00} m");
            _ropeLen = GUILayout.HorizontalSlider(_ropeLen, ropeLenMin, ropeLenMax);
            if (GUILayout.Button("Apply rope length"))
            {
                pendulum.ropeLength = _ropeLen;
                pendulum.BuildSystem();
            }

            GUILayout.Space(4);
            GUILayout.Label($"Paint mass (bucket dynamics): {pendulum.paintMass:0.00} kg");
            pendulum.paintMass = GUILayout.HorizontalSlider(pendulum.paintMass, 0f, 5f);

            GUILayout.Label($"Gravity: {pendulum.gravity.y:0.0}");
            pendulum.gravity.y = GUILayout.HorizontalSlider(pendulum.gravity.y, -30f, 0f);

            GUILayout.Label($"Sub-steps: {pendulum.substeps}");
            pendulum.substeps = Mathf.RoundToInt(GUILayout.HorizontalSlider(pendulum.substeps, 1, 24));

            if (fluid != null)
            {
                GUILayout.Label($"Paint viscosity: {fluid.viscosityStrength:0.00}");
                fluid.viscosityStrength = GUILayout.HorizontalSlider(fluid.viscosityStrength, 0f, 2f);
            }

            if (depositor != null)
            {
                GUILayout.Space(4);
                depositor.depositing = GUILayout.Toggle(depositor.depositing, " Deposit paint on canvas");
                GUILayout.Label("Paint colour (switch live → layers)");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < depositor.palette.Length; i++)
                {
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = depositor.palette[i];
                    string lbl = (i == depositor.currentColor) ? "◉" : " ";
                    if (GUILayout.Button(lbl, GUILayout.Height(24))) depositor.currentColor = i;
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset rig")) pendulum.ResetSystem();
            if (canvas != null && GUILayout.Button("Clear canvas")) canvas.ClearCanvas();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Controls</b>\nLMB: grab/pull · Shift+LMB: twist\nP: push · F: force · Q/E: spin · Space: kick");
            GUILayout.EndArea();

            // Stats
            GUILayout.BeginArea(new Rect(Screen.width - 240, 10, 230, 250), GUI.skin.box);
            GUILayout.Label("<b>Statistics</b>");
            GUILayout.Label($"FPS: {_fps:0}");
            if (fluid != null && fluid.positionBuffer != null)
                GUILayout.Label($"Fluid particles: {fluid.positionBuffer.count}");
            GUILayout.Label($"Rope tension: {pendulum.RopeTension:0.0} N");
            GUILayout.Label($"Rope broken: {(pendulum.RopeBroken ? "<color=red>YES</color>" : "no")}");
            GUILayout.Label($"Bucket speed: {pendulum.BucketSpeed:0.00} m/s");
            GUILayout.Label($"Angular speed: {pendulum.AngularSpeed:0.00} rad/s");
            GUILayout.Label($"Kinetic energy: {pendulum.KineticEnergy:0.00} J");
            if (depositor != null) GUILayout.Label($"Deposited/batch: {depositor.LastDeposited}");
            GUILayout.EndArea();
        }
    }
}
