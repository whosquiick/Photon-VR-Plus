using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Player;
using Photon.VR.Saving;

namespace Photon.VR
{
    /// <summary>
    /// The central PhotonVR component. Put exactly one in your first scene (or drop in the
    /// <c>PhotonVRManager</c> prefab); it survives scene loads. It handles connecting with your
    /// App ID / Voice App ID / region, queue-based and private matchmaking, auto-reconnect, and the local
    /// player's name, colour and cosmetics.
    /// </summary>
    // Split into partials: Networking/*.Connection, *.Rooms, *.Security and Cosmetics/*.PlayerValues.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("PhotonVR/PhotonVR Manager")]
    [HelpURL("https://github.com/whosquiick/Photon-VR-Plus")]
    public partial class PhotonVRManager : MonoBehaviourPunCallbacks, ISerializationCallbackReceiver
    {
        /// <summary>The active manager instance (assigned in Awake).</summary>
        public static PhotonVRManager Manager { get; private set; }

        #region Inspector

        [Header("Photon")]
        [Tooltip("Photon PUN App ID (dashboard.photonengine.com). Stored encrypted - set it with the masked field or Tools > PhotonVR > Set App IDs.")]
        [SerializeField] private PhotonVRProtectedString appId = new PhotonVRProtectedString();
        [Tooltip("Photon Voice App ID (dashboard.photonengine.com). Stored encrypted.")]
        [SerializeField] private PhotonVRProtectedString voiceAppId = new PhotonVRProtectedString();

        // v1.0.0 / original PhotonVR stored the IDs as plaintext fields named "AppId" / "VoiceAppId".
        // They are read once, moved into the encrypted fields and cleared (see OnAfterDeserialize).
        [FormerlySerializedAs("AppId")] [SerializeField, HideInInspector] private string legacyAppId = "";
        [FormerlySerializedAs("VoiceAppId")] [SerializeField, HideInInspector] private string legacyVoiceAppId = "";

        /// <summary>
        /// Photon PUN App ID. Same name as the original public field; now backed by encrypted storage
        /// (the getter decrypts, the setter encrypts).
        /// </summary>
        public string AppId
        {
            get => idVault != null ? idVault.Reveal(PhotonVRAppIdKind.Realtime) : appId.Get();
            set => SetId(PhotonVRAppIdKind.Realtime, value);
        }

        /// <summary>Photon Voice App ID. Same name as the original public field; backed by encrypted storage.</summary>
        public string VoiceAppId
        {
            get => idVault != null ? idVault.Reveal(PhotonVRAppIdKind.Voice) : voiceAppId.Get();
            set => SetId(PhotonVRAppIdKind.Voice, value);
        }

        public bool HasAppId => idVault != null ? idVault.Has(PhotonVRAppIdKind.Realtime) : appId.HasValue;
        public bool HasVoiceAppId => idVault != null ? idVault.Has(PhotonVRAppIdKind.Voice) : voiceAppId.HasValue;

        [Tooltip("Please read https://doc.photonengine.com/en-us/pun/current/connection-and-authentication/regions for more information. Use \"best\" or leave empty for the best region.")]
        public string Region = "eu";
        [Tooltip("Optional settings asset. Non-empty values override the fields above when connecting. If empty, Resources/PhotonVRSettings is used when it exists.")]
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
        [Tooltip("Your player colour (saved in PlayerPrefs, synced as the \"Colour\" custom property).")]
        public Color Colour;

        /// <summary>The local player's cosmetics: slot name to cosmetic ID. Change with <see cref="SetCosmetic"/> / <see cref="SetCosmetics"/>.</summary>
        public Dictionary<string, string> Cosmetics { get; private set; } = new Dictionary<string, string>();

        /// <summary>Per-slot tint colours ("#RRGGBBAA"). Change with <see cref="SetCosmeticTint"/>.</summary>
        public Dictionary<string, string> CosmeticTints { get; private set; } = new Dictionary<string, string>();

        [Header("Networking")]
        [Tooltip("Queue joined by JoinRoomOnConnect and the inspector Join button.")]
        public string DefaultQueue = "Default";
        [Tooltip("Max players for rooms created without an explicit limit.")]
        public int DefaultRoomLimit = 16;
        [Tooltip("PhotonNetwork.SendRate (packets per second). Photon's default is 30.")]
        public int SendRate = 30;
        [Tooltip("PhotonNetwork.SerializationRate (pose updates per second). Photon's default is 10; 15-20 looks smoother for VR but uses more messages.")]
        public int SerializationRate = 15;
        [Tooltip("Per-platform overrides for the two rates above (e.g. lower on Android / Quest to save battery and messages).")]
        public List<PhotonVRPlatformRates> PlatformRates = new List<PhotonVRPlatformRates>();
        [Tooltip("Share UserIds in rooms (needed for identity checks, bans and mute lists that survive rejoining).")]
        public bool PublishUserIds = true;

        [Header("Reconnect")]
        [Tooltip("Automatically reconnect after an unexpected disconnect (timeouts, network loss).")]
        public bool AutoReconnect = true;
        [Tooltip("After reconnecting, join the same queue / private room you were in.")]
        public bool RejoinAfterReconnect = true;
        [Tooltip("Give up after this many attempts in a row.")]
        public int MaxReconnectAttempts = 5;
        [Tooltip("Seconds before the first reconnect attempt. Doubles every attempt (capped at 30s).")]
        public float ReconnectDelay = 2f;

        [Header("Security")]
        [Tooltip("Keep the IDs encrypted in memory with a per-session key and scrub them from Photon while connected. Off by default. Can also be enabled on the settings asset.")]
        public bool HardenMemory = false;

        [Header("Other")]
        // This should always be true
        [Tooltip("If the user shall connect when this object has awoken")]
        public bool ConnectOnAwake = true;
        [Tooltip("If the user shall join a room when they connect")]
        public bool JoinRoomOnConnect = true;
        [Tooltip("Log connection progress to the console.")]
        public bool VerboseLogging = true;

        /// <summary>Invoked when connected to the master server.</summary>
        [Header("Events")]
        public UnityEvent OnConnectedEvent = new UnityEvent();
        /// <summary>Invoked after joining a room.</summary>
        public UnityEvent OnJoinedRoomEvent = new UnityEvent();
        /// <summary>Invoked after leaving a room.</summary>
        public UnityEvent OnLeftRoomEvent = new UnityEvent();
        /// <summary>Invoked after disconnecting.</summary>
        public UnityEvent OnDisconnectedEvent = new UnityEvent();

        #endregion

        /// <summary>The local networked player object (set when it spawns, cleared when you leave).</summary>
        [NonSerialized]
        public PhotonVRPlayer LocalPlayer;

        #region Static events

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
        /// <summary>Raised when the local player's cosmetics change (a copy of the dictionary).</summary>
        public static event Action<Dictionary<string, string>> CosmeticsChanged;
        /// <summary>Raised when the local player's colour changes.</summary>
        public static event Action<Color> ColourChanged;
        /// <summary>Raised when the local networked player object spawns.</summary>
        public static event Action<PhotonVRPlayer> LocalPlayerSpawned;
        /// <summary>Raised when another player joins the current room.</summary>
        public static event Action<Photon.Realtime.Player> PlayerJoined;
        /// <summary>Raised when another player leaves the current room.</summary>
        public static event Action<Photon.Realtime.Player> PlayerLeft;

        #endregion

        private ConnectionState State = ConnectionState.Disconnected;

        /// <summary>The current connection state (same value as <see cref="GetConnectionState"/>).</summary>
        public ConnectionState CurrentState => State;

        #region Unity lifecycle

        private void Awake()
        {
            if (Manager != null && Manager != this)
            {
                PhotonVRLog.Error("There can't be multiple PhotonVRManagers in a scene - destroying the duplicate on " + name, this);
                Destroy(gameObject);
                return;
            }

            Manager = this;
            PhotonVRLog.Verbose = VerboseLogging;
            DontDestroyOnLoad(transform.root.gameObject);

            if (Settings == null)
                Settings = PhotonVRSettings.LoadDefault();

            InitMemoryHardening();
            PhotonVRSecurity.Initialize();
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

        private void OnValidate()
        {
            PhotonVRLog.Verbose = VerboseLogging;
#if UNITY_EDITOR
            if (migratedLegacyIds)
            {
                migratedLegacyIds = false;
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    UnityEditor.EditorUtility.SetDirty(this);
                    if (gameObject.scene.IsValid())
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
                    PhotonVRLog.Warn("Moved plaintext App IDs on " + name + " into encrypted storage - save the scene/prefab to remove the plaintext from disk.", this);
                };
            }
#endif
        }

        [NonSerialized] private bool migratedLegacyIds;

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // Never write plaintext IDs back to disk.
            MigrateLegacyIds();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacyIds();

        private void MigrateLegacyIds()
        {
            if (!string.IsNullOrEmpty(legacyAppId))
            {
                if (!appId.HasValue) appId.Set(legacyAppId);
                legacyAppId = string.Empty;
                migratedLegacyIds = true;
            }
            if (!string.IsNullOrEmpty(legacyVoiceAppId))
            {
                if (!voiceAppId.HasValue) voiceAppId.Set(legacyVoiceAppId);
                legacyVoiceAppId = string.Empty;
                migratedLegacyIds = true;
            }
        }

        private void Update()
        {
            if (Manager == this)
                PhotonVRSaveSystem.Tick();
        }

        private void OnDestroy()
        {
            if (Manager == this)
                Manager = null;
        }

        /// <summary>
        /// Fills Head / LeftHand / RightHand when they are not assigned: Head falls back to Camera.main, the hands
        /// are searched by name (see <see cref="PhotonVRRigFinder"/>). Safe to call any time, e.g. after loading a scene with a new rig.
        /// </summary>
        public void ResolveRigReferences()
        {
            if (Head == null) Head = PhotonVRRigFinder.FindHead();
            if (LeftHand == null) LeftHand = PhotonVRRigFinder.FindLeftHand();
            if (RightHand == null) RightHand = PhotonVRRigFinder.FindRightHand();
        }

        #endregion

#if UNITY_EDITOR
        /// <summary>Editor helper (called by the inspector): auto-assigns the rig and copies IDs from PhotonServerSettings.</summary>
        public void CheckDefaultValues()
        {
            bool b = CheckForRig(this);
            if (b)
            {
                AppSettings serverSettings = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
                if (serverSettings == null) return;
                if (!HasAppId && !string.IsNullOrEmpty(serverSettings.AppIdRealtime))
                    AppId = serverSettings.AppIdRealtime;
                if (!HasVoiceAppId && !string.IsNullOrEmpty(serverSettings.AppIdVoice))
                    VoiceAppId = serverSettings.AppIdVoice;
            }
        }

        private bool CheckForRig(PhotonVRManager manager)
        {
            bool missing = manager.Head == null || manager.LeftHand == null || manager.RightHand == null;
            if (manager.Head == null) manager.Head = PhotonVRRigFinder.FindHead();
            if (manager.LeftHand == null) manager.LeftHand = PhotonVRRigFinder.FindLeftHand();
            if (manager.RightHand == null) manager.RightHand = PhotonVRRigFinder.FindRightHand();
            return missing || !HasAppId || !HasVoiceAppId;
        }
#endif

        #region State helpers

        /// <summary>Gets the connection state</summary>
        public static ConnectionState GetConnectionState()
        {
            return Manager != null ? Manager.State : ConnectionState.Disconnected;
        }

        /// <summary>True while inside a room.</summary>
        public static bool IsInRoom => PhotonNetwork.InRoom;

        /// <summary>Players in the current room (0 when not in a room).</summary>
        public static int PlayerCount => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;

        private void SetState(ConnectionState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        // Makes sure <see cref="Manager"/> is set (finds one in the scene) and logs a helpful error otherwise.
        private static bool EnsureManager(string caller)
        {
            if (Manager != null)
                return true;
            Manager = FindObjectOfType<PhotonVRManager>();
            if (Manager != null)
                return true;
            PhotonVRLog.Error($"{caller}: there is no PhotonVRManager in the scene. Drop in the PhotonVRManager prefab or use Tools > PhotonVR > Setup Scene.");
            return false;
        }

        internal static void NotifyLocalPlayerSpawned(PhotonVRPlayer player)
        {
            if (Manager != null)
                Manager.LocalPlayer = player;
            LocalPlayerSpawned?.Invoke(player);
        }

        #endregion
    }

    /// <summary>Send / serialization rates for one platform (0 = keep the manager value).</summary>
    [Serializable]
    public class PhotonVRPlatformRates
    {
        public RuntimePlatform Platform = RuntimePlatform.Android;
        public int SendRate = 20;
        public int SerializationRate = 10;
    }
}
