using System.Collections.Generic;

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Player;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>A player's name, colour and cosmetics as published by that player.</summary>
    public sealed class PhotonVRProfile
    {
        public int Actor;
        public string Name = "";
        public string ColourJson = "";
        public Dictionary<string, string> Cosmetics = new Dictionary<string, string>();
        public Dictionary<string, string> Tints = new Dictionary<string, string>();
    }

    /// <summary>
    /// Trusted player profiles.
    /// <para>Photon lets any client overwrite any other player's custom properties (and NickName), and the update
    /// does not say who wrote it. So every PhotonVR client also publishes its own profile as a cached event; Photon's
    /// server stamps the sender on events, so a profile can only ever describe its sender. PhotonVRPlayer shows the
    /// profile and ignores tampered properties, and each client restores its own properties when someone else
    /// changes them. Clients running the original PhotonVR fall back to the properties.</para>
    /// </summary>
    public static class PhotonVRProfiles
    {
        public const int MaxNameLength = 24;
        private const int MaxCosmetics = 32;
        private const int MaxValueLength = 64;

        private static readonly Dictionary<int, PhotonVRProfile> profiles = new Dictionary<int, PhotonVRProfile>();

        public static bool TryGet(int actor, out PhotonVRProfile profile) => profiles.TryGetValue(actor, out profile);

        // Domain reload can be turned off (Enter Play Mode Options); start every play session clean.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            profiles.Clear();
            ownWrites.Clear();
        }

        /// <summary>The name to show for a player: their own published name, filtered.</summary>
        public static string DisplayName(Photon.Realtime.Player player)
        {
            if (player == null) return string.Empty;
            string name = TryGet(player.ActorNumber, out PhotonVRProfile p) ? p.Name : player.NickName;
            return PhotonVRWordFilter.Default.SanitizeName(name, player.ActorNumber);
        }

        /// <summary>Publishes the local player's profile (replaces the previous one in the room cache).</summary>
        public static void PublishLocal()
        {
            if (!PhotonNetwork.InRoom || PhotonVRManager.Manager == null) return;
            PhotonVRManager m = PhotonVRManager.Manager;
            object[] payload =
            {
                PhotonNetwork.LocalPlayer.NickName ?? "",
                JsonUtility.ToJson(m.Colour),
                m.OwnedOnly(m.Cosmetics),
                new Dictionary<string, string>(m.CosmeticTints)
            };
            // Only one cached profile per player: remove our old one first. TargetActors limits the removal to our
            // own events (without it Photon removes every player's cached profile).
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.ProfileEvent, null,
                new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache, TargetActors = new[] { PhotonNetwork.LocalPlayer.ActorNumber } },
                SendOptions.SendReliable);
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.ProfileEvent, payload,
                new RaiseEventOptions { CachingOption = EventCaching.AddToRoomCache, Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
            Store(PhotonNetwork.LocalPlayer, payload);
        }

        internal static void HandleEvent(EventData e)
        {
            Photon.Realtime.Player sender = PhotonNetwork.CurrentRoom?.GetPlayer(e.Sender);
            if (sender == null || sender.IsLocal || !(e.CustomData is object[] data)) return;
            if (!PhotonVRSecurity.CheckRate(sender, "profile", 2f, 6f)) return;
            if (!Store(sender, data))
                PhotonVRSecurity.Report(sender, PhotonVRViolationType.BadData, "malformed profile");
        }

        private static bool Store(Photon.Realtime.Player owner, object[] data)
        {
            if (data.Length < 3 || !(data[0] is string name) || !(data[1] is string colour)) return false;
            var profile = new PhotonVRProfile
            {
                Actor = owner.ActorNumber,
                Name = Clip(name, MaxNameLength),
                ColourJson = Clip(colour, 128),
                Cosmetics = Bounded(PhotonVRCosmeticUtility.ToStringDictionary(data[2])),
                Tints = data.Length > 3 ? Bounded(PhotonVRCosmeticUtility.ToStringDictionary(data[3])) : new Dictionary<string, string>()
            };
            profiles[owner.ActorNumber] = profile;

            if (!owner.IsLocal && PhotonVRWordFilter.Default.IsBlocked(profile.Name))
            {
                PhotonVRSecurity.Report(owner, PhotonVRViolationType.BlockedName, "blocked name");
                if (PhotonVRModeration.KickBlockedNames && PhotonVRMasterClient.IsLocalLegitimateMaster)
                    PhotonVRModeration.Kick(owner, "Name not allowed");
            }

            foreach (PhotonVRPlayer p in PhotonVRPlayer.All)
                if (p != null && p.Owner != null && p.Owner.ActorNumber == owner.ActorNumber)
                    p.ApplyPlayerValues();
            return true;
        }

        internal static void OnJoinedRoom()
        {
            Clear();
            PublishLocal();
        }

        internal static void Remove(int actor) => profiles.Remove(actor);

        internal static void Clear()
        {
            profiles.Clear();
            ownWrites.Clear();
        }

        // Our own name / colour / cosmetics
        // Each player owns these keys on their own actor; nothing here involves the master client. Photon stamps who
        // wrote a property change on the event, so we can tell our own changes (made by PhotonVR or by your scripts
        // setting PhotonNetwork.NickName) from someone else writing to us. Runs only when a change arrives.

        private const float EchoWindow = 2f;
        private static readonly object[] OwnKeys =
        {
            ActorProperties.PlayerName, PhotonVRKeys.Colour, PhotonVRKeys.Cosmetics, PhotonVRKeys.CosmeticTints
        };
        private static readonly Dictionary<object, float> ownWrites = new Dictionary<object, float>();
        private static readonly PhotonVRRateLimiter restoreLimit = new PhotonVRRateLimiter(1f, 3f);

        // PhotonVR is about to write this key itself; its echo is not an outside change.
        internal static void NoteOwnWrite(object key) => ownWrites[key] = Time.realtimeSinceStartup;

        internal static void HandlePropertiesChanged(EventData e)
        {
            PhotonVRManager m = PhotonVRManager.Manager;
            if (m == null || !PhotonNetwork.InRoom || !(e[ParameterCode.TargetActorNr] is int target)) return;
            if (target != PhotonNetwork.LocalPlayer.ActorNumber || !(e[ParameterCode.Properties] is Hashtable changed)) return;

            bool ours = false, echo = true;
            float now = Time.realtimeSinceStartup;
            foreach (object key in OwnKeys)
            {
                if (!changed.ContainsKey(key)) continue;
                ours = true;
                if (!ownWrites.TryGetValue(key, out float t) || now - t > EchoWindow) echo = false;
            }
            int writer = e.Sender; // 0 when Photon did not say who wrote it: leave it alone
            if (!ours || writer <= 0) return;

            if (writer == target)
            {
                // Our own client, not through PhotonVR (e.g. a name computer script): keep it, filtered.
                if (!echo) m.AdoptLocalChange(changed);
                return;
            }

            PhotonVRSecurity.Report(PhotonNetwork.CurrentRoom.GetPlayer(writer), PhotonVRViolationType.PropertyTamper,
                "changed our name / colour / cosmetics; restoring");
            if (!restoreLimit.TryConsume()) return;
            if (changed.ContainsKey(ActorProperties.PlayerName))
            {
                NoteOwnWrite(ActorProperties.PlayerName);
                PhotonNetwork.LocalPlayer.NickName = PhotonVRManager.AcceptedName;
            }
            m.RepublishLocalProperties();
        }

        internal static void OnMasterClientSwitched()
        {
            PhotonVRManager m = PhotonVRManager.Manager;
            if (m != null && m.ResyncOnMasterSwitch) m.ResyncLocalProperties();
        }

        private static string Clip(string s, int max) => s == null ? "" : s.Length > max ? s.Substring(0, max) : s;

        private static Dictionary<string, string> Bounded(Dictionary<string, string> source)
        {
            var result = new Dictionary<string, string>();
            foreach (var kv in source)
            {
                if (result.Count >= MaxCosmetics) break;
                result[Clip(kv.Key, MaxValueLength)] = Clip(kv.Value, MaxValueLength);
            }
            return result;
        }
    }
}
