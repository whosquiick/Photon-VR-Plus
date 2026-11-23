#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Photon.VR
{
    [InitializeOnLoad]
    static class PhotonVREditModeHooks
    {
        static PhotonVREditModeHooks()
        {
            PhotonVREditMode.Bind(
                action => EditorApplication.delayCall += () => action(),
                obj => { if (obj != null) EditorUtility.SetDirty(obj); },
                EditorUtility.IsPersistent,
                scene => { if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene); },
                obj =>
                {
                    if (obj == null) return;
#if UNITY_2021_2_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(obj);
#else
                    AssetDatabase.SaveAssets();
#endif
                });
        }
    }
}
#endif
