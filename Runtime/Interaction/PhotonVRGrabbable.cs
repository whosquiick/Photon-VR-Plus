using System;

using Photon.Pun;
using UnityEngine;

namespace Photon.VR.Interaction
{
    /// <summary>
    /// A networked object that can be picked up and thrown. Whoever holds it owns its PhotonView and
    /// simulates it; everyone else follows the owner's stream. Updates from anyone but the owner are dropped.
    /// </summary>
    [RequireComponent(typeof(PhotonView), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class PhotonVRGrabbable : MonoBehaviourPun, IPunObservable, IPunOwnershipCallbacks
    {
        [Tooltip("Let another player grab this out of someone's hand.")]
        public bool AllowSteal = false;
        [Tooltip("Keep the grab offset instead of snapping to the hand.")]
        public bool KeepOffset = true;
        public float ThrowMultiplier = 1f;
        [Tooltip("Cap on throw and synced speed (m/s). Also used to reject bogus network data.")]
        public float MaxSpeed = 25f;
        [Tooltip("How fast remote copies catch up with the owner's position.")]
        public float FollowSharpness = 18f;
        [Tooltip("Remote copies further than this from the synced position snap instead of lerping.")]
        public float SnapDistance = 3f;

        public event Action<PhotonVRGrabbable> Grabbed;
        public event Action<PhotonVRGrabbable> Released;

        public bool IsHeld { get; private set; }
        public bool IsHeldLocally => IsHeld && photonView.IsMine && holder != null;
        public Photon.Realtime.Player Holder => IsHeld ? photonView.Owner : null;

        private Rigidbody body;
        private Transform holder;
        private Transform pendingHolder;
        private Vector3 offsetPosition;
        private Quaternion offsetRotation = Quaternion.identity;
        private bool wasKinematic;

        private const int VelocitySamples = 6;
        private readonly Vector3[] velocities = new Vector3[VelocitySamples];
        private readonly Vector3[] angularVelocities = new Vector3[VelocitySamples];
        private int sampleIndex;
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        private Vector3 netPosition;
        private Quaternion netRotation = Quaternion.identity;
        private Vector3 netVelocity;
        private bool hasNetState;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            wasKinematic = body.isKinematic;
            if (photonView.OwnershipTransfer == OwnershipOption.Fixed)
                photonView.OwnershipTransfer = OwnershipOption.Request;
            if (!photonView.ObservedComponents.Contains(this))
                photonView.ObservedComponents.Add(this);
            if (photonView.Synchronization == ViewSynchronization.Off)
                photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            if (IsHeldLocally) Release();
        }

        /// <summary>Grabs with the given hand. Requests ownership first when someone else owns it.</summary>
        public bool Grab(Transform hand)
        {
            if (hand == null) return false;
            if (IsHeld && !photonView.IsMine && !AllowSteal) return false;
            if (PhotonNetwork.InRoom && !photonView.IsMine)
            {
                pendingHolder = hand;
                photonView.RequestOwnership();
                return true;
            }
            Attach(hand);
            return true;
        }

        /// <summary>Lets go, throwing with the hand's recent velocity.</summary>
        public void Release()
        {
            pendingHolder = null;
            if (holder == null) return;
            holder = null;
            IsHeld = false;
            body.isKinematic = wasKinematic;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.ClampMagnitude(Average(velocities) * ThrowMultiplier, MaxSpeed);
                body.angularVelocity = Average(angularVelocities);
            }
            Released?.Invoke(this);
        }

        private void Attach(Transform hand)
        {
            pendingHolder = null;
            holder = hand;
            IsHeld = true;
            body.isKinematic = true;
            if (KeepOffset)
            {
                offsetRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
                offsetPosition = hand.InverseTransformPoint(transform.position);
            }
            else
            {
                offsetRotation = Quaternion.identity;
                offsetPosition = Vector3.zero;
            }
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            Array.Clear(velocities, 0, VelocitySamples);
            Array.Clear(angularVelocities, 0, VelocitySamples);
            Grabbed?.Invoke(this);
        }

        private void FixedUpdate()
        {
            if (holder != null)
            {
                Vector3 pos = holder.TransformPoint(offsetPosition);
                Quaternion rot = holder.rotation * offsetRotation;
                body.MovePosition(pos);
                body.MoveRotation(rot);
                TrackVelocity(pos, rot);
                return;
            }

            if (!PhotonNetwork.InRoom || photonView.IsMine || !hasNetState) return;

            // Remote copy: follow the owner's state, predicting a little with its velocity.
            body.isKinematic = true;
            Vector3 target = netPosition + netVelocity * Time.fixedDeltaTime;
            if ((target - body.position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                body.position = target;
                body.rotation = netRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-FollowSharpness * Time.fixedDeltaTime);
                body.MovePosition(Vector3.Lerp(body.position, target, t));
                body.MoveRotation(Quaternion.Slerp(body.rotation, netRotation, t));
            }
        }

        private void TrackVelocity(Vector3 pos, Quaternion rot)
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            velocities[sampleIndex] = (pos - lastPosition) / dt;
            (rot * Quaternion.Inverse(lastRotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angularVelocities[sampleIndex] = float.IsInfinity(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / dt);
            sampleIndex = (sampleIndex + 1) % VelocitySamples;
            lastPosition = pos;
            lastRotation = rot;
        }

        private static Vector3 Average(Vector3[] values)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 v in values) sum += v;
            return sum / values.Length;
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(body.position);
                stream.SendNext(body.rotation);
                stream.SendNext(holder != null ? Average(velocities) : body.velocity);
                stream.SendNext(IsHeld);
                return;
            }

            Vector3 pos = (Vector3)stream.ReceiveNext();
            Quaternion rot = (Quaternion)stream.ReceiveNext();
            Vector3 vel = (Vector3)stream.ReceiveNext();
            bool held = (bool)stream.ReceiveNext();

            if (info.Sender != null && info.Sender != photonView.Owner)
            {
                PhotonVRSecurity.Report(info.Sender, PhotonVRViolationType.NotOwner, $"grabbable {photonView.ViewID}");
                return;
            }
            if (!PhotonVRSecurity.CheckRate(info.Sender, "grabbable" + photonView.ViewID, 90f, 30f)) return;
            if (!Valid(pos) || !Valid(vel) || float.IsNaN(rot.x) || float.IsNaN(rot.w)) return;

            netPosition = pos;
            netRotation = rot;
            netVelocity = Vector3.ClampMagnitude(vel, MaxSpeed);
            IsHeld = held;
            hasNetState = true;
        }

        private static bool Valid(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && v.sqrMagnitude < 1e10f;

        public void OnOwnershipRequest(PhotonView targetView, Photon.Realtime.Player requestingPlayer)
        {
            if (targetView != photonView || !photonView.IsMine || requestingPlayer == null) return;
            if (!PhotonVRSecurity.CheckRate(requestingPlayer, "grab-request", 4f, 8f)) return;
            if (IsHeldLocally && !AllowSteal) return;
            if (PhotonVRMatchmaking.IsSpectator(requestingPlayer)) return;
            if (IsHeldLocally) Release();
            photonView.TransferOwnership(requestingPlayer);
        }

        public void OnOwnershipTransfered(PhotonView targetView, Photon.Realtime.Player previousOwner)
        {
            if (targetView != photonView) return;
            if (photonView.IsMine)
            {
                body.position = transform.position;
                if (pendingHolder != null) Attach(pendingHolder);
                else body.isKinematic = wasKinematic;
            }
            else if (holder != null)
            {
                // Taken from us; stop driving it.
                holder = null;
                IsHeld = true;
                Released?.Invoke(this);
            }
        }

        public void OnOwnershipTransferFailed(PhotonView targetView, Photon.Realtime.Player senderOfFailedRequest)
        {
            if (targetView == photonView) pendingHolder = null;
        }
    }
}
