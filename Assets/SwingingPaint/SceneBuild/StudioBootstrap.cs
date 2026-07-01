using UnityEngine;
using UnityEngine.SceneManagement;
using SwingingPaint.Core;
using SwingingPaint.Managers;

namespace SwingingPaint.SceneBuild
{
    /// <summary>
    /// Auto-bootstraps the studio when Play starts in the SwingingPaintStudio scene — so the committed
    /// scene can stay empty and there is literally nothing to assemble by hand (Part 6.5 "ready to run").
    /// Guarded to the studio scene so opening other scenes (e.g. the SPH demo) is never disturbed, and
    /// idempotent so it never double-builds.
    /// </summary>
    public static class StudioBootstrap
    {
        public const string StudioSceneName = "SwingingPaintStudio";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SceneManager.GetActiveScene().name != StudioSceneName) return;
            if (SceneRefs.Exists<StudioSceneBuilder>()) return;
            if (SceneRefs.Exists<SimulationManager>()) return;

            var go = new GameObject("[StudioBootstrap]");
            go.AddComponent<StudioSceneBuilder>(); // buildOnAwake = true → builds the whole studio now
        }
    }
}
