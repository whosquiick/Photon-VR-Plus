using UnityEngine;

namespace Photon.VR.Player
{
    /// <summary>Floats the name tag above the head and turns it (yaw only) to face the local camera.</summary>
    [DefaultExecutionOrder(100)] // after PhotonVRPoseSync moved the head
    [AddComponentMenu("PhotonVR/PhotonVR Player Name")]
    public class PhotonVRPlayerName : MonoBehaviour
    {
        [Tooltip("How high the text should be above the players head")]
        public float Offset = 0.17f;
        public Transform Head;
        [Tooltip("How fast the tag turns to face the viewer.")]
        public float TurnSpeed = 10f;

        private void LateUpdate()
        {
            if (Head == null)
                return;

            transform.position = Head.position + new Vector3(0, Offset, 0);

            PhotonVRManager m = PhotonVRManager.Manager;
            Transform viewer = m != null && m.Head != null ? m.Head : FallbackViewer();
            if (viewer == null)
                return;

            Vector3 direction = viewer.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            // Same facing as the original PhotonVR (the TMP child in the prefab is set up for this).
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, TurnSpeed * Time.deltaTime);
        }

        private Transform fallbackViewer;

        private Transform FallbackViewer()
        {
            if (fallbackViewer == null && Camera.main != null) fallbackViewer = Camera.main.transform;
            return fallbackViewer;
        }
    }
}
