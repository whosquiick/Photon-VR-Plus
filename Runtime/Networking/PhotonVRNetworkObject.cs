using System.Collections.Generic;

using Photon.Pun;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// "Network Object": drop it on anything you want synced. It adds a PhotonView and a PhotonTransformView (plus a
    /// PhotonRigidbodyView when the object has a Rigidbody), hooks them up and sets sensible defaults. Change the
    /// settings here and they are copied onto the Photon components for you.
    /// Called PhotonVRNetworkObject so it doesn't clash with Unity Netcode's NetworkObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PhotonView), typeof(PhotonTransformView))]
    [AddComponentMenu("PhotonVR/Network Object")]
    public class PhotonVRNetworkObject : MonoBehaviourPun
    {
        [Header("Transform")]
        public bool SyncPosition = true;
        public bool SyncRotation = true;
        public bool SyncScale = false;
        [Tooltip("Sync local position / rotation instead of world. Use it for objects parented to something that moves.")]
        public bool UseLocal = false;

        [Header("Rigidbody")]
        [Tooltip("With a Rigidbody, sync it with a PhotonRigidbodyView (smoother physics). The transform view then only handles scale.")]
        public bool SyncRigidbody = true;
        public bool SyncVelocity = true;
        public bool SyncAngularVelocity = true;
        [Tooltip("Snap instead of sliding when the synced position is far away (e.g. after a respawn).")]
        public bool TeleportIfFar = true;
        public float TeleportDistance = 3f;

        [Header("Photon View")]
        [Tooltip("Unreliable On Change only sends while the object is moving, which saves a lot of messages for things that sit still.")]
        public ViewSynchronization Synchronization = ViewSynchronization.UnreliableOnChange;
        [Tooltip("Takeover: anyone can take the object (best for things players pick up). Fixed: only the creator moves it.")]
        public OwnershipOption Ownership = OwnershipOption.Takeover;

        [Header("Ownership")]
        [Tooltip("Take ownership when your own hand touches it, so your movement is what gets synced.")]
        public bool TakeOwnershipOnTouch = false;

        /// <summary>True when this client controls (and sends) the object.</summary>
        public bool IsMine => photonView != null && photonView.IsMine;

        private void Reset()
        {
            if (GetComponent<Photon.VR.Interaction.PhotonVRGrabbable>() != null)
                PhotonVRLog.Warn("PhotonVRGrabbable already syncs this object; you don't need a Network Object on it too.", this);
            Apply();
        }

        private void OnValidate()
        {
            TeleportDistance = Mathf.Max(0f, TeleportDistance);
#if UNITY_EDITOR
            // Adding components isn't allowed inside OnValidate, so do it right after.
            PhotonVREditMode.Delay(() =>
            {
                if (this != null && !Application.isPlaying && !PhotonVREditMode.IsPersistent(this)) Apply();
            });
#endif
        }

        private void Awake() => Apply();

        /// <summary>Adds any missing Photon components and copies the settings onto them.</summary>
        public void Apply()
        {
            PhotonView view = GetComponent<PhotonView>();
            PhotonTransformView transformView = GetComponent<PhotonTransformView>();
            if (view == null || transformView == null) return;

            Rigidbody body = GetComponent<Rigidbody>();
            PhotonRigidbodyView bodyView = GetComponent<PhotonRigidbodyView>();
            bool useBody = SyncRigidbody && body != null;
            if (useBody && bodyView == null) bodyView = gameObject.AddComponent<PhotonRigidbodyView>();

            // The rigidbody view moves the object itself; two views writing the position would fight.
            transformView.m_SynchronizePosition = SyncPosition && !useBody;
            transformView.m_SynchronizeRotation = SyncRotation && !useBody;
            transformView.m_SynchronizeScale = SyncScale;
            transformView.m_UseLocal = UseLocal;

            if (bodyView != null)
            {
                bodyView.enabled = useBody;
                bodyView.m_SynchronizeVelocity = SyncVelocity;
                bodyView.m_SynchronizeAngularVelocity = SyncAngularVelocity;
                bodyView.m_TeleportEnabled = TeleportIfFar;
                bodyView.m_TeleportIfDistanceGreaterThan = TeleportDistance;
            }

            view.Synchronization = Synchronization;
            view.OwnershipTransfer = Ownership;
            view.observableSearch = PhotonView.ObservableSearch.Manual;
            if (view.ObservedComponents == null) view.ObservedComponents = new List<Component>();
            view.ObservedComponents.RemoveAll(c => c == null);
            Observe(view, transformView, true);
            Observe(view, bodyView, useBody);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                PhotonVREditMode.SetDirty(view);
                PhotonVREditMode.SetDirty(transformView);
                if (bodyView != null) PhotonVREditMode.SetDirty(bodyView);
            }
#endif
        }

        private static void Observe(PhotonView view, Component component, bool observe)
        {
            if (component == null) return;
            bool listed = view.ObservedComponents.Contains(component);
            if (observe && !listed) view.ObservedComponents.Add(component);
            else if (!observe && listed) view.ObservedComponents.Remove(component);
        }

        /// <summary>Makes this client the owner (needs Ownership set to Takeover or Request).</summary>
        public void TakeOwnership()
        {
            if (PhotonNetwork.InRoom && !IsMine) photonView.RequestOwnership();
        }

        /// <summary>Hands the object to the master client, e.g. before you leave so it keeps existing for everyone.</summary>
        public void GiveToMaster() => PhotonVROwnership.TransferToMaster(photonView);

        private void OnCollisionEnter(Collision collision) => Touched(collision.collider);
        private void OnTriggerEnter(Collider other) => Touched(other);

        private void Touched(Collider other)
        {
            if (!TakeOwnershipOnTouch || IsMine || other == null || PhotonVRManager.Manager == null) return;
            Transform t = other.transform;
            Transform left = PhotonVRManager.Manager.LeftHand, right = PhotonVRManager.Manager.RightHand;
            if ((left != null && t.IsChildOf(left)) || (right != null && t.IsChildOf(right))) TakeOwnership();
        }
    }
}
