using System.Text;

using Photon.Pun;
using TMPro;
using UnityEngine;

namespace Photon.VR.Debugging
{
    /// <summary>
    /// Network debug readout: ping, region, room, players (master / legitimacy / spectator flags) and the views you own.
    /// Draws with OnGUI on desktop; set WorldText to also show it in VR.
    /// </summary>
    public class PhotonVRDebugOverlay : MonoBehaviour
    {
        public bool ShowOnStart = false;
        public KeyCode ToggleKey = KeyCode.F3;
        [Tooltip("Optional world-space text for headsets.")]
        public TextMeshPro WorldText;
        public float RefreshInterval = 0.5f;
        public bool ShowOwnedViews = true;

        public bool Visible { get; set; }

        private readonly StringBuilder sb = new StringBuilder(512);
        private string text = "";
        private float nextRefresh;
        private bool legacyInput = true;
        private GUIStyle style;

        private void Awake() => Visible = ShowOnStart;

        private void Update()
        {
            if (legacyInput && ToggleKey != KeyCode.None)
            {
                try { if (Input.GetKeyDown(ToggleKey)) Visible = !Visible; }
                catch (System.InvalidOperationException) { legacyInput = false; }
            }
            if (WorldText != null && WorldText.gameObject.activeSelf != Visible) WorldText.gameObject.SetActive(Visible);
            if (!Visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            text = Build();
            if (WorldText != null) WorldText.text = text;
        }

        public string Build()
        {
            sb.Length = 0;
            sb.Append("PhotonVR  ").Append(PhotonNetwork.NetworkClientState).Append('\n');
            if (!PhotonNetwork.IsConnected) return sb.ToString();
            sb.Append("Region ").Append(PhotonNetwork.CloudRegion).Append("   Ping ").Append(PhotonNetwork.GetPing()).Append(" ms\n");
            sb.Append("Rates  send ").Append(PhotonNetwork.SendRate).Append(" / ser ").Append(PhotonNetwork.SerializationRate).Append('\n');
            if (!PhotonNetwork.InRoom) return sb.ToString();

            var room = PhotonNetwork.CurrentRoom;
            sb.Append("Room ").Append(room.Name).Append("  ").Append(room.PlayerCount).Append('/').Append(room.MaxPlayers)
              .Append(room.IsVisible ? "  public" : "  private").Append('\n');
            sb.Append("Master ").Append(PhotonNetwork.MasterClient?.ActorNumber)
              .Append(PhotonVRMasterClient.IsMasterTrusted ? " (trusted)" : " (UNTRUSTED)").Append('\n');

            foreach (var p in PhotonNetwork.PlayerList)
            {
                sb.Append(p.IsLocal ? "> " : "  ").Append(p.ActorNumber).Append(' ').Append(PhotonVRProfiles.DisplayName(p));
                if (p.IsMasterClient) sb.Append(" [M]");
                if (PhotonVRMatchmaking.IsSpectator(p)) sb.Append(" [spec]");
                if (p.IsInactive) sb.Append(" [away]");
                if (PhotonVRMuteList.IsMuted(p)) sb.Append(" [muted]");
                int v = PhotonVRSecurity.ViolationCount(p);
                if (v > 0) sb.Append(" !").Append(v);
                if (p.CustomProperties.TryGetValue(PhotonVRKeys.Ping, out object ping)) sb.Append("  ").Append(ping).Append("ms");
                sb.Append('\n');
            }

            if (ShowOwnedViews)
            {
                int owned = 0;
                foreach (PhotonView view in PhotonNetwork.PhotonViewCollection)
                    if (view.IsMine) owned++;
                sb.Append("Views owned ").Append(owned).Append('\n');
            }
            return sb.ToString();
        }

        private void OnGUI()
        {
            if (!Visible) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };
            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, size.x + 10, size.y + 6), text, style);
        }
    }
}
