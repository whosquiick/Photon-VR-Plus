using Photon.Pun;
using UnityEngine;

namespace Photon.VR.Samples
{
    /// <summary>Simple OnGUI lobby used by the samples: public rooms, private codes, name, spectator toggle.</summary>
    public class PhotonVRLobbyMenu : MonoBehaviour
    {
        public string Queue = "Default";
        public int MaxPlayers = 10;

        private string code = "";
        private string username = "";
        private string message = "";

        private void OnEnable()
        {
            PhotonVRManager.RoomCodeRejected += OnCodeRejected;
            PhotonVRManager.UsernameRejected += OnNameRejected;
        }

        private void OnDisable()
        {
            PhotonVRManager.RoomCodeRejected -= OnCodeRejected;
            PhotonVRManager.UsernameRejected -= OnNameRejected;
        }

        private void Start() => username = PhotonNetwork.NickName;

        private void OnCodeRejected(string c) => message = "That code isn't allowed.";
        private void OnNameRejected(string n) => message = "That name isn't allowed.";

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 270, 10, 260, 330), GUI.skin.box);
            GUILayout.Label("State: " + PhotonVRManager.GetConnectionState());

            GUILayout.BeginHorizontal();
            username = GUILayout.TextField(username, 24);
            if (GUILayout.Button("Set name", GUILayout.Width(80)))
                message = PhotonVRManager.TrySetUsername(username) ? "Name saved." : message;
            GUILayout.EndHorizontal();

            if (!PhotonNetwork.InRoom)
            {
                if (GUILayout.Button("Join public room"))
                    PhotonVRMatchmaking.JoinPublic(Queue, MaxPlayers);
                if (GUILayout.Button("Create private room"))
                    code = PhotonVRMatchmaking.CreatePrivateRoom(MaxPlayers) ?? code;
                GUILayout.BeginHorizontal();
                code = GUILayout.TextField(code, 32);
                if (GUILayout.Button("Join code", GUILayout.Width(80)) && !PhotonVRMatchmaking.JoinByCode(code))
                    message = "That code isn't allowed.";
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Best region"))
                    PhotonVRMatchmaking.UseBestRegion();
            }
            else
            {
                GUILayout.Label($"Room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
                bool spectating = PhotonVRMatchmaking.IsLocalSpectator;
                if (GUILayout.Button(spectating ? "Stop spectating" : "Spectate"))
                    PhotonVRMatchmaking.SetSpectator(!spectating);
                if (GUILayout.Button(PhotonVRVoice.IsMuted ? "Unmute mic" : "Mute mic"))
                    PhotonVRVoice.ToggleMute();
                if (PhotonVRMasterClient.IsLocalLegitimateMaster && GUILayout.Button("Hand off master"))
                    PhotonVRMasterClient.HandOffToBest();
                if (GUILayout.Button("Leave"))
                    PhotonVRManager.LeaveRoom();
            }

            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            GUILayout.EndArea();
        }
    }
}
