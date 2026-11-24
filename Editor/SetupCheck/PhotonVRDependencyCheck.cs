using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Photon.VR
{
    // Lives in its own tiny editor assembly with no references, so it still loads when PUN, Photon Voice or
    // TextMeshPro are missing and the rest of Photon VR Plus can't compile. Instead of a wall of
    // "namespace Photon could not be found" errors you get one message that says what to install.
    //
    // Unity only loads it while other scripts have errors when the editor starts (that's how Unity works),
    // so after importing into a project without Photon, restart Unity or use Tools > PhotonVR > Check Dependencies.
    [InitializeOnLoad]
    internal static class PhotonVRDependencyCheck
    {
        private const string PunUrl = "https://assetstore.unity.com/packages/tools/network/pun-2-free-119922";
        private const string VoiceUrl = "https://assetstore.unity.com/packages/tools/audio/photon-voice-2-130518";
        private const string PlayFabDefine = "PHOTONVR_PLAYFAB";
        private const string ShownKey = "PhotonVR.DependencyDialogShown";

        static PhotonVRDependencyCheck()
        {
            EditorApplication.delayCall += () => Run(false);
        }

        // A Package Manager install and an imported .unitypackage share the same GUIDs. Unity then treats the
        // .unitypackage files as already in the project ("Nothing to import") and the two copies fight.
        private static void WarnIfInstalledTwice()
        {
            bool upm = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.photonvrplus.core") != null;
            if (!upm) return;
            if (AssetDatabase.FindAssets("PhotonVRManager t:MonoScript", new[] { "Assets" }).Length == 0) return;
            Debug.LogError("[PhotonVR] Photon VR Plus is installed twice: through the Package Manager and as files in Assets. " +
                           "Keep one: remove the package in Window > Package Manager, or delete the Assets/PhotonVR folder.");
        }

        [MenuItem("Tools/PhotonVR/Check Dependencies", false, 1)]
        private static void RunFromMenu() => Run(true);

        private static void Run(bool fromMenu)
        {
            if (Application.isBatchMode && !fromMenu) return;
            WarnIfInstalledTwice();

            var missing = new List<string>();
            bool pun = HasType("Photon.Pun.PhotonNetwork");
            bool voice = HasType("Photon.Voice.Unity.Recorder");
            bool voicePun = HasType("Photon.Voice.PUN.PunVoiceClient") || HasType("Photon.Voice.PUN.PhotonVoiceNetwork");
            bool tmp = HasType("TMPro.TextMeshPro");

            if (!pun) missing.Add("PUN 2 (Asset Store: \"PUN 2 - FREE\". Photon Voice 2 includes it too)");
            if (!voice) missing.Add("Photon Voice 2 (Asset Store: \"Photon Voice 2\")");
            else if (!voicePun) missing.Add("Photon Voice's PUN folder (Assets/Photon/PhotonVoice/Code/PUN was removed - reimport Photon Voice 2)");
            if (!tmp) missing.Add("TextMeshPro (Window > Package Manager > Unity Registry > TextMeshPro)");

            bool playFabDefine = HasDefine(PlayFabDefine);
            bool playFabSdk = HasType("PlayFab.PlayFabClientAPI");

            if (missing.Count > 0)
            {
                string list = "- " + string.Join("\n- ", missing);
                Debug.LogError("[PhotonVR] Photon VR Plus needs these before it can compile:\n" + list +
                               "\nInstall them, then restart Unity if the errors don't clear. Links: PUN 2 " + PunUrl + "  Photon Voice 2 " + VoiceUrl);

                // Once per editor session unless you ask for it.
                if (fromMenu || !SessionState.GetBool(ShownKey, false))
                {
                    SessionState.SetBool(ShownKey, true);
                    int choice = EditorUtility.DisplayDialogComplex("Photon VR Plus - missing dependencies",
                        "Photon VR Plus needs these packages before it can compile:\n\n" + list +
                        "\n\nThe compile errors in the console go away once they're installed.",
                        "Open Photon Voice 2 (includes PUN 2)", "Later", "Open PUN 2");
                    if (choice == 0) Application.OpenURL(VoiceUrl);
                    else if (choice == 2) Application.OpenURL(PunUrl);
                }
            }
            else if (fromMenu && !(playFabDefine && !playFabSdk))
            {
                EditorUtility.DisplayDialog("Photon VR Plus", "PUN 2, Photon Voice 2 and TextMeshPro are installed." +
                    (playFabDefine ? "\nPlayFab integration is on and the PlayFab SDK was found." : "") +
                    "\n\nNext: Tools > PhotonVR > Setup Scene.", "OK");
            }

            // PHOTONVR_PLAYFAB left on after the PlayFab SDK was removed breaks the build. Offer to turn it off.
            if (playFabDefine && !playFabSdk)
            {
                Debug.LogError("[PhotonVR] " + PlayFabDefine + " is set but the PlayFab SDK isn't in the project. " +
                               "Import the PlayFab SDK or turn it off with Tools > PhotonVR > PlayFab > Disable Integration.");
                if (!fromMenu && SessionState.GetBool(ShownKey + ".PlayFab", false)) return;
                SessionState.SetBool(ShownKey + ".PlayFab", true);
                if (EditorUtility.DisplayDialog("Photon VR Plus - PlayFab",
                        "The PlayFab integration is turned on (" + PlayFabDefine + ") but the PlayFab SDK isn't in this project, so it can't compile.\n\nTurn the integration off?",
                        "Turn it off", "Keep it"))
                    RemoveDefine(PlayFabDefine);
            }
        }

        private static bool HasType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            {
                try { return a.GetType(fullName, false) != null; }
                catch (Exception) { return false; }
            });

        private static string GetDefines(BuildTargetGroup group)
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
        }

        private static void SetDefines(BuildTargetGroup group, string defines)
        {
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
#endif
        }

        private static List<string> Split(string defines) =>
            (defines ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();

        private static bool HasDefine(string define) =>
            Split(GetDefines(EditorUserBuildSettings.selectedBuildTargetGroup)).Contains(define);

        private static void RemoveDefine(string define)
        {
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android, EditorUserBuildSettings.selectedBuildTargetGroup }.Distinct())
            {
                List<string> defines = Split(GetDefines(group));
                if (defines.Remove(define))
                    SetDefines(group, string.Join(";", defines));
            }
            Debug.Log("[PhotonVR] " + define + " removed. Unity will recompile.");
        }
    }
}
