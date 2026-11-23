// Photon VR Plus - a revamp of PhotonVR by fchb1239 (MIT). See LICENSE.md.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.SceneManagement;
#else
using UnityEditor.Experimental.SceneManagement;
#endif

using Photon.Pun;
using Photon.VR.Player;

namespace Photon.VR
{
    [CustomEditor(typeof(PhotonVRManager))]
    public class PhotonVRManagerGUI : Editor
    {
        private Texture2D logo;

        public override void OnInspectorGUI()
        {
            if (logo == null)
                logo = Resources.Load<Texture2D>("PhotonVR/Assets/PhotonVRWide");
            if (logo != null)
                GUILayout.Label(new GUIContent() { image = logo }, GUILayout.MaxHeight(80));

            PhotonVRManager manager = (PhotonVRManager)target;

            bool missing = manager.Head == null || manager.LeftHand == null || manager.RightHand == null ||
                           string.IsNullOrEmpty(manager.AppId) || string.IsNullOrEmpty(manager.VoiceAppId);
            if (missing && !Application.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() == null && !EditorUtility.IsPersistent(manager))
            {
                Undo.RecordObject(manager, "PhotonVR default values");
                manager.CheckDefaultValues();
            }

            DrawValidation(manager);

            base.OnInspectorGUI();
            GUILayout.Space(10);

            if (!Application.isPlaying)
            {
                if (GUILayout.Button(CreateContent("Setup Scene", "Adds the spawner + voice objects and assigns your rig (Tools > PhotonVR > Setup Scene)")))
                    PhotonVRSetupMenu.SetupScene();
                if (GUILayout.Button(CreateContent("Open Photon Dashboard", "Get your PUN and Voice App IDs")))
                    Application.OpenURL("https://dashboard.photonengine.com");
                return;
            }

            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            GUILayout.Label($"State: {PhotonVRManager.GetConnectionState()}");
            if (PhotonNetwork.IsConnected)
            {
                GUILayout.Label($"Ping: {PhotonNetwork.GetPing()}");
                GUILayout.Label($"Region: {PhotonNetwork.CloudRegion}");
                GUILayout.Label($"Room: {(PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name + $" ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})" : "Not in a room")}");
                if (!PhotonNetwork.InRoom)
                {
                    if (GUILayout.Button(CreateContent("Join", "Join a random public lobby in the default queue")))
                        PhotonVRManager.JoinRandomRoom(manager.DefaultQueue, manager.DefaultRoomLimit);
                }
                else if (GUILayout.Button("Leave Room"))
                    PhotonVRManager.LeaveRoom();

                if (GUILayout.Button("Disconnect"))
                    manager.Disconnect();
            }
            else if (GUILayout.Button("Connect"))
                PhotonVRManager.Connect();

            Repaint();
        }

        private static void DrawValidation(PhotonVRManager manager)
        {
            bool hasSettingsIds = manager.Settings != null && !string.IsNullOrEmpty(manager.Settings.AppId);
            if (string.IsNullOrEmpty(manager.AppId) && !hasSettingsIds)
                EditorGUILayout.HelpBox("AppId is empty. Paste your Photon PUN App ID (dashboard.photonengine.com).", MessageType.Error);
            bool hasSettingsVoice = manager.Settings != null && !string.IsNullOrEmpty(manager.Settings.VoiceAppId);
            if (string.IsNullOrEmpty(manager.VoiceAppId) && !hasSettingsVoice)
                EditorGUILayout.HelpBox("VoiceAppId is empty. Voice chat will be disabled.", MessageType.Warning);
            if (manager.LeftHand == null || manager.RightHand == null)
                EditorGUILayout.HelpBox("Left/Right hand not assigned - drag your controllers in.", MessageType.Warning);
            if (Object.FindObjectOfType<PlayerSpawner>() == null)
                EditorGUILayout.HelpBox("No PlayerSpawner in the scene - nobody will be spawned. Use Setup Scene.", MessageType.Warning);
            if (Object.FindObjectOfType<PhotonVRVoice>() == null)
                EditorGUILayout.HelpBox("No PhotonVRVoice in the scene - voice chat will not transmit. Use Setup Scene.", MessageType.Info);
        }

        private GUIContent CreateContent(string text, string tooltip)
        {
            return new GUIContent()
            {
                text = text,
                tooltip = tooltip
            };
        }
    }
}
#endif
