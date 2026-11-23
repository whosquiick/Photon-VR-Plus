// Photon VR Plus - a revamp of PhotonVR by fchb1239 (MIT). See LICENSE.md.
// Every public member of the original PhotonVRManager is kept with the same name and signature.
using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

using Photon.VR.Player;
using Photon.VR.Saving;
using Photon.Pun;
using Photon.Realtime;

using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Photon.VR
{
    /// <summary>
    /// The central PhotonVR component. Put exactly one in your first scene; it survives scene loads.
    /// Handles connecting (App ID + Voice App ID + region), matchmaking (queues / private rooms),
    /// reconnecting, and the local player's name, colour and cosmetics.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class PhotonVRManager : MonoBehaviourPunCallbacks
    {
        /// <summary>The active manager instance (set in Awake).</summary>
        public static PhotonVRManager Manager { get; private set; }

        [Header("Photon")]
        public string AppId;
        public string VoiceAppId;
        [Tooltip("Please read https://doc.photonengine.com/en-us/pun/current/connection-and-authentication/regions for more information. Use \"best\" or leave empty for the best region.")]
        public string Region = "eu";
        [Tooltip("Optional settings asset. Non-empty values in it override the fields above when connecting. If empty, Resources/PhotonVRSettings is used when it exists.")]
        public PhotonVRSettings Settings;
        [Tooltip("Game version. Players only meet players with the same version. Empty = leave PhotonServerSettings.AppVersion unchanged.")]
        public string GameVersion = "";

        [Header("Player")]
        [Tooltip("Your headset / camera. If empty, Camera.main is used.")]
        public Transform Head;
        [Tooltip("Your left controller / hand transform.")]
        public Transform LeftHand;
        [Tooltip("Your right controller / hand transform.")]
        public Transform RightHand;
        public Color Colour;
        public Dictionary<string, string> Cosmetics { get; private set; } = new Dictionary<string, string>();

        [Header("Networking")]
        public string DefaultQueue = "Default";
        public int DefaultRoomLimit = 16;
        [Tooltip("PhotonNetwork.SendRate (packets per second). Photon's default is 30.")]
        public int SendRate = 30;
        [Tooltip("PhotonNetwork.SerializationRate (pose updates per second). Photon's default is 10; 15-20 looks smoother for VR but uses more messages.")]
        public int SerializationRate = 15;

        [Header("Reconnect")]
        [Tooltip("Automatically reconnect after an unexpected disconnect (timeouts, network loss).")]
        public bool AutoReconnect = true;
        [Tooltip("After reconnecting, join the same queue / private room you were in.")]
        public bool RejoinAfterReconnect = true;
        public int MaxReconnectAttempts = 5;
        [Tooltip("Seconds before the first reconnect attempt. Doubles every attempt (capped at 30s).")]
        public float ReconnectDelay = 2f;

        [Header("Other")]
        // This should always be true
        [Tooltip("If the user shall connect when this object has awoken")]
        public bool ConnectOnAwake = true;
        [Tooltip("If the user shall join a room when they connect")]
        public bool JoinRoomOnConnect = true;
        [Tooltip("Log connection progress to the console.")]
        public bool VerboseLogging = true;

        [Header("Events")]
        public UnityEvent OnConnectedEvent = new UnityEvent();
        public UnityEvent OnJoinedRoomEvent = new UnityEvent();
        public UnityEvent OnLeftRoomEvent = new UnityEvent();
        public UnityEvent OnDisconnectedEvent = new UnityEvent();

        [NonSerialized]
        public PhotonVRPlayer LocalPlayer;

        // ---- C# events (static so you can subscribe before the manager exists) ----
        /// <summary>Raised whenever <see cref="GetConnectionState"/> changes.</summary>
        public static event Action<ConnectionState> StateChanged;
        /// <summary>Raised when connected to the master server (also after leaving a room).</summary>
        public static event Action ConnectedToMaster;
        /// <summary>Raised after joining a room.</summary>
        public static event Action<Room> JoinedRoom;
        /// <summary>Raised after leaving a room.</summary>
        public static event Action LeftRoom;
        /// <summary>Raised when disconnected (with the cause).</summary>
        public static event Action<DisconnectCause> DisconnectedFromServer;
        /// <summary>Raised when the local player's cosmetics change (via SetCosmetic / SetCosmetics).</summary>
        public static event Action<Dictionary<string, string>> CosmeticsChanged;
        /// <summary>Raised when the local player's colour changes.</summary>
        public static event Action<Color> ColourChanged;
        /// <summary>Raised when the local networked player object is spawned (PhotonVRPlayer.Awake on the owner).</summary>
        public static event Action<PhotonVRPlayer> LocalPlayerSpawned;

        private RoomOptions options;

        private ConnectionState State = ConnectionState.Disconnected;

        // Last room request, used for rejoin-after-reconnect and SwitchScenes.
        private enum RoomRequestType { None, Random, Private }
        private RoomRequestType lastRequestType = RoomRequestType.None;
        private string lastRequestName;
        private int lastRequestMaxPlayers;

        // Work queued until we are back on the master server (after LeaveRoom / reconnect).
        private Action pendingOnMaster;
        // Work queued until we are fully disconnected (ChangeServers).
        private Action pendingOnDisconnected;

        private bool intentionalDisconnect;
        // True between Connect() and the first OnConnectedToMaster, so JoinRoomOnConnect only fires once per Connect.
        private bool autoJoinPending;
        private int reconnectAttempts;
        private Coroutine reconnectRoutine;
        private int createRoomRetries;
        private const int MaxCreateRoomRetries = 5;

        /// <summary>The current connection state (same value as <see cref="GetConnectionState"/>).</summary>
        public ConnectionState CurrentState => State;

        #region Unity lifecycle

        private void Awake()
        {
            if (Manager != null && Manager != this)
            {
                Debug.LogError("[PhotonVR] There can't be multiple PhotonVRManagers in a scene - destroying the duplicate on " + name);
                Destroy(gameObject);
                return;
            }

            Manager = this;
            DontDestroyOnLoad(gameObject);

            if (Settings == null)
                Settings = PhotonVRSettings.LoadDefault();

            LoadSavedValues();
        }

        private void Start()
        {
            if (Manager != this)
                return;

            ResolveRigReferences();

            if (ConnectOnAwake)
                Connect();
        }

        private void OnDestroy()
        {
            if (Manager == this)
                Manager = null;
        }

        private void LoadSavedValues()
        {
            string colour = PlayerPrefs.GetString(PhotonVRKeys.PrefsColour);
            if (!string.IsNullOrEmpty(colour))
            {
                try { Colour = JsonUtility.FromJson<Color>(colour); }
                catch (Exception e) { Debug.LogWarning("[PhotonVR] Could not read saved colour: " + e.Message); }
            }

            if (PhotonVRValueSaver.HasDictionary(PhotonVRKeys.PrefsCosmetics))
                Cosmetics = PhotonVRValueSaver.GetDictionary(PhotonVRKeys.PrefsCosmetics);
        }

        /// <summary>
        /// Fills Head / LeftHand / RightHand if they are not assigned: Head falls back to Camera.main,
        /// the hands are searched by name (XR Origin "Left Controller", "LeftHand Controller", "LeftHand Anchor" ...).
        /// Safe to call any time (e.g. after loading a scene with a new rig).
        /// </summary>
        public void ResolveRigReferences()
        {
            if (Head == null && Camera.main != null)
                Head = Camera.main.transform;

            if (LeftHand == null)
                LeftHand = FindRigTransform("Left");
            if (RightHand == null)
                RightHand = FindRigTransform("Right");
        }

        private static Transform FindRigTransform(string side)
        {
            Transform best = null;
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                string n = t.name;
                if (n.IndexOf(side, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (n.IndexOf("Cosmetic", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue; // never pick our own player model
                if (t.GetComponentInParent<PhotonVRPlayer>() != null)
                    continue;
                if (n.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Anchor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Prefer "Controller" objects (XR Origin) over generic "Hand" objects.
                    if (best == null || n.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0)
                        best = t;
                }
            }
            return best;
        }

        #endregion

#if UNITY_EDITOR
        public void CheckDefaultValues()
        {
            bool b = CheckForRig(this);
            if (b)
            {
                if (string.IsNullOrEmpty(AppId))
                    AppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;

                if (string.IsNullOrEmpty(VoiceAppId))
                    VoiceAppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice;
            }
        }

        private bool CheckForRig(PhotonVRManager manager)
        {
            bool b = false;
            if (manager.Head == null)
            {
                b = true;
                foreach (GameObject obj in FindObjectsOfType<GameObject>())
                {
                    if (obj.GetComponent<Camera>() != null && (obj.CompareTag("MainCamera") || obj.name.Contains("Camera") || obj.name.Contains("Head")))
                    {
                        manager.Head = obj.transform;
                        break;
                    }
                }
            }
            if (manager.LeftHand == null)
            {
                b = true;
                manager.LeftHand = FindRigTransform("Left");
            }
            if (manager.RightHand == null)
            {
                b = true;
                manager.RightHand = FindRigTransform("Right");
            }
            return b;
        }
#endif

        #region Connecting

        private static bool EnsureManager(string caller)
        {
            if (Manager != null)
                return true;
            Manager = FindObjectOfType<PhotonVRManager>();
            if (Manager != null)
                return true;
            Debug.LogError($"[PhotonVR] {caller}: there is no PhotonVRManager in the scene. Use Tools > PhotonVR > Setup Scene.");
            return false;
        }

        /// <summary>
        /// Copies AppId / VoiceAppId / Region / GameVersion / rates (manager fields, overridden by the
        /// settings asset) into PhotonServerSettings and PhotonNetwork. Called automatically by Connect.
        /// </summary>
        public void ApplyConnectionSettings()
        {
            if (Settings != null)
            {
                if (!string.IsNullOrEmpty(Settings.AppId)) AppId = Settings.AppId;
                if (!string.IsNullOrEmpty(Settings.VoiceAppId)) VoiceAppId = Settings.VoiceAppId;
                if (!string.IsNullOrEmpty(Settings.Region)) Region = Settings.Region;
                if (!string.IsNullOrEmpty(Settings.GameVersion)) GameVersion = Settings.GameVersion;
                if (Settings.SendRate > 0) SendRate = Settings.SendRate;
                if (Settings.SerializationRate > 0) SerializationRate = Settings.SerializationRate;
            }

            AppSettings app = PhotonNetwork.PhotonServerSettings.AppSettings;
            app.AppIdRealtime = AppId;
            // Photon Voice (PunVoiceClient / PhotonVoiceNetwork with "Use PUN App Settings") reads this value.
            app.AppIdVoice = VoiceAppId;
            string region = string.IsNullOrEmpty(Region) ? null : Region.Trim();
            app.FixedRegion = (region == null || region.Equals("best", StringComparison.OrdinalIgnoreCase)) ? null : region;
            if (!string.IsNullOrEmpty(GameVersion))
                app.AppVersion = GameVersion;

            if (SendRate > 0) PhotonNetwork.SendRate = SendRate;
            if (SerializationRate > 0) PhotonNetwork.SerializationRate = Mathf.Min(SerializationRate, PhotonNetwork.SendRate);

            PhotonVRVoice.ApplyAppSettings(VoiceAppId, app.FixedRegion);
        }

        private static bool ConnectInternal(AuthenticationValues auth)
        {
            if (!EnsureManager("Connect"))
                return false;

            PhotonVRManager m = Manager;
            m.ApplyConnectionSettings();

            if (string.IsNullOrEmpty(m.AppId))
            {
                Debug.LogError("[PhotonVR] Please input an app id (PhotonVRManager.AppId or the PhotonVRSettings asset)");
                return false;
            }
            if (string.IsNullOrEmpty(m.VoiceAppId))
                Debug.LogWarning("[PhotonVR] No VoiceAppId set - connecting without voice chat.");

            if (PhotonNetwork.IsConnected)
            {
                if (m.VerboseLogging) Debug.Log("[PhotonVR] Already connected.");
                return true;
            }

            PhotonNetwork.AuthValues = auth;
            m.intentionalDisconnect = false;
            m.autoJoinPending = m.JoinRoomOnConnect;

            // Name, colour and cosmetics are set before connecting so they arrive together with the join.
            string savedName = PlayerPrefs.GetString(PhotonVRKeys.PrefsUsername);
            PhotonNetwork.NickName = string.IsNullOrEmpty(savedName)
                ? "Player" + UnityEngine.Random.Range(1000, 9999)
                : savedName;
            m.PushLocalProperties();

            m.SetState(ConnectionState.Connecting);
            bool ok = PhotonNetwork.ConnectUsingSettings();
            if (!ok)
            {
                m.SetState(ConnectionState.Disconnected);
                Debug.LogError("[PhotonVR] ConnectUsingSettings failed (are you still disconnecting?)");
                return false;
            }
            if (m.VerboseLogging)
                Debug.Log($"[PhotonVR] Connecting - AppId: {PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime} VoiceAppId: {PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice} Region: {PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion ?? "best"}");
            return true;
        }

        /// <summary>
        /// Connects to Photon using the specified AppId and VoiceAppId
        /// </summary>
        public static bool Connect() => ConnectInternal(null);

        /// <summary>
        /// Connects to Photon using the specified AppId and VoiceAppId with authentication
        /// </summary>
        public static bool ConnectAuthenticated(string username, string token)
        {
            AuthenticationValues authentication = new AuthenticationValues { AuthType = CustomAuthenticationType.Custom };
            authentication.AddAuthParameter("username", username);
            authentication.AddAuthParameter("token", token);
            return ConnectInternal(authentication);
        }

        /// <summary>
        /// Disconnects from the Photon servers (no auto-reconnect).
        /// </summary>
        public void Disconnect()
        {
            intentionalDisconnect = true;
            StopReconnect();
            pendingOnMaster = null;
            PhotonNetwork.Disconnect();
        }

        /// <summary>
        /// Changes Photon servers
        /// </summary>
        /// <param name="Id">The new AppId</param>
        /// <param name="VoiceId">The new VoiceAppId</param>
        public static void ChangeServers(string Id, string VoiceId)
        {
            if (!EnsureManager("ChangeServers")) return;
            ChangeServersInternal(Id, VoiceId, () => Connect());
        }

        /// <summary>
        /// Changes Photon servers with authentication
        /// </summary>
        /// <param name="Id">The new AppId</param>
        /// <param name="VoiceId">The new VoiceAppId</param>
        public static void ChangeServersAuthenticated(string Id, string VoiceId, string username, string token)
        {
            if (!EnsureManager("ChangeServersAuthenticated")) return;
            ChangeServersInternal(Id, VoiceId, () => ConnectAuthenticated(username, token));
        }

        private static void ChangeServersInternal(string id, string voiceId, Action connect)
        {
            PhotonVRManager m = Manager;
            m.AppId = id;
            m.VoiceAppId = voiceId;
            // Explicit IDs win over the settings asset from now on.
            m.Settings = null;

            if (PhotonNetwork.NetworkClientState == ClientState.Disconnected || PhotonNetwork.NetworkClientState == ClientState.PeerCreated)
            {
                connect();
                return;
            }

            // Connecting while still disconnecting fails, so wait for OnDisconnected.
            m.pendingOnDisconnected = connect;
            m.Disconnect();
        }

        #endregion

        #region Player values (name / colour / cosmetics)

        /// <summary>
        /// Sets the Photon nickname to something
        /// </summary>
        /// <param name="Name">The string you want the Photon nickname to be</param>
        public static void SetUsername(string Name)
        {
            Name = Name ?? string.Empty;
            PhotonNetwork.LocalPlayer.NickName = Name;
            PlayerPrefs.SetString(PhotonVRKeys.PrefsUsername, Name);
            PlayerPrefs.Save();

            RefreshLocal();
        }

        /// <summary>
        /// Sets the colour
        /// </summary>
        /// <param name="PlayerColour">The colour you want the player to be</param>
        public static void SetColour(Color PlayerColour)
        {
            if (!EnsureManager("SetColour")) return;
            Manager.Colour = PlayerColour;
            string json = JsonUtility.ToJson(PlayerColour);
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PhotonVRKeys.Colour, json } });
            PlayerPrefs.SetString(PhotonVRKeys.PrefsColour, json);
            PlayerPrefs.Save();

            ColourChanged?.Invoke(PlayerColour);
            RefreshLocal();
        }

        /// <summary>
        /// Sets the cosmetics
        /// </summary>
        /// <param name="PlayerCosmetics">The cosmetics you want to set (slot name -> cosmetic ID)</param>
        public static void SetCosmetics(Dictionary<string, string> PlayerCosmetics)
        {
            if (!EnsureManager("SetCosmetics")) return;
            var copy = new Dictionary<string, string>();
            if (PlayerCosmetics != null)
                foreach (var kv in PlayerCosmetics)
                    if (!string.IsNullOrEmpty(kv.Key))
                        copy[kv.Key] = kv.Value ?? string.Empty;
            Manager.Cosmetics = copy;
            Manager.CommitCosmetics();
        }

        /// <summary>
        /// Sets a specific cosmetic
        /// </summary>
        /// <param name="Type">The slot of cosmetic you want to set ("Head", "Face", "Body", "LeftHand", "RightHand" or your own)</param>
        /// <param name="CosmeticId">The name of the cosmetic object under that slot ("" to clear)</param>
        public static void SetCosmetic(string Type, string CosmeticId)
        {
            if (string.IsNullOrEmpty(Type))
            {
                Debug.LogWarning("[PhotonVR] SetCosmetic called with an empty slot name");
                return;
            }
            if (!EnsureManager("SetCosmetic")) return;
            Manager.Cosmetics[Type] = CosmeticId ?? string.Empty;
            Manager.CommitCosmetics();
        }

        /// <summary>Clears one cosmetic slot. Same as SetCosmetic(slot, "").</summary>
        public static void ClearCosmetic(string Type) => SetCosmetic(Type, string.Empty);

        /// <summary>Removes every equipped cosmetic.</summary>
        public static void ClearAllCosmetics()
        {
            if (!EnsureManager("ClearAllCosmetics")) return;
            var cleared = new Dictionary<string, string>();
            foreach (var key in Manager.Cosmetics.Keys)
                cleared[key] = string.Empty;
            SetCosmetics(cleared);
        }

        /// <summary>Returns the cosmetic equipped in a slot, or "" if none.</summary>
        public static string GetCosmetic(string Type)
        {
            if (Manager == null || string.IsNullOrEmpty(Type)) return string.Empty;
            return Manager.Cosmetics.TryGetValue(Type, out string id) ? id ?? string.Empty : string.Empty;
        }

        private void CommitCosmetics()
        {
            // Always send a fresh copy so Photon never holds a reference we mutate later.
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PhotonVRKeys.Cosmetics, new Dictionary<string, string>(Cosmetics) } });
            PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmetics, Cosmetics);

            CosmeticsChanged?.Invoke(new Dictionary<string, string>(Cosmetics));
            RefreshLocal();
        }

        /// <summary>Pushes Colour + Cosmetics into the local player's custom properties.</summary>
        private void PushLocalProperties()
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { PhotonVRKeys.Colour, JsonUtility.ToJson(Colour) },
                { PhotonVRKeys.Cosmetics, new Dictionary<string, string>(Cosmetics) }
            });
        }

        private static void RefreshLocal()
        {
            // Applies immediately on our own client. Remote clients update through
            // OnPlayerPropertiesUpdate; the RPC is kept for clients running the original PhotonVR.
            if (PhotonNetwork.InRoom && Manager != null && Manager.LocalPlayer != null)
                Manager.LocalPlayer.RefreshPlayerValues();
        }

        internal static void NotifyLocalPlayerSpawned(PhotonVRPlayer player)
        {
            if (Manager != null)
                Manager.LocalPlayer = player;
            LocalPlayerSpawned?.Invoke(player);
        }

        #endregion

        #region Rooms

        /// <summary>
        /// Gets the connection state
        /// </summary>
        /// <returns>The current connection state</returns>
        public static ConnectionState GetConnectionState()
        {
            return Manager != null ? Manager.State : ConnectionState.Disconnected;
        }

        /// <summary>
        /// Switches scenes and joins a new room (queue = scene index)
        /// </summary>
        public static void SwitchScenes(int SceneIndex, int MaxPlayers)
        {
            SceneManager.LoadScene(SceneIndex);
            JoinRandomRoom(SceneIndex.ToString(), MaxPlayers);
        }

        /// <summary>
        /// Switches scenes and joins a new room (queue = scene index)
        /// </summary>
        public static void SwitchScenes(int SceneIndex)
        {
            if (!EnsureManager("SwitchScenes")) return;
            SwitchScenes(SceneIndex, Manager.DefaultRoomLimit);
        }

        /// <summary>
        /// Joins a random room in a queue (only players in the same queue + game version meet)
        /// </summary>
        public static void JoinRandomRoom(string Queue, int MaxPlayers) => _JoinRandomRoom(Queue, MaxPlayers);

        /// <summary>
        /// Joins a random room in a queue with the default room limit
        /// </summary>
        public static void JoinRandomRoom(string Queue)
        {
            if (!EnsureManager("JoinRandomRoom")) return;
            _JoinRandomRoom(Queue, Manager.DefaultRoomLimit);
        }

        private static void _JoinRandomRoom(string Queue, int MaxPlayers)
        {
            if (!EnsureManager("JoinRandomRoom")) return;
            PhotonVRManager m = Manager;
            m.lastRequestType = RoomRequestType.Random;
            m.lastRequestName = string.IsNullOrEmpty(Queue) ? m.DefaultQueue : Queue;
            m.lastRequestMaxPlayers = Mathf.Clamp(MaxPlayers, 1, 255);
            m.RunWhenOnMaster(m.DoJoinRandom);
        }

        private void DoJoinRandom()
        {
            SetState(ConnectionState.JoiningRoom);
            Hashtable hastable = new Hashtable
            {
                { PhotonVRKeys.Queue, lastRequestName },
                { PhotonVRKeys.Version, Application.version }
            };

            RoomOptions roomOptions = new RoomOptions();
            roomOptions.MaxPlayers = (byte)lastRequestMaxPlayers;
            roomOptions.IsVisible = true;
            roomOptions.IsOpen = true;
            roomOptions.CustomRoomProperties = hastable;
            roomOptions.CustomRoomPropertiesForLobby = new string[] { PhotonVRKeys.Queue, PhotonVRKeys.Version };
            roomOptions.CleanupCacheOnLeave = true;
            options = roomOptions;
            createRoomRetries = 0;

            PhotonNetwork.JoinRandomRoom(hastable, (byte)lastRequestMaxPlayers, MatchmakingMode.RandomMatching, null, null, null);
            if (VerboseLogging) Debug.Log($"[PhotonVR] Joining random with type {hastable[PhotonVRKeys.Queue]}");
        }

        /// <summary>
        /// Joins (or creates) a private room
        /// </summary>
        /// <param name="RoomId">The room code</param>
        /// <param name="MaxPlayers">The maximum amount of players that can be in the room</param>
        public static void JoinPrivateRoom(string RoomId, int MaxPlayers) => _JoinPrivateRoom(RoomId, MaxPlayers);

        /// <summary>
        /// Joins (or creates) a private room with the default room limit
        /// </summary>
        /// <param name="RoomId">The room code</param>
        public static void JoinPrivateRoom(string RoomId)
        {
            if (!EnsureManager("JoinPrivateRoom")) return;
            _JoinPrivateRoom(RoomId, Manager.DefaultRoomLimit);
        }

        public static void _JoinPrivateRoom(string RoomId, int MaxPlayers)
        {
            if (string.IsNullOrEmpty(RoomId))
            {
                Debug.LogWarning("[PhotonVR] JoinPrivateRoom called with an empty room code");
                return;
            }
            if (!EnsureManager("JoinPrivateRoom")) return;
            PhotonVRManager m = Manager;
            m.lastRequestType = RoomRequestType.Private;
            m.lastRequestName = RoomId;
            m.lastRequestMaxPlayers = Mathf.Clamp(MaxPlayers, 1, 255);
            m.RunWhenOnMaster(m.DoJoinPrivate);
        }

        private void DoJoinPrivate()
        {
            SetState(ConnectionState.JoiningRoom);
            PhotonNetwork.JoinOrCreateRoom(lastRequestName, new RoomOptions()
            {
                IsVisible = false,
                IsOpen = true,
                MaxPlayers = (byte)lastRequestMaxPlayers,
                CleanupCacheOnLeave = true
            }, null, null);
            if (VerboseLogging) Debug.Log($"[PhotonVR] Joining a private room: {lastRequestName}");
        }

        /// <summary>Leaves the current room and goes back to the master server (stays connected).</summary>
        public static void LeaveRoom()
        {
            if (!PhotonNetwork.InRoom) return;
            if (Manager != null) Manager.lastRequestType = RoomRequestType.None;
            PhotonNetwork.LeaveRoom(false);
        }

        /// <summary>Name of the current room, or null.</summary>
        public static string CurrentRoomName => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null;

        /// <summary>
        /// Runs <paramref name="action"/> as soon as we can do matchmaking: now if on the master server,
        /// after leaving if in a room, after connecting if disconnected.
        /// </summary>
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

        /// <summary>
        /// Generates a random room code
        /// </summary>
        /// <returns>A room code</returns>
        public string CreateRoomCode()
        {
            return new System.Random().Next(99999).ToString();
        }

        #endregion

        #region Photon callbacks

        private void SetState(ConnectionState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        public override void OnConnectedToMaster()
        {
            SetState(ConnectionState.Connected);
            reconnectAttempts = 0;
            if (VerboseLogging) Debug.Log($"[PhotonVR] Connected to master ({PhotonNetwork.CloudRegion})");

            // Make sure name / colour / cosmetics are current (they are sent when joining a room).
            if (string.IsNullOrEmpty(PhotonNetwork.NickName))
                PhotonNetwork.NickName = PlayerPrefs.GetString(PhotonVRKeys.PrefsUsername);
            PushLocalProperties();

            ConnectedToMaster?.Invoke();
            OnConnectedEvent?.Invoke();

            if (pendingOnMaster != null)
            {
                Action a = pendingOnMaster;
                pendingOnMaster = null;
                a();
            }
            else if (autoJoinPending && JoinRoomOnConnect)
            {
                JoinRandomRoom(DefaultQueue, DefaultRoomLimit);
            }
            autoJoinPending = false;
        }

        public override void OnJoinedRoom()
        {
            if (VerboseLogging) Debug.Log($"[PhotonVR] Joined room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
            SetState(ConnectionState.InRoom);
            JoinedRoom?.Invoke(PhotonNetwork.CurrentRoom);
            OnJoinedRoomEvent?.Invoke();
        }

        public override void OnLeftRoom()
        {
            if (VerboseLogging) Debug.Log("[PhotonVR] Left room");
            LocalPlayer = null;
            if (PhotonNetwork.IsConnected)
                SetState(ConnectionState.Connected);
            LeftRoom?.Invoke();
            OnLeftRoomEvent?.Invoke();
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            base.OnDisconnected(cause);
            bool wasInRoom = State == ConnectionState.InRoom || State == ConnectionState.JoiningRoom;
            SetState(ConnectionState.Disconnected);
            LocalPlayer = null;
            if (VerboseLogging) Debug.Log($"[PhotonVR] Disconnected from server: {cause}");

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
                {
                    if (lastRequestType == RoomRequestType.Random) pendingOnMaster = DoJoinRandom;
                    else if (lastRequestType == RoomRequestType.Private) pendingOnMaster = DoJoinPrivate;
                }
                StartReconnect();
            }
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
            if (reconnectRoutine != null) return;
            if (!isActiveAndEnabled) return;
            reconnectRoutine = StartCoroutine(ReconnectLoop());
        }

        private void StopReconnect()
        {
            if (reconnectRoutine != null)
            {
                StopCoroutine(reconnectRoutine);
                reconnectRoutine = null;
            }
        }

        private IEnumerator ReconnectLoop()
        {
            while (reconnectAttempts < MaxReconnectAttempts && !PhotonNetwork.IsConnected && !intentionalDisconnect)
            {
                float delay = Mathf.Min(30f, Mathf.Max(0.5f, ReconnectDelay) * Mathf.Pow(2f, reconnectAttempts));
                reconnectAttempts++;
                if (VerboseLogging) Debug.Log($"[PhotonVR] Reconnecting in {delay:0.#}s (attempt {reconnectAttempts}/{MaxReconnectAttempts})");
                yield return new WaitForSecondsRealtime(delay);

                if (PhotonNetwork.IsConnected || intentionalDisconnect)
                    break;

                SetState(ConnectionState.Connecting);
                // Reconnect() reuses the last master server and auth values; fall back to a fresh connect.
                if (!PhotonNetwork.Reconnect())
                {
                    SetState(ConnectionState.Disconnected);
                    ConnectInternal(PhotonNetwork.AuthValues);
                }

                // Wait for the attempt to resolve.
                float timeout = 15f;
                while (timeout > 0f && State == ConnectionState.Connecting)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (!PhotonNetwork.IsConnected && reconnectAttempts >= MaxReconnectAttempts)
                Debug.LogWarning("[PhotonVR] Gave up reconnecting. Call PhotonVRManager.Connect() to try again.");
            reconnectRoutine = null;
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
            Debug.LogWarning($"[PhotonVR] Create room failed ({returnCode}): {message}");
            SetState(ConnectionState.Connected);
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            Debug.LogWarning($"[PhotonVR] Join room failed ({returnCode}): {message}");
            SetState(ConnectionState.Connected);
        }

        public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps)
        {
            // Applies colour / cosmetic changes for everyone, without waiting for the legacy RPC.
            if (targetPlayer == null || changedProps == null) return;
            if (!changedProps.ContainsKey(PhotonVRKeys.Colour) && !changedProps.ContainsKey(PhotonVRKeys.Cosmetics))
                return;
            foreach (PhotonVRPlayer p in PhotonVRPlayer.All)
                if (p != null && p.photonView != null && p.photonView.Owner == targetPlayer)
                    p.ApplyPlayerValues();
        }

        private void HandleJoinError()
        {
            if (VerboseLogging) Debug.Log("[PhotonVR] Failed to join room - creating a new one");

            string roomCode = CreateRoomCode();
            if (VerboseLogging) Debug.Log($"[PhotonVR] Joining {roomCode}");
            PhotonNetwork.CreateRoom(roomCode, options, null, null);
        }

        #endregion
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        JoiningRoom,
        InRoom
    }
}
