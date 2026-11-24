using System;
using System.Collections;

using UnityEngine;

using Photon.Pun;
using Photon.Realtime;

namespace Photon.VR
{
    public partial class PhotonVRManager
    {
        // Work queued until we are fully disconnected (ChangeServers).
        private Action pendingOnDisconnected;
        private bool intentionalDisconnect;
        // True between Connect() and the first OnConnectedToMaster, so JoinRoomOnConnect fires once per Connect.
        private bool autoJoinPending;
        private int reconnectAttempts;
        private Coroutine reconnectRoutine;

        /// <summary>
        /// Applies region, game version and send/serialization rates (manager fields, overridden by the settings asset).
        /// The App IDs are <b>not</b> written to the shared PhotonServerSettings asset any more: they are decrypted
        /// into a private AppSettings copy only at connect time (see <see cref="Connect"/>). Called automatically by Connect.
        /// </summary>
        public void ApplyConnectionSettings()
        {
            if (Settings != null)
            {
                if (!string.IsNullOrEmpty(Settings.Region)) Region = Settings.Region;
                if (!string.IsNullOrEmpty(Settings.GameVersion)) GameVersion = Settings.GameVersion;
                if (Settings.SendRate > 0) SendRate = Settings.SendRate;
                if (Settings.SerializationRate > 0) SerializationRate = Settings.SerializationRate;
            }

            int send = SendRate, serialize = SerializationRate;
            if (PlatformRates != null)
                foreach (PhotonVRPlatformRates rates in PlatformRates)
                    if (rates != null && rates.Platform == Application.platform)
                    {
                        if (rates.SendRate > 0) send = rates.SendRate;
                        if (rates.SerializationRate > 0) serialize = rates.SerializationRate;
                    }

            if (send > 0) PhotonNetwork.SendRate = send;
            if (serialize > 0) PhotonNetwork.SerializationRate = Mathf.Min(serialize, PhotonNetwork.SendRate);
        }

        /// <summary>True when an App ID is available (settings asset or manager) without decrypting it.</summary>
        public bool HasAnyAppId => idVault != null ? idVault.Has(PhotonVRAppIdKind.Realtime) : (Settings != null && Settings.HasAppId) || HasAppId;

        /// <summary>True when a Voice App ID is available (settings asset or manager) without decrypting it.</summary>
        public bool HasAnyVoiceAppId => idVault != null ? idVault.Has(PhotonVRAppIdKind.Voice) : (Settings != null && Settings.HasVoiceAppId) || HasVoiceAppId;

        // Builds a fresh AppSettings for this connection: a copy of PhotonServerSettings with the decrypted IDs,
        // region and version. The copy is handed to Photon and never stored in a serialized field.
        private AppSettings BuildAppSettings()
        {
            var app = new AppSettings();
            if (PhotonNetwork.PhotonServerSettings != null && PhotonNetwork.PhotonServerSettings.AppSettings != null)
                PhotonNetwork.PhotonServerSettings.AppSettings.CopyTo(app);

            app.AppIdRealtime = ResolveId(PhotonVRAppIdKind.Realtime);
            app.AppIdVoice = ResolveId(PhotonVRAppIdKind.Voice);
            app.FixedRegion = NormalizeRegion(Region);
            if (!string.IsNullOrEmpty(GameVersion))
                app.AppVersion = GameVersion;
            return app;
        }

        /// <summary>Connects to Photon using the specified AppId and VoiceAppId</summary>
        public static bool Connect() => ConnectInternal(null);

        /// <summary>Connects to Photon using the specified AppId and VoiceAppId with authentication</summary>
        public static bool ConnectAuthenticated(string username, string token)
        {
            AuthenticationValues authentication = new AuthenticationValues { AuthType = CustomAuthenticationType.Custom };
            authentication.AddAuthParameter("username", username);
            authentication.AddAuthParameter("token", token);
            return ConnectInternal(authentication);
        }

        /// <summary>Connects with fully custom AuthenticationValues (e.g. a PlayFab ticket with UserId set).</summary>
        public static bool ConnectWithAuthentication(AuthenticationValues authentication) => ConnectInternal(authentication);

        /// <summary>Disconnects from the Photon servers (never triggers auto-reconnect).</summary>
        public void Disconnect()
        {
            intentionalDisconnect = true;
            StopReconnect();
            ClearPendingRoomRequest();
            PhotonNetwork.Disconnect();
        }

        /// <summary>Changes Photon servers</summary>
        public static void ChangeServers(string Id, string VoiceId)
        {
            if (!EnsureManager("ChangeServers")) return;
            ChangeServersInternal(Id, VoiceId, () => Connect());
        }

        /// <summary>Changes Photon servers with authentication</summary>
        public static void ChangeServersAuthenticated(string Id, string VoiceId, string username, string token)
        {
            if (!EnsureManager("ChangeServersAuthenticated")) return;
            ChangeServersInternal(Id, VoiceId, () => ConnectAuthenticated(username, token));
        }

        private static string NormalizeRegion(string region)
        {
            if (string.IsNullOrEmpty(region)) return null;
            region = region.Trim();
            return region.Length == 0 || region.Equals("best", StringComparison.OrdinalIgnoreCase) ? null : region;
        }

        private static bool ConnectInternal(AuthenticationValues auth)
        {
            if (!EnsureManager("Connect"))
                return false;

            PhotonVRManager m = Manager;
            if (PhotonNetwork.IsConnected)
            {
                PhotonVRLog.Info("Already connected.");
                return true;
            }

            m.ApplyConnectionSettings();
            // Decrypted only now, into a private copy that goes straight to Photon.
            AppSettings app = m.BuildAppSettings();

            if (string.IsNullOrEmpty(app.AppIdRealtime))
            {
                PhotonVRLog.Error("Please input an app id (PhotonVRManager or PhotonVRSettings - Tools > PhotonVR > Set App IDs)", m);
                return false;
            }
            if (string.IsNullOrEmpty(app.AppIdVoice))
                PhotonVRLog.Warn("No VoiceAppId set - connecting without voice chat.", m);

            PhotonVRVoice.ApplyAppSettings(app);
            if (m.IsMemoryHardened)
                PhotonVRVoice.ScrubAppSettings(); // restored right before voice needs it

            PhotonNetwork.AuthValues = auth;
            m.intentionalDisconnect = false;
            m.autoJoinPending = m.JoinRoomOnConnect;

            // Name, colour and cosmetics are set before connecting so they arrive together with the join.
            string savedName = SanitizeSavedName(PlayerPrefs.GetString(PhotonVRKeys.PrefsUsername));
            PhotonNetwork.NickName = string.IsNullOrEmpty(savedName)
                ? "Player" + UnityEngine.Random.Range(1000, 9999)
                : savedName;
            acceptedName = PhotonNetwork.NickName;
            m.PushLocalProperties();

            m.SetState(ConnectionState.Connecting);
            bool started = PhotonNetwork.ConnectUsingSettings(app);
            string region = app.FixedRegion ?? "best";
            app.AppIdRealtime = app.AppIdVoice = null;
            app = null;
            if (!started)
            {
                m.SetState(ConnectionState.Disconnected);
                PhotonVRLog.Error("ConnectUsingSettings failed (are you still disconnecting?)", m);
                return false;
            }

            // IDs are deliberately not logged.
            PhotonVRLog.Info($"Connecting - Region: {region}");
            return true;
        }

        private static void ChangeServersInternal(string id, string voiceId, Action connect)
        {
            PhotonVRManager m = Manager;
            m.AppId = id;
            m.VoiceAppId = voiceId;
            // Explicit IDs win over the settings asset from now on.
            m.Settings = null;

            ClientState state = PhotonNetwork.NetworkClientState;
            if (state == ClientState.Disconnected || state == ClientState.PeerCreated)
            {
                connect();
                return;
            }

            // Connecting while still disconnecting fails, so wait for OnDisconnected.
            m.pendingOnDisconnected = connect;
            m.Disconnect();
        }

        private static bool IsRecoverable(DisconnectCause cause)
        {
            switch (cause)
            {
                case DisconnectCause.ClientTimeout:
                case DisconnectCause.ServerTimeout:
                case DisconnectCause.Exception:
                case DisconnectCause.ExceptionOnConnect:
                case DisconnectCause.DisconnectByServerReasonUnknown:
                    return true;
                default:
                    return false;
            }
        }

        private void StartReconnect()
        {
            if (reconnectRoutine != null || !isActiveAndEnabled) return;
            reconnectRoutine = StartCoroutine(ReconnectLoop());
        }

        private void StopReconnect()
        {
            if (reconnectRoutine == null) return;
            StopCoroutine(reconnectRoutine);
            reconnectRoutine = null;
        }

        private IEnumerator ReconnectLoop()
        {
            while (reconnectAttempts < MaxReconnectAttempts && !PhotonNetwork.IsConnected && !intentionalDisconnect)
            {
                float delay = Mathf.Min(30f, Mathf.Max(0.5f, ReconnectDelay) * Mathf.Pow(2f, reconnectAttempts));
                reconnectAttempts++;
                PhotonVRLog.Info($"Reconnecting in {delay:0.#}s (attempt {reconnectAttempts}/{MaxReconnectAttempts})");
                yield return new WaitForSecondsRealtime(delay);

                if (PhotonNetwork.IsConnected || intentionalDisconnect)
                    break;

                SetState(ConnectionState.Connecting);
                RestorePunAppId();
                // Reconnect() reuses the last master server and auth values; fall back to a fresh connect.
                if (!PhotonNetwork.Reconnect())
                {
                    SetState(ConnectionState.Disconnected);
                    ConnectInternal(PhotonNetwork.AuthValues);
                }

                float timeout = 15f;
                while (timeout > 0f && State == ConnectionState.Connecting)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (!PhotonNetwork.IsConnected && reconnectAttempts >= MaxReconnectAttempts)
                PhotonVRLog.Warn("Gave up reconnecting. Call PhotonVRManager.Connect() to try again.");
            reconnectRoutine = null;
        }

        public override void OnConnectedToMaster()
        {
            SetState(ConnectionState.Connected);
            reconnectAttempts = 0;
            PhotonVRLog.Info($"Connected to master ({PhotonNetwork.CloudRegion})");
            PhotonVRVoice.FollowRegion(PhotonNetwork.CloudRegion);

            // Make sure name / colour / cosmetics are current (they are sent when joining a room).
            if (string.IsNullOrEmpty(PhotonNetwork.NickName))
                PhotonNetwork.NickName = AcceptedName;
            PushLocalProperties();

            ConnectedToMaster?.Invoke();
            OnConnectedEvent?.Invoke();

            if (!RunPendingRoomRequest() && autoJoinPending && JoinRoomOnConnect)
                JoinRandomRoom(DefaultQueue, DefaultRoomLimit);
            autoJoinPending = false;
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            base.OnDisconnected(cause);
            bool wasInRoom = State == ConnectionState.InRoom || State == ConnectionState.JoiningRoom;
            SetState(ConnectionState.Disconnected);
            LocalPlayer = null;
            PhotonVRLog.Info($"Disconnected from server: {cause}");

            DisconnectedFromServer?.Invoke(cause);
            OnDisconnectedEvent?.Invoke();

            if (pendingOnDisconnected != null)
            {
                Action a = pendingOnDisconnected;
                pendingOnDisconnected = null;
                intentionalDisconnect = false;
                a();
                return;
            }

            if (!intentionalDisconnect && AutoReconnect && IsRecoverable(cause))
            {
                if (RejoinAfterReconnect && wasInRoom)
                    QueueRejoinLastRoom();
                StartReconnect();
            }
        }
    }
}
