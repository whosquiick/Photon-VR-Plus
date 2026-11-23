// Photon VR Plus - a revamp of PhotonVR by fchb1239 (MIT). See LICENSE.md.
// NOTE: intentionally in the global namespace, exactly like the original, so existing references keep compiling.
using UnityEngine;

/// <summary>
/// Keeps the body under the head: same X/Z, head Y + Offset, and yaw-only rotation (no tilting with the head).
/// </summary>
[DefaultExecutionOrder(100)] // after PhotonVRPlayer moved the head
public class PhotonVRPlayerBody : MonoBehaviour
{
    public Transform Head;
    public float Offset;

    [Tooltip("0 = snap to the head's yaw every frame (original behaviour). Higher values make the body turn smoothly.")]
    public float RotationSmoothing = 0f;

    private void LateUpdate()
    {
        if (Head == null)
            return;

        // Yaw only. Using the forward vector avoids the original's quaternion component trick,
        // which skewed the rotation when looking up or down.
        Vector3 forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Head.up, Vector3.up) * (Head.forward.y > 0 ? -1f : 1f);
        Quaternion target = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : transform.rotation;

        transform.rotation = RotationSmoothing > 0f
            ? Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime))
            : target;
        transform.position = new Vector3(Head.position.x, Head.position.y + Offset, Head.position.z);
    }
}
