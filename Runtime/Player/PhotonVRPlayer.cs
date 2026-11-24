using System;
using System.Collections.Generic;
using UnityEngine;

using Photon.Pun;
using Photon.Voice.PUN;

using TMPro;

namespace Photon.VR.Player
{
    /// <summary>
    /// The networked player model, spawned by <see cref="PlayerSpawner"/> from <c>Resources/PhotonVR/Player</c>.
    /// <list type="bullet">
    /// <item>Pose: <see cref="PhotonVRPoseSync"/> makes the owner's Head follow the Main Camera and the hands follow the
    /// XR controllers, and interpolates them for everyone else.</item>
    /// <item>Voice: <see cref="PhotonVRPlayerVoice"/> (PhotonVoiceView + Speaker).</item>
    /// <item>Name / colour / cosmetics: read from the owner's custom properties ("Colour", "Cosmetics") and applied
    /// to <see cref="NameText"/>, <see cref="ColourObjects"/> and <see cref="CosmeticSlots"/>.</item>
    /// </list>
    /// Missing helper components are added automatically, so prefabs made for the original PhotonVR keep working.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PhotonVR/PhotonVR Player")]
    public class PhotonVRPlayer : MonoBehaviourPun
    {
        [Header("Objects")]
        public Transform Head;
        public Transform Body;
        public Transform LeftHand;
        public Transform RightHand;
        [Tooltip("The objects that will get the colour of the player applied to them")]
        public List<MeshRenderer> ColourObjects;

        [Space]
        [Tooltip("Feel free to add as many slots as you feel necessary. SlotName is the key used by PhotonVRManager.SetCosmetic; Object is the parent of the cosmetic models (each child named by its cosmetic ID).")]
        public List<CosmeticSlot> CosmeticSlots = new List<CosmeticSlot>();

        [Header("Other")]
        public TextMeshPro NameText;
        public bool HideLocalPlayer = true;

        [Header("Photon VR Plus")]
        [Tooltip("Sync head + hands with PhotonVRPoseSync (snapshot interpolation). Legacy PhotonTransformViews on the head/hands are turned off.")]
        public bool SmoothSync = true;
        [Tooltip("Add PhotonVRPlayerVoice (PhotonVoiceView + Speaker) at runtime when the prefab has none.")]
        public bool AutoSetupVoice = true;

        /// <summary>The name shown for this player (their own published name, filtered).</summary>
        public string DisplayName { get; private set; } = "";

        /// <summary>Every PhotonVRPlayer currently spawned (local and remote).</summary>
        public static IReadOnlyList<PhotonVRPlayer> All => all;
        private static readonly List<PhotonVRPlayer> all = new List<PhotonVRPlayer>();

        /// <summary>Raised on any player after its name / colour / cosmetics were applied.</summary>
        public static event Action<PhotonVRPlayer> ValuesRefreshed;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();
            ValuesRefreshed = null;
        }

        /// <summary>The head / hand sync component (added automatically when missing).</summary>
        public PhotonVRPoseSync PoseSync { get; private set; }

        /// <summary>The voice component (null when <see cref="AutoSetupVoice"/> is off and the prefab has none).</summary>
        public PhotonVRPlayerVoice Voice { get; private set; }

        /// <summary>True if this is the local player's object.</summary>
        public bool IsLocal => photonView != null && photonView.IsMine;

        /// <summary>The Photon player who owns this object (null for room objects).</summary>
        public Photon.Realtime.Player Owner => photonView != null ? photonView.Owner : null;

        /// <summary>The PhotonVoiceView used for this player (may be null without voice).</summary>
        public PhotonVoiceView VoiceView => Voice != null ? Voice.VoiceView : GetComponent<PhotonVoiceView>();

        /// <summary>True while this player is talking.</summary>
        public bool IsSpeaking => Voice != null && Voice.IsSpeaking;

        /// <summary>Optional object shown while this player is talking (forwarded to <see cref="PhotonVRPlayerVoice"/>).</summary>
        public GameObject SpeakingIndicator
        {
            get => Voice != null ? Voice.SpeakingIndicator : null;
            set { if (Voice != null) Voice.SpeakingIndicator = value; }
        }

        /// <summary>Forwarded to <see cref="PhotonVRPoseSync.InterpolationBackTime"/>.</summary>
        public float InterpolationBackTime
        {
            get => PoseSync != null ? PoseSync.InterpolationBackTime : 0f;
            set { if (PoseSync != null) PoseSync.InterpolationBackTime = value; }
        }

        /// <summary>Forwarded to <see cref="PhotonVRPoseSync.MaxExtrapolation"/>.</summary>
        public float MaxExtrapolation
        {
            get => PoseSync != null ? PoseSync.MaxExtrapolation : 0f;
            set { if (PoseSync != null) PoseSync.MaxExtrapolation = value; }
        }

        /// <summary>Forwarded to <see cref="PhotonVRPoseSync.TeleportDistance"/>.</summary>
        public float TeleportDistance
        {
            get => PoseSync != null ? PoseSync.TeleportDistance : 0f;
            set { if (PoseSync != null) PoseSync.TeleportDistance = value; }
        }

        /// <summary>The cosmetics currently applied on this player (slot to ID), as received over the network.</summary>
        public IReadOnlyDictionary<string, string> AppliedCosmetics => appliedCosmetics;
        private readonly Dictionary<string, string> appliedCosmetics = new Dictionary<string, string>();

        private void Awake()
        {
            all.Add(this);
            AutoAssignReferences();
            SetupPoseSync();
            if (AutoSetupVoice)
                SetupVoice();

            if (photonView.IsMine)
            {
                if (PhotonVRManager.Manager != null)
                    PhotonVRManager.Manager.ResolveRigReferences();
                PhotonVRManager.NotifyLocalPlayerSpawned(this);

                if (HideLocalPlayer)
                    SetModelVisible(false);

                PoseSync.FollowRig();
            }

            // It will delete automatically when you leave the room
            DontDestroyOnLoad(gameObject);

            _RefreshPlayerValues();
        }

        private void OnDestroy()
        {
            all.Remove(this);
            if (PhotonVRManager.Manager != null && PhotonVRManager.Manager.LocalPlayer == this)
                PhotonVRManager.Manager.LocalPlayer = null;
        }

        /// <summary>Shows or hides the head, body, hands and name tag (used for <see cref="HideLocalPlayer"/>).</summary>
        public void SetModelVisible(bool visible)
        {
            SetActiveSafe(Head, visible);
            SetActiveSafe(Body, visible);
            SetActiveSafe(RightHand, visible);
            SetActiveSafe(LeftHand, visible);
            if (NameText != null) NameText.gameObject.SetActive(visible);
        }

        private static void SetActiveSafe(Transform t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }

        // Fills missing references by child names (Head, Body, LeftHand, RightHand, *Cosmetics).
        private void AutoAssignReferences()
        {
            if (Head == null) Head = FindChild("Head");
            if (Body == null) Body = FindChild("Body");
            if (LeftHand == null) LeftHand = FindChild("LeftHand");
            if (RightHand == null) RightHand = FindChild("RightHand");
            if (NameText == null) NameText = GetComponentInChildren<TextMeshPro>(true);
            if (ColourObjects == null) ColourObjects = new List<MeshRenderer>();
            if (CosmeticSlots == null) CosmeticSlots = new List<CosmeticSlot>();

            if (CosmeticSlots.Count == 0)
            {
                foreach (string slot in PhotonVRCosmeticSlots.All)
                {
                    Transform container = FindChild(PhotonVRCosmeticSlots.ContainerName(slot));
                    if (container != null)
                        CosmeticSlots.Add(new CosmeticSlot { SlotName = slot, Object = container });
                }
            }
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == childName)
                    return t;
            return null;
        }

        private void SetupPoseSync()
        {
            PoseSync = GetComponent<PhotonVRPoseSync>();
            if (PoseSync == null)
                PoseSync = gameObject.AddComponent<PhotonVRPoseSync>();
            if (PoseSync.Head == null) PoseSync.Head = Head;
            if (PoseSync.LeftHand == null) PoseSync.LeftHand = LeftHand;
            if (PoseSync.RightHand == null) PoseSync.RightHand = RightHand;

            if (SmoothSync)
                PoseSync.ConfigureObservation();
        }

        private void SetupVoice()
        {
            Voice = GetComponent<PhotonVRPlayerVoice>();
            if (Voice != null)
                return;
            // Its Awake creates a missing Speaker under our Head.
            Voice = gameObject.AddComponent<PhotonVRPlayerVoice>();
        }

        /// <summary>Asks every client to re-apply this player's name, colour and cosmetics (RPC to All).</summary>
        public void RefreshPlayerValues()
        {
            if (PhotonNetwork.InRoom && photonView != null && photonView.ViewID != 0)
                photonView.RPC(PhotonVRKeys.RefreshRpc, RpcTarget.All);
            else
                _RefreshPlayerValues();
        }

        [PunRPC]
        private void RPCRefreshPlayerValues(PhotonMessageInfo info)
        {
            // Anyone can send this RPC, so cap it per sender. It only re-reads the owner's own values, but a
            // flood would still rebuild cosmetics over and over. (Same RPC name, so older clients still match.)
            if (info.Sender != null && !info.Sender.IsLocal && !PhotonVRRpc.Rate(info, "refresh", 2f, 6f)) return;
            _RefreshPlayerValues();
        }

        /// <summary>Re-applies name, colour and cosmetics from the owner's custom properties on this client only (no RPC).</summary>
        public void ApplyPlayerValues() => _RefreshPlayerValues();

        private void _RefreshPlayerValues()
        {
            Photon.Realtime.Player owner = Owner;
            if (owner == null)
                return;

            // Prefer the profile the owner published themselves (see PhotonVRProfiles); custom properties can be
            // written by anyone, so they are only used for clients without a profile (e.g. the original PhotonVR).
            string colourJson = null;
            Dictionary<string, string> cosmetics = null, tints = null;
            if (PhotonVRProfiles.TryGet(owner.ActorNumber, out PhotonVRProfile profile))
            {
                colourJson = profile.ColourJson;
                cosmetics = profile.Cosmetics;
                tints = profile.Tints;
            }
            else
            {
                if (owner.CustomProperties.TryGetValue(PhotonVRKeys.Colour, out object colourObj)) colourJson = colourObj as string;
                if (owner.CustomProperties.TryGetValue(PhotonVRKeys.Cosmetics, out object cosObj)) cosmetics = PhotonVRCosmeticUtility.ToStringDictionary(cosObj);
                if (owner.CustomProperties.TryGetValue(PhotonVRKeys.CosmeticTints, out object tintObj)) tints = PhotonVRCosmeticUtility.ToStringDictionary(tintObj);
            }

            // Name (filtered, rich text stripped)
            DisplayName = PhotonVRProfiles.DisplayName(owner);
            if (NameText != null)
                NameText.text = DisplayName;

            // Colour
            if (!string.IsNullOrEmpty(colourJson))
                ApplyColour(ParseColour(colourJson));

            // Cosmetics
            if (cosmetics != null)
            {
                foreach (KeyValuePair<string, string> cosmetic in cosmetics)
                {
                    appliedCosmetics[cosmetic.Key] = cosmetic.Value;
                    CosmeticSlot slot = GetSlot(cosmetic.Key);
                    if (slot != null)
                        slot.Equip(cosmetic.Value);
                }
            }
            if (CosmeticSlots != null)
                foreach (CosmeticSlot slot in CosmeticSlots)
                    if (slot != null)
                        PhotonVRCosmeticUtility.ApplyTint(slot.Object, tints != null && tints.TryGetValue(slot.SlotName ?? "", out string t) ? t : null);

            ValuesRefreshed?.Invoke(this);
        }

        private static Color ParseColour(string json)
        {
            try { return JsonUtility.FromJson<Color>(json); }
            catch { return Color.white; }
        }

        private void ApplyColour(Color colour)
        {
            if (ColourObjects == null) return;
            foreach (MeshRenderer renderer in ColourObjects)
                if (renderer != null)
                    renderer.material.color = colour;
        }

        /// <summary>Finds a cosmetic slot by name (null if this prefab has no such slot).</summary>
        public CosmeticSlot GetSlot(string slotName)
        {
            if (CosmeticSlots == null || string.IsNullOrEmpty(slotName)) return null;
            foreach (CosmeticSlot slot in CosmeticSlots)
                if (slot != null && slot.SlotName == slotName)
                    return slot;
            return null;
        }

        /// <summary>The cosmetic ID currently equipped in a slot on this player ("" if none).</summary>
        public string GetEquipped(string slotName) =>
            appliedCosmetics.TryGetValue(slotName ?? string.Empty, out string id) ? id ?? string.Empty : string.Empty;

        /// <summary>A named cosmetic slot: <see cref="Object"/> holds one child per cosmetic, named by its ID.</summary>
        [Serializable]
        public class CosmeticSlot
        {
            public string SlotName;
            public Transform Object;

            /// <summary>Enables the cosmetic <paramref name="cosmeticId"/> and disables all others ("" clears the slot).
            /// IDs registered in <see cref="PhotonVRCosmeticRegistry"/> are spawned into the slot first.</summary>
            public void Equip(string cosmeticId)
            {
                PhotonVRCosmeticRegistry.EnsureLoaded(Object, SlotName, cosmeticId);
                PhotonVRCosmeticUtility.Equip(Object, cosmeticId);
            }

            /// <summary>IDs of every cosmetic available in this slot.</summary>
            public List<string> GetAvailableCosmetics() => PhotonVRCosmeticUtility.GetAvailable(Object);
        }
    }
}
