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

        private const string Credit =
            "This package was made by Quick. You can find me at discord.gg/uglymonke.\n" +
            "You're welcome to make stuff with it and edit it however you'd like. I tried to keep the API similar to " +
            "the original PhotonVR so it stays cross-compatible.";

        public override void OnInspectorGUI()
        {
            if (logo == null)
                logo = Resources.Load<Texture2D>("PhotonVR/Assets/PhotonVRWide");
            if (logo != null)
                GUILayout.Label(new GUIContent { image = logo }, GUILayout.MaxHeight(80));

            EditorGUILayout.HelpBox(Credit, MessageType.None);

            var manager = (PhotonVRManager)target;

            bool editable = !Application.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() == null && !EditorUtility.IsPersistent(manager);
            bool missing = manager.Head == null || manager.LeftHand == null || manager.RightHand == null || !manager.HasAnyAppId;
            if (missing && editable)
            {
                Undo.RecordObject(manager, "PhotonVR default values");
                manager.CheckDefaultValues();
            }

            DrawValidation(manager);

            base.OnInspectorGUI();
            GUILayout.Space(10);

            if (!Application.isPlaying)
            {
                if (GUILayout.Button(new GUIContent("Set App IDs...", "Enter the IDs through a masked field; they are saved encrypted")))
                    PhotonVRAppIdWindow.Open();
                if (GUILayout.Button(new GUIContent("Setup Scene", "Adds the spawner + voice objects and assigns your rig")))
                    PhotonVRSetupMenu.SetupScene();
                if (GUILayout.Button(new GUIContent("Open Photon Dashboard", "Get your PUN and Voice App IDs")))
                    Application.OpenURL("https://dashboard.photonengine.com");
                return;
            }

            DrawRuntime(manager);
            Repaint();
        }

        private static void DrawRuntime(PhotonVRManager manager)
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            GUILayout.Label($"State: {PhotonVRManager.GetConnectionState()}");
            if (manager.IsMemoryHardened)
                GUILayout.Label("Memory hardening: on");

            if (!PhotonNetwork.IsConnected)
            {
                if (GUILayout.Button("Connect"))
                    PhotonVRManager.Connect();
                return;
            }

            GUILayout.Label($"Ping: {PhotonNetwork.GetPing()}");
            GUILayout.Label($"Region: {PhotonNetwork.CloudRegion}");
            if (PhotonNetwork.InRoom)
            {
                var room = PhotonNetwork.CurrentRoom;
                GUILayout.Label($"Room: {room.Name} ({room.PlayerCount}/{room.MaxPlayers})");
                if (GUILayout.Button("Leave Room"))
                    PhotonVRManager.LeaveRoom();
            }
            else
            {
                GUILayout.Label("Room: not in a room");
                if (GUILayout.Button(new GUIContent("Join", "Join a random public room in the default queue")))
                    PhotonVRManager.JoinRandomRoom(manager.DefaultQueue, manager.DefaultRoomLimit);
            }

            if (GUILayout.Button("Disconnect"))
                manager.Disconnect();
        }

        private static void DrawValidation(PhotonVRManager manager)
        {
            if (!manager.HasAnyAppId)
                EditorGUILayout.HelpBox("No App ID set. Use Set App IDs... (get one at dashboard.photonengine.com).", MessageType.Error);
            if (!manager.HasAnyVoiceAppId)
                EditorGUILayout.HelpBox("No Voice App ID set. Voice chat will be disabled.", MessageType.Warning);
            if (PhotonVRAppIdWindow.HasPlaintextInServerSettings())
            {
                EditorGUILayout.HelpBox("PhotonServerSettings has plaintext App IDs that will ship in your build.", MessageType.Warning);
                if (GUILayout.Button("Move them into the encrypted settings"))
                    PhotonVRAppIdWindow.MoveFromServerSettings(manager.Settings != null ? (Object)manager.Settings : PhotonVRSetupMenu.CreateSettingsAsset());
            }
            if (manager.LeftHand == null || manager.RightHand == null)
                EditorGUILayout.HelpBox("Left/Right hand not assigned - drag your controllers in.", MessageType.Warning);
            if (Object.FindObjectOfType<PlayerSpawner>() == null)
                EditorGUILayout.HelpBox("No PlayerSpawner in the scene - nobody will be spawned. Use Setup Scene.", MessageType.Warning);
            if (Object.FindObjectOfType<PhotonVRVoice>() == null)
                EditorGUILayout.HelpBox("No PhotonVRVoice in the scene - your microphone will not transmit. Use Setup Scene.", MessageType.Info);
        }
    }
}
#endif
