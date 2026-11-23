using UnityEngine;
using UnityEngine.XR;

namespace Photon.VR.Interaction
{
    /// <summary>
    /// Put on a local hand (rig controller, not the networked avatar). Grabs the nearest PhotonVRGrabbable
    /// with the grip, or call Grab()/Release() from your own input.
    /// </summary>
    public class PhotonVRGrabber : MonoBehaviour
    {
        public XRNode Hand = XRNode.RightHand;
        [Tooltip("Read the grip from UnityEngine.XR. Turn off to drive Grab()/Release() yourself.")]
        public bool UseXRGrip = true;
        [Range(0.05f, 1f)] public float GripThreshold = 0.6f;
        public float GrabRadius = 0.08f;
        public LayerMask GrabLayers = ~0;
        [Tooltip("Optional point to grab from; defaults to this transform.")]
        public Transform GrabPoint;

        public PhotonVRGrabbable Held { get; private set; }

        private readonly Collider[] hits = new Collider[16];
        private bool gripDown;

        private void Update()
        {
            if (!UseXRGrip) return;
            bool down = PhotonVRXRInput.GetAxis(Hand, true) >= GripThreshold;
            if (down == gripDown) return;
            gripDown = down;
            if (down) Grab();
            else Release();
        }

        public PhotonVRGrabbable Grab()
        {
            if (Held != null) return Held;
            Transform point = GrabPoint != null ? GrabPoint : transform;
            int count = Physics.OverlapSphereNonAlloc(point.position, GrabRadius, hits, GrabLayers, QueryTriggerInteraction.Collide);
            PhotonVRGrabbable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                PhotonVRGrabbable g = hits[i].GetComponentInParent<PhotonVRGrabbable>();
                if (g == null || (g.IsHeld && !g.AllowSteal && !g.photonView.IsMine)) continue;
                float d = (hits[i].ClosestPoint(point.position) - point.position).sqrMagnitude;
                if (d < bestDistance) { best = g; bestDistance = d; }
            }
            if (best != null && best.Grab(point))
            {
                Held = best;
                Held.Released += OnHeldReleased;
            }
            return Held;
        }

        public void Release()
        {
            if (Held == null) return;
            PhotonVRGrabbable g = Held;
            g.Released -= OnHeldReleased;
            Held = null;
            g.Release();
        }

        private void OnHeldReleased(PhotonVRGrabbable g)
        {
            g.Released -= OnHeldReleased;
            if (Held == g) Held = null;
        }

        private void OnDisable() => Release();

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere((GrabPoint != null ? GrabPoint : transform).position, GrabRadius);
        }
    }
}
