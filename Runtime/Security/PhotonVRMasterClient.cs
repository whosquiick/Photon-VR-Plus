using System;
using System.Linq;

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace Photon.VR
{
    /// <summary>
    /// Master-client handoff helpers and the takeover guard.
    /// <para>In PUN any client can call <c>SetMasterClient</c>. This class tracks the <b>legitimate</b> master:
    /// a switch is accepted when the old master left, when the old master announced a handoff first
    /// (<see cref="HandOffTo"/>), or when your <see cref="PhotonVRSecurity.Authority"/> approves it. Anything else is
    /// treated as a hijack: it is reported, the real master takes the role back, and PhotonVR code (kicks, room
    /// control) keeps following <see cref="LegitimateMaster"/> instead of the hijacker.</para>
    /// </summary>
    public static class PhotonVRMasterClient
    {
        /// <summary>Seconds a handoff announcement stays valid.</summary>
        public static float HandoffWindow = 10f;
        /// <summary>Kick a player that hijacked the master role (done by the legitimate master).</summary>
        public static bool KickHijackers = true;

        /// <summary>Raised with (newMaster, wasLegitimate) after every master switch.</summary>
        public static event Action<Photon.Realtime.Player, bool> MasterChanged;

        private static int legitimateActor = -1;
        private static int handoffTarget = -1;
        private static double handoffTime;
        private static int reclaimAttempts;
        private static double reclaimWindowStart;

        /// <summary>The master PhotonVR trusts. Usually equals PhotonNetwork.MasterClient.</summary>
        public static Photon.Realtime.Player LegitimateMaster =>
            PhotonNetwork.CurrentRoom != null && legitimateActor > 0 ? PhotonNetwork.CurrentRoom.GetPlayer(legitimateActor) : PhotonNetwork.MasterClient;

        public static bool IsLocalLegitimateMaster =>
            PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer.ActorNumber == (LegitimateMaster?.ActorNumber ?? -1);

        /// <summary>True when Photon's master and the legitimate master are the same player.</summary>
        public static bool IsMasterTrusted => PhotonNetwork.MasterClient != null && PhotonNetwork.MasterClient.ActorNumber == legitimateActor;

        /// <summary>Hands the master role to <paramref name="target"/>. Only works on the legitimate master.</summary>
        public static bool HandOffTo(Photon.Realtime.Player target)
        {
            if (target == null || target.IsInactive || !IsLocalLegitimateMaster || target.IsLocal)
                return false;
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.MasterHandoffEvent, target.ActorNumber,
                new RaiseEventOptions { Receivers = ReceiverGroup.All }, SendOptions.SendReliable);
            return PhotonNetwork.SetMasterClient(target);
        }

        /// <summary>Hands off to the active player with the lowest published ping (see <see cref="PublishPing"/>), excluding spectators.</summary>
        public static bool HandOffToBest()
        {
            if (!PhotonNetwork.InRoom) return false;
            Photon.Realtime.Player best = PhotonNetwork.PlayerListOthers
                .Where(p => !p.IsInactive && !PhotonVRMatchmaking.IsSpectator(p))
                .OrderBy(p => p.CustomProperties.TryGetValue(PhotonVRKeys.Ping, out object v) && v is int ping ? ping : 9999)
                .FirstOrDefault();
            return best != null && HandOffTo(best);
        }

        /// <summary>Publishes this client's ping as a player property so the master can pick a good successor.</summary>
        public static void PublishPing()
        {
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PhotonVRKeys.Ping, PhotonNetwork.GetPing() } });
        }

        internal static void OnJoinedRoom()
        {
            legitimateActor = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;
            handoffTarget = -1;
            reclaimAttempts = 0;
        }

        internal static void OnLeftRoom()
        {
            legitimateActor = -1;
            handoffTarget = -1;
        }

        internal static void HandleHandoffEvent(EventData e)
        {
            Photon.Realtime.Player sender = PhotonNetwork.CurrentRoom?.GetPlayer(e.Sender);
            if (sender == null) return;
            if (sender.ActorNumber != legitimateActor)
            {
                PhotonVRSecurity.Report(sender, PhotonVRViolationType.MasterTakeover, "handoff announced by a non-master");
                return;
            }
            if (e.CustomData is int target)
            {
                handoffTarget = target;
                handoffTime = PhotonNetwork.Time;
            }
        }

        internal static void OnMasterClientSwitched(Photon.Realtime.Player newMaster)
        {
            if (newMaster == null) return;
            Photon.Realtime.Player previous = legitimateActor > 0 ? PhotonNetwork.CurrentRoom?.GetPlayer(legitimateActor) : null;
            bool legit = IsLegitimateSwitch(newMaster, previous);

            if (legit)
            {
                legitimateActor = newMaster.ActorNumber;
                handoffTarget = -1;
            }
            else
            {
                PhotonVRSecurity.Report(newMaster, PhotonVRViolationType.MasterTakeover,
                    $"became master while #{legitimateActor} was still in the room");
                if (previous != null && previous.IsLocal)
                    Reclaim(newMaster);
            }
            MasterChanged?.Invoke(newMaster, legit);
        }

        private static bool IsLegitimateSwitch(Photon.Realtime.Player newMaster, Photon.Realtime.Player previous)
        {
            if (newMaster.ActorNumber == legitimateActor) return true;  // reclaimed / unchanged
            if (legitimateActor <= 0) return true;                       // nothing known yet

            if (PhotonVRSecurity.Authority != null)
            {
                bool? decision = null;
                try { decision = PhotonVRSecurity.Authority.ValidateMasterClient(newMaster, previous); }
                catch (Exception ex) { PhotonVRLog.Error("Authority.ValidateMasterClient threw: " + ex); }
                if (decision.HasValue) return decision.Value;
            }

            // Old master gone (left, or inactive with PlayerTTL): Photon picked the successor.
            if (previous == null || previous.IsInactive) return true;
            // Announced by the old master just before.
            return handoffTarget == newMaster.ActorNumber && PhotonNetwork.Time - handoffTime < HandoffWindow;
        }

        private static void Reclaim(Photon.Realtime.Player hijacker)
        {
            if (PhotonNetwork.Time - reclaimWindowStart > 30) { reclaimWindowStart = PhotonNetwork.Time; reclaimAttempts = 0; }
            if (++reclaimAttempts > 5)
            {
                PhotonVRLog.Warn("Master hijack keeps repeating; giving up reclaiming. Consider leaving the room.");
                return;
            }
            PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
            if (KickHijackers)
                PhotonVRModeration.Kick(hijacker, "Master client takeover");
        }
    }
}
