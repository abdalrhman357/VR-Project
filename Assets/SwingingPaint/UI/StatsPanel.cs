using UnityEngine;
using UnityEngine.Profiling;
using SwingingPaint.Managers;

namespace SwingingPaint.UI
{
    /// <summary>
    /// Live performance + simulation statistics overlay (Part 7): FPS / frame time, particle count,
    /// fixed sub-steps per frame, simulation time, pause state, and memory (managed + total allocated).
    /// IMGUI, top-right, no setup. Press J to hide/show.
    /// </summary>
    public sealed class StatsPanel : MonoBehaviour
    {
        public SimulationManager sim;
        public bool visible = true;

        float _fps = 60f;
        float _worstMs;
        float _worstResetTimer;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.J)) visible = !visible;

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.08f);

            float ms = dt * 1000f;
            if (ms > _worstMs) _worstMs = ms;
            _worstResetTimer += dt;
            if (_worstResetTimer > 3f) { _worstResetTimer = 0f; _worstMs = ms; }
        }

        void OnGUI()
        {
            if (!visible) return;

            int particles = (sim != null && sim.fluidSim != null && sim.fluidSim.positionBuffer != null)
                ? sim.fluidSim.positionBuffer.count : 0;
            long managedMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
            long totalMB = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

            var sb = _sb;
            sb.Length = 0;
            sb.Append("<b>STATS</b>  (J to hide)\n");
            sb.Append("FPS: ").Append(Mathf.RoundToInt(_fps))
              .Append("   (").Append((1000f / Mathf.Max(_fps, 0.01f)).ToString("0.0")).Append(" ms)\n");
            sb.Append("Worst (3s): ").Append(_worstMs.ToString("0.0")).Append(" ms\n");
            sb.Append("Particles: ").Append(particles.ToString("N0")).Append('\n');
            if (sim != null)
            {
                sb.Append("Sub-steps/frame: ").Append(sim.StepsLastFrame).Append('\n');
                sb.Append("Sim time: ").Append(sim.SimTime.ToString("0.0")).Append(" s\n");
                sb.Append("State: ").Append(sim.IsPaused ? "PAUSED" : "running").Append('\n');
            }
            sb.Append("Mem managed: ").Append(managedMB).Append(" MB\n");
            sb.Append("Mem total: ").Append(totalMB).Append(" MB");

            float w = 230f, h = 168f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 8, 8, w, h), GUI.skin.box);
            GUILayout.Label(sb.ToString());
            GUILayout.EndArea();
        }

        readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(256);
    }
}
