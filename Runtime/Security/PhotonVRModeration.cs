using System;
using System.Collections.Generic;

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// Validated kicks and identity checks.
    /// <para>A kick is a custom event the <b>target's</b> client obeys only when it comes from the legitimate master
    /// (and your Authority agrees). PUN's own <c>CloseConnection</c> stays disabled, so a hijacked master or a
    /// modded client cannot disconnect anyone through either route.</para>
    /// </summary>
    public static class PhotonVRModeration
    {
        /// <summary>On the master: kick players whose published name is blocked by the word filter.</summary>
        public static bool KickBlockedNames = false;

        /// <summary>Raised on the kicked client with the reason, right before it leaves the room.</summary>
        public static event Action<string> Kicked;

        private static readonly HashSet<string> bannedUserIds = new HashSet<string>();

        /// <summary>Asks <paramref name="target"/> to leave. Only works on the legitimate master.</summary>
        public static bool Kick(Photon.Realtime.Player target, string reason)
        {
            if (target == null || target.IsLocal) return false;
            if (!PhotonVRMasterClient.IsLocalLegitimateMaster)
            {
                PhotonVRLog.Warn("Only the master client can kick players.");
                return false;
            }
            reason = string.IsNullOrEmpty(reason) ? "Kicked" : reason.Length > 64 ? reason.Substring(0, 64) : reason;
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.KickEvent, new object[] { target.ActorNumber, reason },
                new RaiseEventOptions { TargetActors = new[] { target.ActorNumber } }, SendOptions.SendReliable);
            PhotonVRLog.Info($"Kicked #{target.ActorNumber} ({reason})");
            return true;
        }

        /// <summary>Kicks and remembers the UserId for the rest of this session (persistent bans belong in your backend).</summary>
        public static bool Ban(Photon.Realtime.Player target, string reason)
        {
            if (target != null && !string.IsNullOrEmpty(target.UserId)) bannedUserIds.Add(target.UserId);
            return Kick(target, reason);
        }

        public static bool IsBanned(Photon.Realtime.Player player) => player != null && !string.IsNullOrEmpty(player.UserId) && bannedUserIds.Contains(player.UserId);

        internal static void HandleKickEvent(EventData e)
        {
            Photon.Realtime.Player sender = PhotonNetwork.CurrentRoom?.GetPlayer(e.Sender);
            if (!(e.CustomData is object[] data) || data.Length < 2 || !(data[0] is int target) || target != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            Photon.Realtime.Player master = PhotonVRMasterClient.LegitimateMaster;
            bool fromMaster = sender != null && master != null && sender.ActorNumber == master.ActorNumber;
            string reason = data[1] as string ?? "Kicked";
            if (!fromMaster)
            {
                PhotonVRSecurity.Report(sender, PhotonVRViolationType.KickAttempt, "tried to kick us without being the master");
                return;
            }
            if (PhotonVRSecurity.Authority != null && !PhotonVRSecurity.Authority.ValidateKick(sender, PhotonNetwork.LocalPlayer, reason))
            {
                PhotonVRLog.Warn("Kick refused by the authority: " + reason);
                return;
            }

            PhotonVRLog.Warn("Kicked by the master: " + reason);
            Kicked?.Invoke(reason);
            PhotonNetwork.LeaveRoom(false);
        }

        internal static void OnPlayerEnteredRoom(Photon.Realtime.Player player)
        {
            if (!PhotonVRMasterClient.IsLocalLegitimateMaster || player == null) return;

            if (IsBanned(player))
            {
                Kick(player, "Banned");
                return;
            }
            if (PhotonVRSecurity.Authority != null && !PhotonVRSecurity.Authority.ValidateIdentity(player))
            {
                PhotonVRSecurity.Report(player, PhotonVRViolationType.IdentityRejected, "identity check failed");
                Kick(player, "Identity check failed");
                return;
            }
            // Two connections with the same UserId means someone is impersonating (PUN rejects this by default
            // with CheckUserOnJoin; this catches setups where it was turned off).
            if (!string.IsNullOrEmpty(player.UserId))
                foreach (Photon.Realtime.Player other in PhotonNetwork.PlayerList)
                    if (other != player && other.UserId == player.UserId && !other.IsInactive)
                    {
                        PhotonVRSecurity.Report(player, PhotonVRViolationType.IdentityRejected, "duplicate UserId");
                        Kick(player, "Duplicate user");
                        return;
                    }
        }
    }
}
