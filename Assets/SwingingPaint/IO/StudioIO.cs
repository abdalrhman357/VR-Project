using System;
using System.IO;
using UnityEngine;
using SwingingPaint.Core;
using SwingingPaint.Canvas;
using SwingingPaint.Managers;

namespace SwingingPaint.IO
{
    /// <summary>
    /// Save/Load (simulation config as JSON), Painting export (canvas → PNG), and Screenshot capture.
    /// Files go to Application.persistentDataPath so they always have a writable location. The control
    /// panel calls these; <see cref="LastMessage"/> is surfaced in the UI for feedback.
    /// </summary>
    public sealed class StudioIO : MonoBehaviour
    {
        public SimulationManager sim;
        public PaintCanvas canvas;

        public string LastMessage { get; private set; } = "";
        public string Folder => Application.persistentDataPath;

        string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        public void SaveConfig(string name = "config")
        {
            try
            {
                if (sim == null) { LastMessage = "No SimulationManager"; return; }
                string path = Path.Combine(Folder, name + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(sim.Config, true));
                LastMessage = "Saved config → " + path;
                Debug.Log("[StudioIO] " + LastMessage);
            }
            catch (Exception e) { LastMessage = "Save failed: " + e.Message; }
        }

        public void LoadConfig(string name = "config")
        {
            try
            {
                if (sim == null) { LastMessage = "No SimulationManager"; return; }
                string path = Path.Combine(Folder, name + ".json");
                if (!File.Exists(path)) { LastMessage = "No saved config at " + path; return; }
                var cfg = JsonUtility.FromJson<SimulationConfig>(File.ReadAllText(path));
                if (cfg == null) { LastMessage = "Config parse failed"; return; }
                sim.SetConfig(cfg);
                sim.ResetSimulation(); // re-distributes the new config to every subsystem
                LastMessage = "Loaded config ← " + path;
            }
            catch (Exception e) { LastMessage = "Load failed: " + e.Message; }
        }

        public void ExportPainting()
        {
            try
            {
                if (canvas == null) { LastMessage = "No PaintCanvas"; return; }
                byte[] png = canvas.EncodePNG();
                if (png == null) { LastMessage = "Canvas not ready"; return; }
                string path = Path.Combine(Folder, "painting_" + Stamp() + ".png");
                File.WriteAllBytes(path, png);
                LastMessage = "Exported painting → " + path;
                Debug.Log("[StudioIO] " + LastMessage);
            }
            catch (Exception e) { LastMessage = "Export failed: " + e.Message; }
        }

        public void CaptureScreenshot()
        {
            string path = Path.Combine(Folder, "screenshot_" + Stamp() + ".png");
            ScreenCapture.CaptureScreenshot(path);
            LastMessage = "Screenshot → " + path;
        }

        public void OpenFolder()
        {
            Application.OpenURL("file://" + Folder);
        }
    }
}
