#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

using Photon.Pun;
using Photon.Voice.Unity;
using Photon.VR.Player;

namespace Photon.VR
{
    // Tools > PhotonVR: scene setup, the settings asset and a setup validator.
    public static class PhotonVRSetupMenu
    {
        public const string DefaultRoot = "Assets/PhotonVR";
        public const string PlayerPrefabPath = DefaultRoot + "/Resources/PhotonVR/Player.prefab";
        // Used when the default prefab lives in a read-only UPM package.
        public const string CustomPlayerPrefabPath = DefaultRoot + "/Resources/PhotonVR/PlayerCustom.prefab";
        public const string CustomPlayerResourcesName = "PhotonVR/PlayerCustom";
        public const string SettingsPath = DefaultRoot + "/Resources/PhotonVRSettings.asset";
        public const string ManagerPrefabGuid = "9676815e63c6d7376e386ccb32e05881";

        [MenuItem("Tools/PhotonVR/Setup Scene", false, 0)]
        public static void SetupScene()
        {
            // Manager: prefer the shipped drop-in prefab (manager + voice + spawner).
            PhotonVRManager manager = PhotonVRFind.First<PhotonVRManager>();
            if (manager == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(ManagerPrefabGuid));
                GameObject go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : new GameObject("PhotonVRManager");
                Undo.RegisterCreatedObjectUndo(go, "Create PhotonVRManager");
                manager = go.GetComponent<PhotonVRManager>() ?? go.AddComponent<PhotonVRManager>();
            }
            Undo.RecordObject(manager, "Setup PhotonVRManager");
            // Use (or make) Resources/PhotonVRSettings so the IDs live in one project asset, not in every scene.
            if (manager.Settings == null)
                manager.Settings = PhotonVRSettings.LoadDefault() ?? CreateSettingsAsset();
            manager.CheckDefaultValues();
            if (manager.Head == null && Camera.main != null)
                manager.Head = Camera.main.transform;
            EditorUtility.SetDirty(manager);

            // Spawner
            if (PhotonVRFind.First<PlayerSpawner>() == null)
            {
                var go = new GameObject("PhotonVRPlayerSpawner");
                Undo.RegisterCreatedObjectUndo(go, "Create PlayerSpawner");
                go.AddComponent<PlayerSpawner>();
            }

            // Voice
            if (PhotonVRFind.First<PhotonVRVoice>() == null)
            {
                VoiceConnection existing = PhotonVRFind.All<VoiceConnection>()
                    .FirstOrDefault(v => v.GetType().Name == "PunVoiceClient" || v.GetType().Name == "PhotonVoiceNetwork");
                GameObject go = existing != null ? existing.gameObject : new GameObject("PhotonVRVoice");
                if (existing == null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Create PhotonVRVoice");
                    Type client = PhotonVREditorUtil.FindType("Photon.Voice.PUN.PunVoiceClient") ?? PhotonVREditorUtil.FindType("Photon.Voice.PUN.PhotonVoiceNetwork");
                    if (client != null)
                        Undo.AddComponent(go, client);
                    else
                        PhotonVRLog.Warn("Photon Voice PUN client type not found - is Photon Voice 2 imported?");
                }
                Recorder recorder = go.GetComponent<Recorder>() ?? Undo.AddComponent<Recorder>(go);
                PhotonVRVoice voice = Undo.AddComponent<PhotonVRVoice>(go);
                voice.Recorder = recorder;
                VoiceConnection vc = go.GetComponent<VoiceConnection>();
                if (vc != null) vc.PrimaryRecorder = recorder;
            }

            // Player prefab
            if (Resources.Load<GameObject>("PhotonVR/Player") == null)
            {
                if (EditorUtility.DisplayDialog("PhotonVR", "No player prefab found at Resources/PhotonVR/Player. Create one now?", "Create", "Skip"))
                    CreatePlayerPrefab();
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = manager.gameObject;

            // The one step we can't do for you: the App IDs from dashboard.photonengine.com.
            if (!manager.HasAnyAppId)
            {
                if (EditorUtility.DisplayDialog("Photon VR Plus",
                        "The scene is set up. Last step: paste your Photon App ID and Voice App ID from dashboard.photonengine.com.\n\n" +
                        "They're saved encrypted to Resources/PhotonVRSettings.", "Enter App IDs", "Later"))
                    PhotonVRAppIdWindow.Open();
                ValidateSetup();
                return;
            }
            ValidateSetup(true);
        }

        [MenuItem("Tools/PhotonVR/Create Settings Asset", false, 20)]
        public static PhotonVRSettings CreateSettingsAsset()
        {
            var existing = PhotonVRSettings.LoadDefault();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return existing;
            }

            PhotonVREditorUtil.EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            var settings = ScriptableObject.CreateInstance<PhotonVRSettings>();
            // Encrypted through the property setters.
            if (PhotonNetwork.PhotonServerSettings != null)
            {
                settings.AppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;
                settings.VoiceAppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice;
            }
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            PhotonVRLog.Info("Created " + SettingsPath + " - set your IDs with Tools > PhotonVR > Set App IDs.", settings);
            return settings;
        }

        public static GameObject CreatePlayerPrefab() => PhotonVRPrefabBuilder.CreatePlayerPrefab();
        public static void CopyDefaultPlayerPrefab() => PhotonVRPrefabBuilder.CopyDefaultPlayerPrefab();
        public static GameObject BuildPlayer() => PhotonVRPrefabBuilder.BuildPlayer();

        [MenuItem("Tools/PhotonVR/Validate Setup", false, 30)]
        private static void ValidateSetupMenu() => ValidateSetup(true);

        public static void ValidateSetup() => ValidateSetup(false);

        /// <summary>Checks the open scene and project; logs every problem and, with <paramref name="showDialog"/>, sums them up in a dialog.</summary>
        public static int ValidateSetup(bool showDialog)
        {
            int problems = 0;
            var summary = new System.Text.StringBuilder();
            void Problem(string msg)
            {
                problems++;
                PhotonVRLog.Warn(msg);
                if (problems <= 8) summary.Append("- ").Append(msg.Split('\n')[0]).Append('\n');
            }

            var manager = PhotonVRFind.First<PhotonVRManager>();
            if (manager == null) Problem("No PhotonVRManager in the scene.");
            else
            {
                if (!manager.HasAnyAppId) Problem("No App ID set (Tools > PhotonVR > Set App IDs).");
                if (!manager.HasAnyVoiceAppId) Problem("No Voice App ID set - voice will not connect.");
                if (manager.Head == null && Camera.main == null) Problem("No Head assigned and no camera tagged MainCamera.");
                if (manager.LeftHand == null) Problem("LeftHand is not assigned on PhotonVRManager.");
                if (manager.RightHand == null) Problem("RightHand is not assigned on PhotonVRManager.");
            }
            if (PhotonVRAppIdWindow.HasPlaintextInServerSettings())
                Problem("PhotonServerSettings contains plaintext App IDs (Tools > PhotonVR > Move App IDs Out Of PhotonServerSettings).");
            if (PhotonVRFind.First<PlayerSpawner>() == null) Problem("No PlayerSpawner in the scene.");
            if (PhotonVRFind.First<PhotonVRVoice>() == null) Problem("No PhotonVRVoice in the scene (no microphone transmission).");

            var prefabs = Resources.LoadAll<GameObject>("PhotonVR").Where(g => g.name == "Player" && g.GetComponent<PhotonVRPlayer>() != null).ToArray();
            if (prefabs.Length == 0) Problem("No Resources/PhotonVR/Player prefab (Tools > PhotonVR > Prefabs > Create Player Prefab).");
            if (prefabs.Length > 1) Problem("More than one Resources/PhotonVR/Player prefab - Unity picks one at random. Delete the one you do not use:\n" +
                                            string.Join("\n", prefabs.Select(AssetDatabase.GetAssetPath)));
            foreach (var p in prefabs)
            {
                var player = p.GetComponent<PhotonVRPlayer>();
                if (p.GetComponent<PhotonVRPoseSync>() == null)
                    Problem($"{AssetDatabase.GetAssetPath(p)}: no PhotonVRPoseSync (added at runtime, but add it to the prefab to tune it).");
                foreach (string slot in PhotonVRCosmeticSlots.All)
                    if (player.CosmeticSlots == null || !player.CosmeticSlots.Any(s => s != null && s.SlotName == slot && s.Object != null))
                        Problem($"{AssetDatabase.GetAssetPath(p)}: cosmetic slot '{slot}' is missing or has no Object.");
            }

            if (Resources.Load("TMP Settings") == null)
                Problem("TextMeshPro Essential Resources aren't imported, so nameplates show no text (Window > TextMeshPro > Import TMP Essential Resources).");

            if (problems == 0) PhotonVRLog.Info("Setup looks good.");
            if (showDialog)
            {
                if (problems == 0)
                    EditorUtility.DisplayDialog("Photon VR Plus", "Setup looks good. Press Play to connect.", "OK");
                else
                    EditorUtility.DisplayDialog("Photon VR Plus", problems + " thing(s) to fix (details in the Console):\n\n" + summary +
                                                (problems > 8 ? "- ...\n" : ""), "OK");
            }
            return problems;
        }
    }
}
#endif
