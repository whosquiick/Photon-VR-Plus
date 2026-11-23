#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

using Photon.Pun;
using Photon.Realtime;

namespace Photon.VR
{
    // Tools > PhotonVR > Set App IDs. Writes encrypted IDs to the settings asset (or the scene's manager)
    // and can move plaintext IDs out of PhotonServerSettings.
    public class PhotonVRAppIdWindow : EditorWindow
    {
        private enum Target { SettingsAsset, SceneManager }

        private Target target = Target.SettingsAsset;
        private string appId = "";
        private string voiceAppId = "";

        [MenuItem("Tools/PhotonVR/Set App IDs...", false, 21)]
        public static void Open()
        {
            var window = GetWindow<PhotonVRAppIdWindow>(true, "Photon VR Plus - App IDs");
            window.minSize = new Vector2(420, 260);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("IDs are stored encrypted, never as plain serialized text. This is obfuscation, " +
                                    "not real security - lock your app down in the Photon dashboard too (README > Protecting your App IDs).",
                                    MessageType.Info);

            target = (Target)EditorGUILayout.EnumPopup("Save to", target);
            Object destination = ResolveTarget(false);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Object", destination, typeof(Object), true);

            if (destination != null)
            {
                var so = new SerializedObject(destination);
                EditorGUILayout.LabelField("Current App ID", Current(so, "appId"));
                EditorGUILayout.LabelField("Current Voice App ID", Current(so, "voiceAppId"));
            }

            EditorGUILayout.Space();
            appId = EditorGUILayout.PasswordField("New App ID", appId);
            voiceAppId = EditorGUILayout.PasswordField("New Voice App ID", voiceAppId);
            EditorGUILayout.LabelField("Leave a field empty to keep the current value.", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(appId) && string.IsNullOrEmpty(voiceAppId)))
            {
                if (GUILayout.Button("Encrypt and Save"))
                {
                    Save(ResolveTarget(true), appId, voiceAppId);
                    appId = voiceAppId = "";
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.Space();
            if (HasPlaintextInServerSettings())
            {
                EditorGUILayout.HelpBox("PhotonServerSettings still contains plaintext App IDs that ship with your build.", MessageType.Warning);
                if (GUILayout.Button("Move IDs from PhotonServerSettings (and blank them there)"))
                    MoveFromServerSettings(ResolveTarget(true));
            }
        }

        private Object ResolveTarget(bool create)
        {
            if (target == Target.SceneManager)
                return FindObjectOfType<PhotonVRManager>();
            PhotonVRSettings settings = PhotonVRSettings.LoadDefault();
            if (settings == null && create)
                settings = PhotonVRSetupMenu.CreateSettingsAsset();
            return settings;
        }

        private static string Current(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            return p == null ? "-" : PhotonVRProtectedStringDrawer.Mask(p.FindPropertyRelative("cipher").stringValue, p.FindPropertyRelative("salt").stringValue);
        }

        private static void Save(Object destination, string newAppId, string newVoiceAppId)
        {
            if (destination == null)
            {
                PhotonVRLog.Warn("Nothing to save to - add a PhotonVRManager to the scene or pick the settings asset.");
                return;
            }
            var so = new SerializedObject(destination);
            if (!string.IsNullOrEmpty(newAppId))
                PhotonVRProtectedStringDrawer.Write(so.FindProperty("appId"), newAppId);
            if (!string.IsNullOrEmpty(newVoiceAppId))
                PhotonVRProtectedStringDrawer.Write(so.FindProperty("voiceAppId"), newVoiceAppId);
            EditorUtility.SetDirty(destination);
            AssetDatabase.SaveAssets();
            PhotonVRLog.Info("Saved encrypted App IDs to " + destination.name + ".", destination);
        }

        public static bool HasPlaintextInServerSettings()
        {
            ServerSettings server = PhotonNetwork.PhotonServerSettings;
            return server != null && server.AppSettings != null &&
                   (!string.IsNullOrEmpty(server.AppSettings.AppIdRealtime) || !string.IsNullOrEmpty(server.AppSettings.AppIdVoice));
        }

        [MenuItem("Tools/PhotonVR/Move App IDs Out Of PhotonServerSettings", false, 22)]
        public static void MoveFromServerSettingsMenu() => MoveFromServerSettings(PhotonVRSetupMenu.CreateSettingsAsset());

        public static void MoveFromServerSettings(Object destination)
        {
            ServerSettings server = PhotonNetwork.PhotonServerSettings;
            if (server == null || destination == null) return;
            AppSettings app = server.AppSettings;

            Save(destination, app.AppIdRealtime, app.AppIdVoice);
            Undo.RecordObject(server, "Blank PhotonServerSettings App IDs");
            app.AppIdRealtime = string.Empty;
            app.AppIdVoice = string.Empty;
            EditorUtility.SetDirty(server);
            AssetDatabase.SaveAssets();
            PhotonVRLog.Info("App IDs moved into " + destination.name + " (encrypted) and cleared from PhotonServerSettings.", destination);
        }
    }
}
#endif
