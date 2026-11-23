using System;
using System.Collections.Generic;

using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Player;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// The calls you use most, in one place, so a script can just do <c>PhotonVR.SetName("Monke")</c> or
    /// <c>PhotonVR.JoinRoom("Forest")</c> without finding components. Everything here forwards to
    /// <see cref="PhotonVRManager"/> and friends, so the same checks (word filter, owned cosmetics, master-only kicks) apply.
    /// </summary>
    public static class PhotonVR
    {
        // Connection and rooms

        public static bool IsConnected => PhotonVRManager.IsConnected;
        public static bool IsInRoom => PhotonVRManager.IsInRoom;
        public static bool IsMasterClient => PhotonVRManager.IsMasterClient;
        public static int PlayerCount => PhotonVRManager.PlayerCount;

        /// <summary>Name of the current room (the code for private rooms), or null.</summary>
        public static string RoomName => PhotonVRManager.CurrentRoomName;

        /// <summary>Everyone in the room, you included.</summary>
        public static Photon.Realtime.Player[] Players => PhotonVRManager.GetPlayersInRoom();

        /// <summary>Your own networked player object (null until it spawns).</summary>
        public static PhotonVRPlayer LocalPlayer => PhotonVRManager.Manager != null ? PhotonVRManager.Manager.LocalPlayer : null;

        /// <summary>All spawned player objects in the room, yours included.</summary>
        public static IReadOnlyList<PhotonVRPlayer> AllPlayers => PhotonVRPlayer.All;

        public static bool Connect() => PhotonVRManager.Connect();
        public static void Disconnect() => PhotonNetwork.Disconnect();

        /// <summary>Joins a random room in this queue, or makes one if none has space.</summary>
        public static void JoinRoom(string queue) => PhotonVRManager.JoinRandomRoom(queue);
        public static void JoinRoom(string queue, int maxPlayers) => PhotonVRManager.JoinRandomRoom(queue, maxPlayers);

        /// <summary>Joins the private room with this code, making it if it doesn't exist.</summary>
        public static void JoinPrivateRoom(string code) => PhotonVRManager.JoinPrivateRoom(code);

        /// <summary>Makes a private room with a random code and returns the code.</summary>
        public static string CreatePrivateRoom(int maxPlayers = 10) => PhotonVRMatchmaking.CreatePrivateRoom(maxPlayers);

        public static void LeaveRoom() => PhotonVRManager.LeaveRoom();

        /// <summary>Loads a scene and joins that scene's queue (like the original SwitchScenes).</summary>
        public static void SwitchScenes(int sceneIndex) => PhotonVRManager.SwitchScenes(sceneIndex);

        // Local player

        /// <summary>Sets your name. Returns false when the word filter refuses it.</summary>
        public static bool SetName(string name) => PhotonVRManager.TrySetUsername(name);
        public static string Name => PhotonNetwork.NickName;

        public static void SetColour(Color colour) => PhotonVRManager.SetColour(colour);

        /// <summary>Wears a cosmetic in a slot (Head, Face, Body, LeftHand, RightHand...). Refused when you don't own it.</summary>
        public static void EquipCosmetic(string slot, string cosmeticId) => PhotonVRManager.SetCosmetic(slot, cosmeticId);
        public static void UnequipCosmetic(string slot) => PhotonVRManager.ClearCosmetic(slot);
        public static string GetCosmetic(string slot) => PhotonVRManager.GetCosmetic(slot);
        public static bool OwnsCosmetic(string cosmeticId) => PhotonVRManager.OwnsCosmetic(cosmeticId);

        /// <summary>Marks a cosmetic as owned for this session (e.g. after a purchase or a code).</summary>
        public static void GrantCosmetic(string cosmeticId) => PhotonVRManager.GrantCosmetic(cosmeticId);

        // Voice

        /// <summary>Mutes or unmutes your own mic.</summary>
        public static void SetMicMuted(bool muted) => PhotonVRVoice.SetMuted(muted);
        public static bool IsMicMuted => PhotonVRVoice.IsMuted;

        /// <summary>Mutes another player for you only.</summary>
        public static void MutePlayer(Photon.Realtime.Player player, bool mute = true) => PhotonVRMuteList.SetMuted(player, mute);
        public static bool IsPlayerMuted(Photon.Realtime.Player player) => PhotonVRMuteList.IsMuted(player);

        // Moderation (master client only)

        public static bool Kick(Photon.Realtime.Player player, string reason = "Kicked") => PhotonVRModeration.Kick(player, reason);

        // Events (the same events as on PhotonVRManager, just easier to find)

        public static event Action Connected
        {
            add => PhotonVRManager.ConnectedToMaster += value;
            remove => PhotonVRManager.ConnectedToMaster -= value;
        }

        public static event Action<Room> JoinedRoom
        {
            add => PhotonVRManager.JoinedRoom += value;
            remove => PhotonVRManager.JoinedRoom -= value;
        }

        public static event Action LeftRoom
        {
            add => PhotonVRManager.LeftRoom += value;
            remove => PhotonVRManager.LeftRoom -= value;
        }

        public static event Action<DisconnectCause> Disconnected
        {
            add => PhotonVRManager.DisconnectedFromServer += value;
            remove => PhotonVRManager.DisconnectedFromServer -= value;
        }

        public static event Action<Photon.Realtime.Player> PlayerJoined
        {
            add => PhotonVRManager.PlayerJoined += value;
            remove => PhotonVRManager.PlayerJoined -= value;
        }

        public static event Action<Photon.Realtime.Player> PlayerLeft
        {
            add => PhotonVRManager.PlayerLeft += value;
            remove => PhotonVRManager.PlayerLeft -= value;
        }

        /// <summary>(new master, whether the switch was legitimate).</summary>
        public static event Action<Photon.Realtime.Player, bool> MasterChanged
        {
            add => PhotonVRMasterClient.MasterChanged += value;
            remove => PhotonVRMasterClient.MasterChanged -= value;
        }

        public static event Action<string> NameChanged
        {
            add => PhotonVRManager.UsernameChanged += value;
            remove => PhotonVRManager.UsernameChanged -= value;
        }

        /// <summary>(slot, cosmetic ID) when an equip was refused because it isn't owned.</summary>
        public static event Action<string, string> CosmeticRejected
        {
            add => PhotonVRManager.CosmeticRejected += value;
            remove => PhotonVRManager.CosmeticRejected -= value;
        }

        public static event Action<PhotonVRPlayer> LocalPlayerSpawned
        {
            add => PhotonVRManager.LocalPlayerSpawned += value;
            remove => PhotonVRManager.LocalPlayerSpawned -= value;
        }
    }
}
