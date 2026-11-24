using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Photon.VR
{
    internal static class PhotonVRIntegrationMenu
    {
        private const string PlayFabDefine = "PHOTONVR_PLAYFAB";

        [MenuItem("Tools/PhotonVR/PlayFab/Enable Integration", false, 40)]
        private static void EnablePlayFab()
        {
            if (!PlayFabSdkPresent())
            {
                EditorUtility.DisplayDialog("PhotonVR", "The PlayFab SDK was not found. Import the PlayFab Unity SDK first.", "OK");
                return;
            }
            SetDefine(PlayFabDefine, true);
        }

        [MenuItem("Tools/PhotonVR/PlayFab/Enable Integration", true)]
        private static bool EnablePlayFabValidate() => !HasDefine(PlayFabDefine);

        [MenuItem("Tools/PhotonVR/PlayFab/Disable Integration", false, 41)]
        private static void DisablePlayFab() => SetDefine(PlayFabDefine, false);

        [MenuItem("Tools/PhotonVR/PlayFab/Disable Integration", true)]
        private static bool DisablePlayFabValidate() => HasDefine(PlayFabDefine);

        [MenuItem("Tools/PhotonVR/Create Blocklist Asset", false, 30)]
        private static void CreateBlocklist() => CreateResource<PhotonVRBlocklist>("PhotonVRBlocklist");

        [MenuItem("Tools/PhotonVR/Create Cosmetic Registry", false, 31)]
        private static void CreateRegistry() => CreateResource<PhotonVRCosmeticRegistry>("PhotonVRCosmeticRegistry");

        private static void CreateResource<T>(string name) where T : ScriptableObject
        {
            string path = $"Assets/Resources/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                Directory.CreateDirectory("Assets/Resources");
                existing = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(existing, path);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        private static bool PlayFabSdkPresent() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("PlayFab.PlayFabClientAPI", false) != null);

        // The BuildTargetGroup overloads are obsolete from 2021.2 (warnings on Unity 6), so use NamedBuildTarget there.
        private static string ReadDefines(BuildTargetGroup group)
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
        }

        private static void WriteDefines(BuildTargetGroup group, string defines)
        {
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
#endif
        }

        private static List<string> GetDefines() =>
            ReadDefines(EditorUserBuildSettings.selectedBuildTargetGroup)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();

        private static bool HasDefine(string define) => GetDefines().Contains(define);

        private static void SetDefine(string define, bool on)
        {
            // Apply to every installed group so Android (Quest) and Standalone builds match.
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android, EditorUserBuildSettings.selectedBuildTargetGroup }.Distinct())
            {
                var defines = ReadDefines(group)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
                if (on == defines.Contains(define)) continue;
                if (on) defines.Add(define);
                else defines.Remove(define);
                WriteDefines(group, string.Join(";", defines));
            }
            Debug.Log($"[PhotonVR] {define} {(on ? "enabled" : "disabled")}. Unity will recompile.");
        }
    }
}
