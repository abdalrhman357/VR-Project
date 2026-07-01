using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Reference-lookup helpers using the current, non-deprecated <c>FindObjectsByType</c> API
    /// (Unity 6000.4 deprecated the whole <c>FindFirstObjectByType</c> family). These are only used as
    /// fallbacks/guards — the scene builders wire every reference explicitly — so the array allocation
    /// is irrelevant (called at build/reset time, never per frame).
    /// </summary>
    public static class SceneRefs
    {
        public static T FindFirst<T>() where T : Object
        {
            T[] all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return all.Length > 0 ? all[0] : null;
        }

        public static bool Exists<T>() where T : Object => FindFirst<T>() != null;
    }
}
