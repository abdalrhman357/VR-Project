using UnityEngine;
using UnityEngine.SceneManagement;
using Seb.Fluid.Simulation;
using SwingingPaint.Core;

namespace SwingingPaint.Tools
{
    /// <summary>
    /// Auto-builds the fluid sandbox when Play starts in the "FluidSandbox" scene. Guarded to that scene
    /// so it never disturbs the studio scene, and idempotent.
    /// </summary>
    public static class FluidSandboxBootstrap
    {
        public const string SceneName = "FluidSandbox";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;
            if (SceneRefs.Exists<FluidSandboxBuilder>()) return;
            if (SceneRefs.Exists<FluidSim>()) return;

            new GameObject("[FluidSandboxBootstrap]").AddComponent<FluidSandboxBuilder>();
        }
    }
}
