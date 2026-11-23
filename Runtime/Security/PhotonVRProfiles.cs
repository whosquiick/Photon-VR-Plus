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
        private static readonly PhotonVRRateLimiter restoreLimit = new PhotonVRRateLimiter(1f, 3f);
        private static float lastPublish;

        public static bool TryGet(int actor, out PhotonVRProfile profile) => profiles.TryGetValue(actor, out profile);

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
                new Dictionary<string, string>(m.Cosmetics),
                new Dictionary<string, string>(m.CosmeticTints)
            };
            // Only one cached profile per player: remove the old one first.
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.ProfileEvent, null,
                new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache }, SendOptions.SendReliable);
            PhotonNetwork.RaiseEvent(PhotonVRSecurity.ProfileEvent, payload,
                new RaiseEventOptions { CachingOption = EventCaching.AddToRoomCache, Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
            Store(PhotonNetwork.LocalPlayer, payload);
            lastPublish = Time.realtimeSinceStartup;
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
        internal static void Clear() => profiles.Clear();

        // Someone changed our properties or NickName: put ours back. We cannot tell who did it.
        internal static void OnPlayerPropertiesUpdate(Photon.Realtime.Player target, Hashtable changed)
        {
            if (target == null || !target.IsLocal || PhotonVRManager.Manager == null) return;
            if (!TryGet(target.ActorNumber, out PhotonVRProfile mine)) return;
            // Echoes of our own recent changes can arrive out of order.
            if (Time.realtimeSinceStartup - lastPublish < 1.5f) return;

            bool tampered = false;
            if (changed.TryGetValue(PhotonVRKeys.Colour, out object c) && c as string != JsonUtility.ToJson(PhotonVRManager.Manager.Colour))
                tampered = true;
            if (changed.TryGetValue(PhotonVRKeys.Cosmetics, out object cos) && !SameDictionary(PhotonVRCosmeticUtility.ToStringDictionary(cos), PhotonVRManager.Manager.Cosmetics))
                tampered = true;
            if (changed.TryGetValue(ActorProperties.PlayerName, out object n) && n as string != mine.Name)
                tampered = true;

            if (!tampered || !restoreLimit.TryConsume()) return;
            PhotonVRSecurity.Report(null, PhotonVRViolationType.PropertyTamper, "another client changed our name / colour / cosmetics; restoring");
            PhotonNetwork.LocalPlayer.NickName = mine.Name;
            PhotonVRManager.Manager.RepublishLocalProperties();
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

        private static bool SameDictionary(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out string v) || v != kv.Value) return false;
            return true;
        }
    }
}
