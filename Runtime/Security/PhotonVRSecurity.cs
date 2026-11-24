using System;
using System.Collections.Generic;

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace Photon.VR
{
    public enum PhotonVRViolationType
    {
        MasterTakeover,
        PropertyTamper,
        KickAttempt,
        NotOwner,
        RateLimit,
        BadData,
        BlockedName,
        IdentityRejected,
        Reported
    }

    public struct PhotonVRViolation
    {
        /// <summary>The player responsible, or null when Photon does not tell us who it was.</summary>
        public Photon.Realtime.Player Offender;
        public PhotonVRViolationType Type;
        public string Detail;
        public double Time;

        public override string ToString() =>
            $"{Type} by {(Offender != null ? $"#{Offender.ActorNumber} {Offender.NickName}" : "unknown")}: {Detail}";
    }

    /// <summary>
    /// Client-side guard rails for PUN rooms: who may act as master, who may kick, whose profile data is trusted,
    /// and who owns what. Every client runs these checks for itself, so a cheater's own client is not stopped,
    /// but the honest clients ignore what the cheater sends. See README > Security for what this can and cannot do.
    /// </summary>
    public static class PhotonVRSecurity
    {
        // Custom event codes (PUN uses 200+ internally).
        public const byte ProfileEvent = 180;
        public const byte MasterHandoffEvent = 181;
        public const byte KickEvent = 182;

        /// <summary>Optional backend authority. Null = built-in rules only.</summary>
        public static PhotonVRAuthority Authority;

        /// <summary>Raised for every detected violation (also forwarded to <see cref="Authority"/>).</summary>
        public static event Action<PhotonVRViolation> ViolationDetected;

        /// <summary>On the legitimate master: kick a player after this many violations (0 = never kick automatically).</summary>
        public static int KickAfterViolations = 0;

        /// <summary>Log violations to the console.</summary>
        public static bool LogViolations = true;

        internal static readonly PhotonVRRateLimiterTable Limits = new PhotonVRRateLimiterTable();
        private static readonly Dictionary<int, int> violationCounts = new Dictionary<int, int>();
        private static SecurityCallbacks callbacks;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // PUN may also have kept its callback list, so take ours out before forgetting it.
            if (callbacks != null) { try { PhotonNetwork.RemoveCallbackTarget(callbacks); } catch (Exception) { } }
            callbacks = null;
            Authority = null;
            ViolationDetected = null;
            KickAfterViolations = 0;
            LogViolations = true;
            violationCounts.Clear();
            Limits.Clear();
            ReportThrottle.Clear();
        }

        /// <summary>Registers the Photon callbacks. Called by PhotonVRManager; safe to call more than once.</summary>
        public static void Initialize()
        {
            if (callbacks != null) return;
            callbacks = new SecurityCallbacks();
            PhotonNetwork.AddCallbackTarget(callbacks);
            // PUN's own CloseConnection is off by default; keep it that way so only our validated kick works.
            PhotonNetwork.EnableCloseConnection = false;
        }

        public static void Shutdown()
        {
            if (callbacks == null) return;
            PhotonNetwork.RemoveCallbackTarget(callbacks);
            callbacks = null;
        }

        public static int ViolationCount(Photon.Realtime.Player player) =>
            player != null && violationCounts.TryGetValue(player.ActorNumber, out int n) ? n : 0;

        /// <summary>Records a violation. Use it from your own validation code as well.</summary>
        public static void Report(Photon.Realtime.Player offender, PhotonVRViolationType type, string detail)
        {
            var v = new PhotonVRViolation { Offender = offender, Type = type, Detail = detail, Time = PhotonNetwork.Time };
            if (offender != null)
            {
                violationCounts.TryGetValue(offender.ActorNumber, out int n);
                violationCounts[offender.ActorNumber] = ++n;
                if (KickAfterViolations > 0 && n >= KickAfterViolations && PhotonVRMasterClient.IsLocalLegitimateMaster && !offender.IsLocal)
                    PhotonVRModeration.Kick(offender, "Too many violations");
            }

            if (LogViolations) PhotonVRLog.Warn("Security: " + v);
            try { Authority?.OnViolation(v); } catch (Exception e) { PhotonVRLog.Error("Authority.OnViolation threw: " + e); }
            ViolationDetected?.Invoke(v);
        }

        /// <summary>Lets players report someone; goes to <see cref="ViolationDetected"/> / your Authority.</summary>
        public static void ReportPlayer(Photon.Realtime.Player player, string reason) => Report(player, PhotonVRViolationType.Reported, reason);

        /// <summary>Per-actor rate limit shared by the security layer. Reports when exceeded.</summary>
        public static bool CheckRate(Photon.Realtime.Player sender, string key, float perSecond, float burst)
        {
            if (sender == null || sender.IsLocal) return true;
            if (Limits.TryConsume(sender.ActorNumber, key, perSecond, burst)) return true;
            // One report per sender and key every few seconds, not one per dropped packet.
            if (ReportThrottle.TryConsume(sender.ActorNumber, key, 0.2f, 1f))
                Report(sender, PhotonVRViolationType.RateLimit, key);
            return false;
        }

        private static readonly PhotonVRRateLimiterTable ReportThrottle = new PhotonVRRateLimiterTable();

        internal static void ForgetPlayer(Photon.Realtime.Player player)
        {
            if (player == null) return;
            violationCounts.Remove(player.ActorNumber);
            Limits.Forget(player.ActorNumber);
            ReportThrottle.Forget(player.ActorNumber);
        }

        internal static void ResetRoom()
        {
            violationCounts.Clear();
            Limits.Clear();
            ReportThrottle.Clear();
        }

        private sealed class SecurityCallbacks : IOnEventCallback, IInRoomCallbacks, IMatchmakingCallbacks
        {
            public void OnEvent(EventData e)
            {
                switch (e.Code)
                {
                    case ProfileEvent: PhotonVRProfiles.HandleEvent(e); break;
                    case MasterHandoffEvent: PhotonVRMasterClient.HandleHandoffEvent(e); break;
                    case KickEvent: PhotonVRModeration.HandleKickEvent(e); break;
                    case EventCode.PropertiesChanged: PhotonVRProfiles.HandlePropertiesChanged(e); break;
                }
            }

            public void OnJoinedRoom()
            {
                ResetRoom();
                PhotonVRMasterClient.OnJoinedRoom();
                PhotonVRProfiles.OnJoinedRoom();
            }

            public void OnLeftRoom()
            {
                ResetRoom();
                PhotonVRMasterClient.OnLeftRoom();
                PhotonVRProfiles.Clear();
            }

            public void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => PhotonVRModeration.OnPlayerEnteredRoom(newPlayer);

            public void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
            {
                if (otherPlayer == null) return;
                PhotonVRProfiles.Remove(otherPlayer.ActorNumber);
                ForgetPlayer(otherPlayer);
            }

            public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
            // Property changes are handled from the raw event in OnEvent, which carries who wrote them.
            public void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps) { }

            public void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
            {
                PhotonVRMasterClient.OnMasterClientSwitched(newMasterClient);
                PhotonVRProfiles.OnMasterClientSwitched();
            }

            public void OnFriendListUpdate(List<FriendInfo> friendList) { }
            public void OnCreatedRoom() { }
            public void OnCreateRoomFailed(short returnCode, string message) { }
            public void OnJoinRoomFailed(short returnCode, string message) { }
            public void OnJoinRandomFailed(short returnCode, string message) { }
        }
    }
}
