// Photon VR Plus - a revamp of PhotonVR by fchb1239 (MIT). See LICENSE.md.
// Every public member of the original PhotonVRPlayer is kept with the same name and signature.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Photon.Pun;
using Photon.Voice.PUN;
using Photon.Voice.Unity;

using TMPro;

namespace Photon.VR.Player
{
    /// <summary>
    /// The networked player model (spawned by <see cref="PlayerSpawner"/> from Resources/PhotonVR/Player).
    /// On the owner it follows the local rig: Head = Main Camera / PhotonVRManager.Head, hands = the XR controllers.
    /// The pose is sent through the PhotonView (this component is observed) and interpolated on remote clients.
    /// Name, colour and cosmetics come from the owner's Photon custom properties ("Colour", "Cosmetics").
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [DisallowMultipleComponent]
    public class PhotonVRPlayer : MonoBehaviourPun, IPunObservable
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

        [Header("Sync (Photon VR Plus)")]
        [Tooltip("Sync head + hands through this component with snapshot interpolation. Any PhotonTransformView on Head/LeftHand/RightHand gets disabled automatically.")]
        public bool SmoothSync = true;
        [Tooltip("How far in the past remote players are rendered, in seconds. 0 = automatic (about 2 serialization intervals).")]
        public float InterpolationBackTime = 0f;
        [Tooltip("How long, in seconds, positions are extrapolated when packets are late.")]
        public float MaxExtrapolation = 0.2f;
        [Tooltip("If a remote part moves farther than this in one update, it snaps instead of sliding (respawns / teleports).")]
        public float TeleportDistance = 4f;

        [Header("Voice (Photon VR Plus)")]
        [Tooltip("Adds a PhotonVoiceView + Speaker at runtime if the prefab has none.")]
        public bool AutoSetupVoice = true;
        [Tooltip("Optional object shown while this player is talking.")]
        public GameObject SpeakingIndicator;

        /// <summary>Every PhotonVRPlayer currently spawned (local and remote).</summary>
        public static IReadOnlyList<PhotonVRPlayer> All => all;
        private static readonly List<PhotonVRPlayer> all = new List<PhotonVRPlayer>();

        /// <summary>Raised on any player after its name / colour / cosmetics were applied.</summary>
        public static event Action<PhotonVRPlayer> ValuesRefreshed;

        /// <summary>True if this is the local player's object.</summary>
        public bool IsLocal => photonView != null && photonView.IsMine;
        /// <summary>The Photon player who owns this object (may be null for room objects).</summary>
        public Photon.Realtime.Player Owner => photonView != null ? photonView.Owner : null;
        /// <summary>The PhotonVoiceView used for this player (may be null without voice).</summary>
        public PhotonVoiceView VoiceView { get; private set; }
        /// <summary>True while this (remote) player's voice is playing, or the local player is transmitting.</summary>
        public bool IsSpeaking => VoiceView != null && (IsLocal ? VoiceView.IsRecording : VoiceView.IsSpeaking);
        /// <summary>The cosmetics currently applied on this player (slot to ID), as received over the network.</summary>
        public IReadOnlyDictionary<string, string> AppliedCosmetics => appliedCosmetics;
        private readonly Dictionary<string, string> appliedCosmetics = new Dictionary<string, string>();

        // ---------- interpolation buffer ----------
        private struct Snapshot
        {
            public double Time;
            public Vector3 HeadPos, LeftPos, RightPos;
            public Quaternion HeadRot, LeftRot, RightRot;
        }
        private const int BufferSize = 12;
        private readonly Snapshot[] buffer = new Snapshot[BufferSize];
        private int bufferCount;

        #region Unity lifecycle

        private void Awake()
        {
            all.Add(this);
            AutoAssignReferences();
            ConfigureObservation();

            if (AutoSetupVoice)
                SetupVoice();

            if (photonView.IsMine)
            {
                if (PhotonVRManager.Manager != null)
                    PhotonVRManager.Manager.ResolveRigReferences();
                PhotonVRManager.NotifyLocalPlayerSpawned(this);

                if (HideLocalPlayer)
                {
                    SetActiveSafe(Head, false);
                    SetActiveSafe(Body, false);
                    SetActiveSafe(RightHand, false);
                    SetActiveSafe(LeftHand, false);
                    if (NameText != null) NameText.gameObject.SetActive(false);
                }

                FollowRig();
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

        private void Update()
        {
            if (photonView.IsMine)
                FollowRig();
            else if (SmoothSync)
                Interpolate();

            if (SpeakingIndicator != null)
            {
                bool speaking = IsSpeaking;
                if (SpeakingIndicator.activeSelf != speaking)
                    SpeakingIndicator.SetActive(speaking);
            }
        }

        private void LateUpdate()
        {
            // Run again after tracking updated the camera / controllers this frame,
            // so the pose we serialize is as fresh as possible.
            if (photonView.IsMine)
                FollowRig();
        }

        #endregion

        #region Setup

        private static void SetActiveSafe(Transform t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }

        /// <summary>Fills missing references by child names (Head, Body, LeftHand, RightHand, *Cosmetics).</summary>
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
                // Build the default 5 slots from the standard container names.
                foreach (string slot in PhotonVRCosmeticSlots.All)
                {
                    Transform container = FindChild(slot + "Cosmetics");
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

        /// <summary>
        /// Makes this component the observed pose source and turns off legacy PhotonTransformViews on
        /// the head and hands (the original prefab used three of them). Runs before the first serialization.
        /// </summary>
        private void ConfigureObservation()
        {
            if (!SmoothSync)
                return;

            PhotonView view = photonView;
            if (view.ObservedComponents == null)
                view.ObservedComponents = new List<Component>();

            foreach (PhotonTransformView ptv in GetComponentsInChildren<PhotonTransformView>(true))
            {
                Transform t = ptv.transform;
                if (t == Head || t == LeftHand || t == RightHand)
                {
                    view.ObservedComponents.Remove(ptv);
                    ptv.enabled = false;
                }
            }

            if (!view.ObservedComponents.Contains(this))
                view.ObservedComponents.Insert(0, this);

            view.observableSearch = PhotonView.ObservableSearch.Manual;
            if (view.Synchronization == ViewSynchronization.Off)
                view.Synchronization = ViewSynchronization.UnreliableOnChange;
        }

        private void SetupVoice()
        {
            VoiceView = GetComponent<PhotonVoiceView>();
            if (VoiceView != null)
                return;

            // Older/custom prefabs without voice: add a Speaker on the head and a PhotonVoiceView on the root.
            if (GetComponentInChildren<Speaker>(true) == null)
            {
                var speakerGo = new GameObject("Speaker");
                speakerGo.transform.SetParent(Head != null ? Head : transform, false);
                var source = speakerGo.AddComponent<AudioSource>();
                source.spatialBlend = 1f;
                source.playOnAwake = true;
                speakerGo.AddComponent<Speaker>();
            }
            VoiceView = gameObject.AddComponent<PhotonVoiceView>();
        }

        #endregion

        #region Pose: local follow + remote interpolation

        /// <summary>Copies the local rig (camera + controllers) onto Head / LeftHand / RightHand.</summary>
        private void FollowRig()
        {
            PhotonVRManager m = PhotonVRManager.Manager;
            Transform headSource = m != null && m.Head != null ? m.Head : (Camera.main != null ? Camera.main.transform : null);
            Transform leftSource = m != null ? m.LeftHand : null;
            Transform rightSource = m != null ? m.RightHand : null;

            CopyPose(headSource, Head);
            CopyPose(leftSource, LeftHand);
            CopyPose(rightSource, RightHand);
        }

        private static void CopyPose(Transform from, Transform to)
        {
            if (from == null || to == null) return;
            to.SetPositionAndRotation(from.position, from.rotation);
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                WritePose(stream, Head);
                WritePose(stream, LeftHand);
                WritePose(stream, RightHand);
            }
            else
            {
                Snapshot s = new Snapshot { Time = info.SentServerTime };
                ReadPose(stream, out s.HeadPos, out s.HeadRot);
                ReadPose(stream, out s.LeftPos, out s.LeftRot);
                ReadPose(stream, out s.RightPos, out s.RightRot);
                AddSnapshot(s);
            }
        }

        private static void WritePose(PhotonStream stream, Transform t)
        {
            stream.SendNext(t != null ? t.position : Vector3.zero);
            stream.SendNext(t != null ? t.rotation : Quaternion.identity);
        }

        private static void ReadPose(PhotonStream stream, out Vector3 pos, out Quaternion rot)
        {
            pos = (Vector3)stream.ReceiveNext();
            rot = (Quaternion)stream.ReceiveNext();
        }

        private void AddSnapshot(Snapshot s)
        {
            if (bufferCount > 0)
            {
                Snapshot newest = buffer[0];
                // Out-of-order packet: ignore. Clock wrap / big jump back: reset.
                if (s.Time <= newest.Time)
                {
                    if (newest.Time - s.Time < 1000.0) return;
                    bufferCount = 0;
                }
                else if ((s.HeadPos - newest.HeadPos).sqrMagnitude > TeleportDistance * TeleportDistance)
                {
                    bufferCount = 0; // teleport: drop history so we snap
                }
            }

            for (int i = Mathf.Min(bufferCount, BufferSize - 1); i > 0; i--)
                buffer[i] = buffer[i - 1];
            buffer[0] = s;
            bufferCount = Mathf.Min(bufferCount + 1, BufferSize);

            if (bufferCount == 1)
                ApplySnapshot(s); // first packet: snap
        }

        private void Interpolate()
        {
            if (bufferCount == 0) return;

            float back = InterpolationBackTime > 0f
                ? InterpolationBackTime
                : 2f / Mathf.Max(1, PhotonNetwork.SerializationRate);
            double renderTime = PhotonNetwork.Time - back;

            Snapshot newest = buffer[0];
            if (renderTime >= newest.Time || bufferCount == 1)
            {
                // Late packets: extrapolate a little using the last two snapshots.
                if (bufferCount > 1 && MaxExtrapolation > 0f)
                {
                    Snapshot prev = buffer[1];
                    double span = newest.Time - prev.Time;
                    float extra = (float)Math.Min(renderTime - newest.Time, MaxExtrapolation);
                    if (span > 0.0001 && extra > 0f)
                    {
                        float k = (float)(extra / span);
                        Apply(Head, newest.HeadPos + (newest.HeadPos - prev.HeadPos) * k, newest.HeadRot);
                        Apply(LeftHand, newest.LeftPos + (newest.LeftPos - prev.LeftPos) * k, newest.LeftRot);
                        Apply(RightHand, newest.RightPos + (newest.RightPos - prev.RightPos) * k, newest.RightRot);
                        return;
                    }
                }
                ApplySnapshot(newest);
                return;
            }

            // Find the two snapshots around renderTime (buffer[0] is newest).
            for (int i = 0; i < bufferCount - 1; i++)
            {
                Snapshot newer = buffer[i];
                Snapshot older = buffer[i + 1];
                if (older.Time <= renderTime)
                {
                    double span = newer.Time - older.Time;
                    float t = span > 0.0001 ? Mathf.Clamp01((float)((renderTime - older.Time) / span)) : 1f;
                    Apply(Head, Vector3.Lerp(older.HeadPos, newer.HeadPos, t), Quaternion.Slerp(older.HeadRot, newer.HeadRot, t));
                    Apply(LeftHand, Vector3.Lerp(older.LeftPos, newer.LeftPos, t), Quaternion.Slerp(older.LeftRot, newer.LeftRot, t));
                    Apply(RightHand, Vector3.Lerp(older.RightPos, newer.RightPos, t), Quaternion.Slerp(older.RightRot, newer.RightRot, t));
                    return;
                }
            }

            // renderTime is older than everything we have: use the oldest.
            ApplySnapshot(buffer[bufferCount - 1]);
        }

        private void ApplySnapshot(Snapshot s)
        {
            Apply(Head, s.HeadPos, s.HeadRot);
            Apply(LeftHand, s.LeftPos, s.LeftRot);
            Apply(RightHand, s.RightPos, s.RightRot);
        }

        private static void Apply(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t != null) t.SetPositionAndRotation(pos, rot);
        }

        #endregion

        #region Name / colour / cosmetics

        public void RefreshPlayerValues()
        {
            if (PhotonNetwork.InRoom && photonView != null && photonView.ViewID != 0)
                photonView.RPC("RPCRefreshPlayerValues", RpcTarget.All);
            else
                _RefreshPlayerValues();
        }

        [PunRPC]
        private void RPCRefreshPlayerValues()
        {
            _RefreshPlayerValues();
        }

        /// <summary>Re-applies name, colour and cosmetics from the owner's custom properties on this client only (no RPC).</summary>
        public void ApplyPlayerValues() => _RefreshPlayerValues();

        private void _RefreshPlayerValues()
        {
            Photon.Realtime.Player owner = photonView != null ? photonView.Owner : null;
            if (owner == null)
                return;

            // Name
            if (NameText != null)
                NameText.text = owner.NickName;

            // Colour
            if (owner.CustomProperties.TryGetValue(PhotonVRKeys.Colour, out object colourObj) && colourObj is string colourJson && !string.IsNullOrEmpty(colourJson))
            {
                Color colour;
                try { colour = JsonUtility.FromJson<Color>(colourJson); }
                catch { colour = Color.white; }

                if (ColourObjects != null)
                    foreach (MeshRenderer renderer in ColourObjects)
                        if (renderer != null)
                            renderer.material.color = colour;
            }

            // Cosmetics
            if (owner.CustomProperties.TryGetValue(PhotonVRKeys.Cosmetics, out object cosObj))
            {
                Dictionary<string, string> cosmetics = ToStringDictionary(cosObj);
                foreach (KeyValuePair<string, string> cosmetic in cosmetics)
                {
                    appliedCosmetics[cosmetic.Key] = cosmetic.Value;
                    foreach (CosmeticSlot slot in CosmeticSlots)
                        if (slot != null && slot.SlotName == cosmetic.Key)
                            slot.Equip(cosmetic.Value);
                }
            }

            ValuesRefreshed?.Invoke(this);
        }

        private static Dictionary<string, string> ToStringDictionary(object value)
        {
            if (value is Dictionary<string, string> typed)
                return typed;

            var result = new Dictionary<string, string>();
            if (value is IDictionary dict)
                foreach (DictionaryEntry e in dict)
                    if (e.Key != null)
                        result[e.Key.ToString()] = e.Value != null ? e.Value.ToString() : string.Empty;
            return result;
        }

        /// <summary>Finds a cosmetic slot by name (null if this prefab has no such slot).</summary>
        public CosmeticSlot GetSlot(string slotName)
        {
            if (CosmeticSlots == null) return null;
            foreach (CosmeticSlot slot in CosmeticSlots)
                if (slot != null && slot.SlotName == slotName)
                    return slot;
            return null;
        }

        /// <summary>The cosmetic ID currently equipped in a slot on this player ("" if none).</summary>
        public string GetEquipped(string slotName) =>
            appliedCosmetics.TryGetValue(slotName ?? string.Empty, out string id) ? id ?? string.Empty : string.Empty;

        #endregion

        [Serializable]
        public class CosmeticSlot
        {
            public string SlotName;
            public Transform Object;

            /// <summary>Enables the child named <paramref name="cosmeticId"/> and disables all others ("" clears the slot).</summary>
            public void Equip(string cosmeticId)
            {
                if (Object == null) return;
                foreach (Transform cos in Object)
                    cos.gameObject.SetActive(!string.IsNullOrEmpty(cosmeticId) && cos.name == cosmeticId);
            }

            /// <summary>Names of every cosmetic available in this slot.</summary>
            public List<string> GetAvailableCosmetics()
            {
                var list = new List<string>();
                if (Object != null)
                    foreach (Transform cos in Object)
                        list.Add(cos.name);
                return list;
            }
        }
    }
}
