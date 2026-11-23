#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace Photon.VR
{
    /// <summary>
    /// Edit-mode helpers used by Runtime scripts. PhotonVR.Editor fills these in so the Runtime
    /// assembly never references editor APIs (Unity would otherwise treat the scripts as both
    /// Editor and Runtime).
    /// </summary>
    internal static class PhotonVREditMode
    {
        private static Action<Action> delayCall;
        private static Action<UnityEngine.Object> setDirty;
        private static Func<UnityEngine.Object, bool> isPersistent;
        private static Action<Scene> markSceneDirty;
        private static Action<UnityEngine.Object> saveAsset;
        private static List<Action> pendingDelay;

        internal static void Bind(
            Action<Action> delay,
            Action<UnityEngine.Object> dirty,
            Func<UnityEngine.Object, bool> persistent,
            Action<Scene> sceneDirty,
            Action<UnityEngine.Object> save)
        {
            delayCall = delay;
            setDirty = dirty;
            isPersistent = persistent;
            markSceneDirty = sceneDirty;
            saveAsset = save;
            if (pendingDelay == null) return;
            for (int i = 0; i < pendingDelay.Count; i++)
                delayCall(pendingDelay[i]);
            pendingDelay = null;
        }

        internal static void Delay(Action action)
        {
            if (action == null) return;
            if (delayCall != null)
            {
                delayCall(action);
                return;
            }
            if (pendingDelay == null) pendingDelay = new List<Action>();
            pendingDelay.Add(action);
        }

        internal static void SetDirty(UnityEngine.Object obj) => setDirty?.Invoke(obj);

        internal static bool IsPersistent(UnityEngine.Object obj) =>
            isPersistent != null && isPersistent(obj);

        internal static void MarkSceneDirty(Scene scene) => markSceneDirty?.Invoke(scene);

        internal static void SaveAsset(UnityEngine.Object obj) => saveAsset?.Invoke(obj);
    }
}
#endif
