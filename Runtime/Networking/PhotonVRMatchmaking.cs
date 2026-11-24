using System.Collections.Generic;
using System.Text;

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Player;

namespace Photon.VR
{
    /// <summary>
    /// Matchmaking conveniences on top of PhotonVRManager: room codes, join-by-code, region selection,
    /// player caps and spectators. All codes and names go through the word filter.
    /// </summary>
    public static class PhotonVRMatchmaking
    {
        /// <summary>Upper bound for any room size requested through PhotonVR.</summary>
        public static int MaxPlayersCap = 20;
        /// <summary>Extra seats added to private rooms for spectators (they do not count as players).</summary>
        public static int SpectatorSlots = 0;

        // No 0/O/1/I so codes are easy to read out loud.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly System.Random random = new System.Random();

        public static int ClampPlayers(int maxPlayers) => UnityEngine.Mathf.Clamp(maxPlayers, 1, UnityEngine.Mathf.Clamp(MaxPlayersCap, 1, 255));

        /// <summary>A random, filter-approved room code such as "K7MQ2X".</summary>
        public static string GenerateCode(int length = 6)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var sb = new StringBuilder(length);
                for (int i = 0; i < length; i++)
                    sb.Append(CodeAlphabet[random.Next(CodeAlphabet.Length)]);
                string code = sb.ToString();
                if (PhotonVRWordFilter.Default.IsRoomCodeAllowed(code)) return code;
            }
            return random.Next(100000, 999999).ToString();
        }

        /// <summary>Creates (or joins) a private room and returns its code.</summary>
        public static string CreatePrivateRoom(int maxPlayers, string code = null)
        {
            code = string.IsNullOrEmpty(code) ? GenerateCode() : NormalizeCode(code);
            if (!PhotonVRWordFilter.Default.IsRoomCodeAllowed(code)) return null;
            PhotonVRManager.JoinPrivateRoom(code, ClampPlayers(maxPlayers) + UnityEngine.Mathf.Max(0, SpectatorSlots));
            return code;
        }

        /// <summary>Joins an existing private room. With <paramref name="createIfMissing"/> it is created instead of failing.</summary>
        public static bool JoinByCode(string code, bool createIfMissing = false)
        {
            code = NormalizeCode(code);
            if (!PhotonVRWordFilter.Default.IsRoomCodeAllowed(code)) return false;
            if (createIfMissing) PhotonVRManager.JoinPrivateRoom(code);
            else PhotonVRManager.JoinRoomByCode(code);
            return true;
        }

        /// <summary>Joins a public room in a queue (same as PhotonVRManager.JoinRandomRoom, with the cap applied).</summary>
        public static void JoinPublic(string queue, int maxPlayers) => PhotonVRManager.JoinRandomRoom(queue, ClampPlayers(maxPlayers));

        public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>Connects to the region with the best ping on the next connect (and reconnects now if connected).</summary>
        public static void UseBestRegion() => SwitchRegion("best");

        /// <summary>Switches region, e.g. "eu", "us", "asia". Leaves the current room.</summary>
        public static void SwitchRegion(string region)
        {
            if (PhotonVRManager.Manager == null) return;
            PhotonVRManager.Manager.Region = region;
            if (PhotonVRManager.Manager.Settings != null) PhotonVRManager.Manager.Settings.Region = region;
            if (PhotonNetwork.IsConnected)
            {
                PhotonVRManager.Manager.Disconnect();
                PhotonVRManager.Manager.StartCoroutine(ReconnectWhenDisconnected());
            }
        }

        private static System.Collections.IEnumerator ReconnectWhenDisconnected()
        {
            while (PhotonNetwork.NetworkClientState != ClientState.Disconnected && PhotonNetwork.NetworkClientState != ClientState.PeerCreated)
                yield return null;
            PhotonVRManager.Connect();
        }

        /// <summary>Regions with their last measured ping (filled after connecting with region "best").</summary>
        public static List<Region> Regions =>
            PhotonNetwork.NetworkingClient?.RegionHandler?.EnabledRegions ?? new List<Region>();

        public static bool IsSpectator(Photon.Realtime.Player player) =>
            player != null && player.CustomProperties.TryGetValue(PhotonVRKeys.Spectator, out object v) && v is bool b && b;

        public static bool IsLocalSpectator => IsSpectator(PhotonNetwork.LocalPlayer);

        /// <summary>Turns spectator mode on or off: no avatar, microphone off (unless allowed in PhotonVRVoice).</summary>
        public static void SetSpectator(bool spectator)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PhotonVRKeys.Spectator, spectator } });
            PlayerSpawner spawner = PhotonVRFind.First<PlayerSpawner>();
            if (spawner == null || !PhotonNetwork.InRoom) return;
            if (spectator) spawner.Despawn();
            else if (spawner.SpawnOnJoin) spawner.Spawn();
        }

        /// <summary>Players in the room that are not spectators.</summary>
        public static int ActivePlayerCount
        {
            get
            {
                if (!PhotonNetwork.InRoom) return 0;
                int n = 0;
                foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerList)
                    if (!IsSpectator(p)) n++;
                return n;
            }
        }
    }
}
