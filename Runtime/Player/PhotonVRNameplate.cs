using TMPro;
using UnityEngine;

namespace Photon.VR.Player
{
    /// <summary>
    /// Billboarded name plus a speaking indicator above a remote player. Uses the filtered display name, so
    /// names set by someone else or containing blocked words never show.
    /// </summary>
    public class PhotonVRNameplate : MonoBehaviour
    {
        public PhotonVRPlayer Player;
        [Tooltip("Text to write the name into. Created at runtime when empty.")]
        public TextMeshPro Text;
        [Tooltip("Shown while the player is talking.")]
        public GameObject SpeakingIcon;
        [Tooltip("Shown while you have this player muted.")]
        public GameObject MutedIcon;
        public Vector3 Offset = new Vector3(0f, 0.35f, 0f);
        public float HideBeyond = 25f;
        public bool ShowOnLocalPlayer = false;
        public bool ShowSpectators = false;

        private Transform anchor;
        private Camera cam;
        private string shownName;

        private void Start()
        {
            if (Player == null) Player = GetComponentInParent<PhotonVRPlayer>();
            if (Player == null) { enabled = false; return; }
            anchor = Player.Head != null ? Player.Head : Player.transform;
            if (Text == null)
            {
                var go = new GameObject("Nameplate Text");
                go.transform.SetParent(transform, false);
                Text = go.AddComponent<TextMeshPro>();
                Text.fontSize = 1.2f;
                Text.alignment = TextAlignmentOptions.Center;
                Text.richText = false;
            }
            else
            {
                Text.richText = false;
            }
        }

        private void LateUpdate()
        {
            if (Player == null) return;
            if (cam == null) cam = Camera.main;

            bool visible = (ShowOnLocalPlayer || !Player.IsLocal) && (ShowSpectators || !PhotonVRMatchmaking.IsSpectator(Player.Owner));
            if (visible && cam != null && HideBeyond > 0f)
                visible = (cam.transform.position - anchor.position).sqrMagnitude < HideBeyond * HideBeyond;

            if (Text != null)
            {
                if (Text.gameObject.activeSelf != visible) Text.gameObject.SetActive(visible);
                if (visible && shownName != Player.DisplayName)
                {
                    shownName = Player.DisplayName;
                    Text.text = shownName;
                }
            }
            if (SpeakingIcon != null) SpeakingIcon.SetActive(visible && Player.IsSpeaking);
            if (MutedIcon != null) MutedIcon.SetActive(visible && PhotonVRMuteList.IsMuted(Player.Owner));

            transform.position = anchor.position + Offset;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
        }
    }
}
