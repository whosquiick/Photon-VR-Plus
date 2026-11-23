using System;

using UnityEngine;
using UnityEngine.SceneManagement;

using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Player;

using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Photon.VR
{
    public partial class PhotonVRManager
    {
        private enum RoomRequestType { None, Random, Private }

        private RoomOptions options;

        // Last room request, used for rejoin-after-reconnect and SwitchScenes.
        private RoomRequestType lastRequestType = RoomRequestType.None;
        private string lastRequestName;
        private int lastRequestMaxPlayers;

        // Room request queued until we are back on the master server (after LeaveRoom / while connecting).
        private Action pendingOnMaster;

        private int createRoomRetries;
        private const int MaxCreateRoomRetries = 5;

        #region Public API

        /// <summary>Switches scenes and joins a new room (queue = scene index)</summary>
        public static void SwitchScenes(int SceneIndex, int MaxPlayers)
        {
            SceneManager.LoadScene(SceneIndex);
            JoinRandomRoom(SceneIndex.ToString(), MaxPlayers);
        }

        /// <summary>Switches scenes and joins a new room (queue = scene index) with the default room limit</summary>
        public static void SwitchScenes(int SceneIndex)
        {
            if (!EnsureManager("SwitchScenes")) return;
            SwitchScenes(SceneIndex, Manager.DefaultRoomLimit);
        }

        /// <summary>
        /// Joins a random room in a queue (only players in the same queue + game version meet).
        /// Leaves the current room first and connects first when needed.
        /// </summary>
        public static void JoinRandomRoom(string Queue, int MaxPlayers) => _JoinRandomRoom(Queue, MaxPlayers);

        /// <summary>Joins a random room in a queue with the default room limit</summary>
        public static void JoinRandomRoom(string Queue)
        {
            if (!EnsureManager("JoinRandomRoom")) return;
            _JoinRandomRoom(Queue, Manager.DefaultRoomLimit);
        }

        /// <summary>Joins (or creates) a private room</summary>
        public static void JoinPrivateRoom(string RoomId, int MaxPlayers) => _JoinPrivateRoom(RoomId, MaxPlayers);

        /// <summary>Joins (or creates) a private room with the default room limit</summary>
        public static void JoinPrivateRoom(string RoomId)
        {
            if (!EnsureManager("JoinPrivateRoom")) return;
            _JoinPrivateRoom(RoomId, Manager.DefaultRoomLimit);
        }

        /// <summary>
        /// Joins (or creates) a private room. Public for compatibility with the original PhotonVR;
        /// prefer <see cref="JoinPrivateRoom(string, int)"/>.
        /// </summary>
        public static void _JoinPrivateRoom(string RoomId, int MaxPlayers)
        {
            if (string.IsNullOrEmpty(RoomId))
            {
                PhotonVRLog.Warn("JoinPrivateRoom called with an empty room code");
                return;
            }
            if (!PhotonVRWordFilter.Default.IsRoomCodeAllowed(RoomId))
            {
                PhotonVRLog.Warn("That room code is not allowed.");
                RoomCodeRejected?.Invoke(RoomId);
                return;
            }
            if (!EnsureManager("JoinPrivateRoom")) return;
            PhotonVRManager m = Manager;
            m.SetRoomRequest(RoomRequestType.Private, RoomId, MaxPlayers);
            m.RunWhenOnMaster(m.DoJoinPrivate);
        }

        /// <summary>Joins an existing room by its code; fails (OnJoinRoomFailed) instead of creating it.</summary>
        public static void JoinRoomByCode(string RoomId)
        {
            if (string.IsNullOrEmpty(RoomId) || !EnsureManager("JoinRoomByCode")) return;
            if (!PhotonVRWordFilter.Default.IsRoomCodeAllowed(RoomId))
            {
                PhotonVRLog.Warn("That room code is not allowed.");
                RoomCodeRejected?.Invoke(RoomId);
                return;
            }
            PhotonVRManager m = Manager;
            m.SetRoomRequest(RoomRequestType.Private, RoomId, m.DefaultRoomLimit);
            m.RunWhenOnMaster(() =>
            {
                m.SetState(ConnectionState.JoiningRoom);
                PhotonNetwork.JoinRoom(RoomId);
            });
        }

        /// <summary>Leaves the current room and goes back to the master server (stays connected).</summary>
        public static void LeaveRoom()
        {
            if (!PhotonNetwork.InRoom) return;
            if (Manager != null)
            {
                Manager.lastRequestType = RoomRequestType.None;
                Manager.pendingOnMaster = null;
            }
            PhotonNetwork.LeaveRoom(false);
        }

        /// <summary>Name of the current room, or null.</summary>
        public static string CurrentRoomName => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null;

        /// <summary>Queue of the current room ("queue" room property), or null for private rooms.</summary>
        public static string CurrentQueue =>
            PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PhotonVRKeys.Queue, out object q) ? q as string : null;

        /// <summary>Generates a random room code</summary>
        public string CreateRoomCode()
        {
            // Same format as the original (a number below 99999), skipping codes the word filter rejects.
            for (int i = 0; i < 20; i++)
            {
                string code = codeRandom.Next(99999).ToString();
                if (PhotonVRWordFilter.Default.IsRoomCodeAllowed(code)) return code;
            }
            return codeRandom.Next(10000, 99999).ToString();
        }

        private static readonly System.Random codeRandom = new System.Random();

        /// <summary>Raised when a room code was refused by the word filter.</summary>
        public static event Action<string> RoomCodeRejected;

        #endregion

        #region Internals

        private static void _JoinRandomRoom(string Queue, int MaxPlayers)
        {
            if (!EnsureManager("JoinRandomRoom")) return;
            PhotonVRManager m = Manager;
            m.SetRoomRequest(RoomRequestType.Random, string.IsNullOrEmpty(Queue) ? m.DefaultQueue : Queue, MaxPlayers);
            m.RunWhenOnMaster(m.DoJoinRandom);
        }

        private void SetRoomRequest(RoomRequestType type, string roomName, int maxPlayers)
        {
            lastRequestType = type;
            lastRequestName = roomName;
            lastRequestMaxPlayers = Mathf.Clamp(maxPlayers, 1, 255);
        }

        private void DoJoinRandom()
        {
            SetState(ConnectionState.JoiningRoom);
            Hashtable hastable = new Hashtable
            {
                { PhotonVRKeys.Queue, lastRequestName },
                { PhotonVRKeys.Version, Application.version }
            };

            options = new RoomOptions
            {
                MaxPlayers = (byte)lastRequestMaxPlayers,
                IsVisible = true,
                IsOpen = true,
                CustomRoomProperties = hastable,
                CustomRoomPropertiesForLobby = new string[] { PhotonVRKeys.Queue, PhotonVRKeys.Version },
                CleanupCacheOnLeave = true,
                PublishUserId = PublishUserIds
            };
            createRoomRetries = 0;

            PhotonNetwork.JoinRandomRoom(hastable, (byte)lastRequestMaxPlayers, MatchmakingMode.RandomMatching, null, null, null);
            PhotonVRLog.Info($"Joining random with type {lastRequestName}");
        }

        private void DoJoinPrivate()
        {
            SetState(ConnectionState.JoiningRoom);
            PhotonNetwork.JoinOrCreateRoom(lastRequestName, new RoomOptions
            {
                IsVisible = false,
                IsOpen = true,
                CustomRoomProperties = new Hashtable { { PhotonVRKeys.PrivateRoom, true }, { PhotonVRKeys.Version, Application.version } },
                MaxPlayers = (byte)lastRequestMaxPlayers,
                CleanupCacheOnLeave = true,
                PublishUserId = PublishUserIds
            }, null, null);
            PhotonVRLog.Info($"Joining a private room: {lastRequestName}");
        }

        // Runs <paramref name="action"/> as soon as matchmaking is possible: now when on the master server,
        // after leaving when in a room, after connecting when disconnected.
        private void RunWhenOnMaster(Action action)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom &&
                PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer)
            {
                action();
                return;
            }

            pendingOnMaster = action;

            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom(false);
            else if (!PhotonNetwork.IsConnected && State != ConnectionState.Connecting)
                Connect();
            // otherwise: connecting / joining - OnConnectedToMaster will run it
        }

        // Runs the queued room request, if any. Returns true when one ran.
        private bool RunPendingRoomRequest()
        {
            if (pendingOnMaster == null) return false;
            Action a = pendingOnMaster;
            pendingOnMaster = null;
            a();
            return true;
        }

        private void ClearPendingRoomRequest() => pendingOnMaster = null;

        // Queues a rejoin of the last queue / private room for after reconnecting.
        private void QueueRejoinLastRoom()
        {
            if (lastRequestType == RoomRequestType.Random) pendingOnMaster = DoJoinRandom;
            else if (lastRequestType == RoomRequestType.Private) pendingOnMaster = DoJoinPrivate;
        }

        private void HandleJoinError()
        {
            PhotonVRLog.Info("Failed to join room - creating a new one");

            string roomCode = CreateRoomCode();
            PhotonVRLog.Info($"Joining {roomCode}");
            PhotonNetwork.CreateRoom(roomCode, options, null, null);
        }

        #endregion

        #region Photon callbacks (rooms)

        public override void OnJoinedRoom()
        {
            Room room = PhotonNetwork.CurrentRoom;
            PhotonVRLog.Info($"Joined room {room.Name} ({room.PlayerCount}/{room.MaxPlayers})");
            SetState(ConnectionState.InRoom);
            JoinedRoom?.Invoke(room);
            OnJoinedRoomEvent?.Invoke();
        }

        public override void OnLeftRoom()
        {
            PhotonVRLog.Info("Left room");
            LocalPlayer = null;
            if (PhotonNetwork.IsConnected)
                SetState(ConnectionState.Connected);
            LeftRoom?.Invoke();
            OnLeftRoomEvent?.Invoke();
        }

        public override void OnJoinRandomFailed(short returnCode, string message) => HandleJoinError();

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            // Most likely a room code collision - try another code a few times.
            if (lastRequestType == RoomRequestType.Random && createRoomRetries < MaxCreateRoomRetries)
            {
                createRoomRetries++;
                HandleJoinError();
                return;
            }
            PhotonVRLog.Warn($"Create room failed ({returnCode}): {message}");
            SetState(ConnectionState.Connected);
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            PhotonVRLog.Warn($"Join room failed ({returnCode}): {message}");
            SetState(ConnectionState.Connected);
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => PlayerJoined?.Invoke(newPlayer);

        public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer) => PlayerLeft?.Invoke(otherPlayer);

        public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps)
        {
            // Applies colour / cosmetic changes for everyone without waiting for the legacy RPC.
            if (targetPlayer == null || changedProps == null) return;
            if (!changedProps.ContainsKey(PhotonVRKeys.Colour) && !changedProps.ContainsKey(PhotonVRKeys.Cosmetics) &&
                !changedProps.ContainsKey(PhotonVRKeys.CosmeticTints))
                return;
            foreach (PhotonVRPlayer p in PhotonVRPlayer.All)
                if (p != null && p.Owner == targetPlayer)
                    p.ApplyPlayerValues();
        }

        #endregion
    }
}
