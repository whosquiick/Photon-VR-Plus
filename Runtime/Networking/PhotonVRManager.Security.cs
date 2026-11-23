using System;
using System.Text;

using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.Unity;

namespace Photon.VR
{
    // Optional memory hardening for the App IDs (HardenMemory on the manager or settings asset).
    //
    // Off: IDs stay encrypted at rest and are decrypted into a fresh AppSettings copy at connect time.
    // On:  IDs live in a session-keyed vault, the at-rest copies are dropped from memory in player builds, and
    //      Photon's own copies (LoadBalancingClient.AppId, the voice client's Settings) are blanked whenever the
    //      clients are idle on a server. They are put back right before Photon switches servers or reconnects,
    //      which is the only time it reads them.
    public partial class PhotonVRManager
    {
        private PhotonVRIdVault idVault;
        private VoiceConnection hookedVoice;

        public bool IsMemoryHardened => idVault != null;

        private bool WantsMemoryHardening => HardenMemory || (Settings != null && Settings.HardenMemory);

        private void InitMemoryHardening()
        {
            if (!WantsMemoryHardening || idVault != null)
                return;

            idVault = new PhotonVRIdVault();
            idVault.Store(PhotonVRAppIdKind.Realtime, TakeAtRestBytes(PhotonVRAppIdKind.Realtime));
            idVault.Store(PhotonVRAppIdKind.Voice, TakeAtRestBytes(PhotonVRAppIdKind.Voice));

#if !UNITY_EDITOR
            // In a player the loaded assets are throwaway copies, so drop the at-rest ciphertext too.
            // (Skipped in the editor, where changing them would leak into your project files.)
            appId.ForgetInMemory();
            voiceAppId.ForgetInMemory();
            if (Settings != null) Settings.ForgetIdsInMemory();
            AppSettings shared = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
            if (shared != null)
                shared.AppIdRealtime = shared.AppIdVoice = string.Empty;
#endif
            CheckIdFormat(PhotonVRAppIdKind.Realtime);
            CheckIdFormat(PhotonVRAppIdKind.Voice);
        }

        // Settings asset wins over the manager fields; PhotonServerSettings is the last fallback.
        private byte[] TakeAtRestBytes(PhotonVRAppIdKind kind)
        {
            byte[] bytes = Settings != null ? Settings.GetIdBytes(kind) : null;
            if (bytes == null)
                bytes = kind == PhotonVRAppIdKind.Realtime ? appId.GetBytes() : voiceAppId.GetBytes();
            if (bytes == null && PhotonNetwork.PhotonServerSettings != null)
            {
                AppSettings shared = PhotonNetwork.PhotonServerSettings.AppSettings;
                string fallback = kind == PhotonVRAppIdKind.Realtime ? shared.AppIdRealtime : shared.AppIdVoice;
                if (!string.IsNullOrEmpty(fallback)) bytes = Encoding.UTF8.GetBytes(fallback);
            }
            return bytes;
        }

        private string ResolveId(PhotonVRAppIdKind kind)
        {
            if (idVault != null)
                return idVault.Reveal(kind);

            bool realtime = kind == PhotonVRAppIdKind.Realtime;
            if (Settings != null && (realtime ? Settings.HasAppId : Settings.HasVoiceAppId))
                return realtime ? Settings.AppId : Settings.VoiceAppId;
            return realtime ? appId.Get() : voiceAppId.Get();
        }

        private void SetId(PhotonVRAppIdKind kind, string value)
        {
            if (idVault != null)
            {
                idVault.Store(kind, value);
#if UNITY_EDITOR
                (kind == PhotonVRAppIdKind.Realtime ? appId : voiceAppId).Set(value);
#endif
                return;
            }
            (kind == PhotonVRAppIdKind.Realtime ? appId : voiceAppId).Set(value);
        }

        // Photon App IDs are GUIDs. Anything else usually means a typo or a tampered asset.
        private void CheckIdFormat(PhotonVRAppIdKind kind)
        {
            string id = ResolveId(kind);
            if (!string.IsNullOrEmpty(id) && !Guid.TryParse(id, out _))
                PhotonVRLog.Warn($"The {kind} App ID does not look like a Photon App ID (expected a GUID).", this);
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (WantsMemoryHardening)
                PhotonNetwork.NetworkingClient.StateChanged += OnPunStateChanged;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.NetworkingClient.StateChanged -= OnPunStateChanged;
            UnhookVoice();
        }

        private void HookVoice()
        {
            if (idVault == null || hookedVoice != null) return;
            VoiceConnection vc = PhotonVRVoice.Instance != null ? PhotonVRVoice.Instance.Connection : null;
            if (vc == null || vc.Client == null) return;
            hookedVoice = vc;
            vc.Client.StateChanged += OnVoiceStateChanged;
        }

        private void UnhookVoice()
        {
            if (hookedVoice != null && hookedVoice.Client != null)
                hookedVoice.Client.StateChanged -= OnVoiceStateChanged;
            hookedVoice = null;
        }

        private void OnPunStateChanged(ClientState previous, ClientState next)
        {
            if (idVault == null) return;
            switch (next)
            {
                case ClientState.ConnectedToMasterServer:
                case ClientState.JoinedLobby:
                case ClientState.Joined:
                    PhotonNetwork.NetworkingClient.AppId = string.Empty;
                    break;

                // Photon reconnects to the next server with LoadBalancingClient.AppId right after these.
                // Disconnected is included so a plain PhotonNetwork.Reconnect() keeps working.
                case ClientState.Disconnected:
                case ClientState.DisconnectingFromNameServer:
                case ClientState.DisconnectingFromMasterServer:
                case ClientState.DisconnectingFromGameServer:
                    RestorePunAppId();
                    break;

                // Voice follows PUN into the room and connects with its Settings once PUN has joined.
                case ClientState.ConnectingToGameServer:
                    HookVoice();
                    PhotonVRVoice.RestoreVoiceAppId(idVault.Reveal(PhotonVRAppIdKind.Voice));
                    break;
            }
        }

        private void OnVoiceStateChanged(ClientState previous, ClientState next)
        {
            if (idVault == null || hookedVoice == null) return;
            switch (next)
            {
                case ClientState.Joined:
                    hookedVoice.Client.AppId = string.Empty;
                    PhotonVRVoice.ScrubAppSettings();
                    break;
                case ClientState.DisconnectingFromNameServer:
                case ClientState.DisconnectingFromMasterServer:
                case ClientState.DisconnectingFromGameServer:
                    hookedVoice.Client.AppId = idVault.Reveal(PhotonVRAppIdKind.Voice);
                    break;
                case ClientState.Disconnected:
                    // Voice retries on its own while PUN stays in the room.
                    if (PhotonNetwork.InRoom)
                        PhotonVRVoice.RestoreVoiceAppId(idVault.Reveal(PhotonVRAppIdKind.Voice));
                    break;
            }
        }

        private void RestorePunAppId()
        {
            if (idVault != null)
                PhotonNetwork.NetworkingClient.AppId = idVault.Reveal(PhotonVRAppIdKind.Realtime);
        }
    }
}
