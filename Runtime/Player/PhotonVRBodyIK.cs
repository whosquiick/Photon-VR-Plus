using System;
using UnityEngine;

namespace Photon.VR.Player
{
    /// <summary>
    /// Optional, cheap body IK for avatars with arms: two-bone arm solve towards the synced hands, plus a torso
    /// that follows the head's yaw. Runs locally on every client from the already-synced transforms, so it costs
    /// no bandwidth.
    /// </summary>
    public class PhotonVRBodyIK : MonoBehaviour
    {
        [Serializable]
        public class Arm
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Hand;
            [Tooltip("Where the hand should go, usually PhotonVRPlayer.LeftHand / RightHand.")]
            public Transform Target;
            [Tooltip("Elbow bends towards this. Optional; defaults to behind and below the shoulder.")]
            public Transform Hint;
            public bool MatchRotation = true;
        }

        public Transform Head;
        public Transform Torso;
        [Tooltip("Torso position relative to the head (in head-yaw space).")]
        public Vector3 TorsoOffset = new Vector3(0f, -0.35f, -0.05f);
        [Tooltip("Degrees the head can turn before the torso follows.")]
        public float TorsoYawDeadZone = 35f;
        public float TorsoTurnSpeed = 6f;
        public Arm LeftArm = new Arm();
        public Arm RightArm = new Arm();
        [Range(0f, 1f)] public float Weight = 1f;

        private float torsoYaw;

        private void Reset()
        {
            PhotonVRPlayer p = GetComponentInParent<PhotonVRPlayer>();
            if (p == null) return;
            Head = p.Head;
            Torso = p.Body;
            LeftArm.Target = p.LeftHand;
            RightArm.Target = p.RightHand;
        }

        private void LateUpdate()
        {
            if (Head != null && Torso != null)
            {
                float headYaw = Head.eulerAngles.y;
                float delta = Mathf.DeltaAngle(torsoYaw, headYaw);
                if (Mathf.Abs(delta) > TorsoYawDeadZone)
                    torsoYaw += (delta - Mathf.Sign(delta) * TorsoYawDeadZone);
                torsoYaw = Mathf.LerpAngle(torsoYaw, headYaw, 1f - Mathf.Exp(-TorsoTurnSpeed * Time.deltaTime * 0.25f));
                Quaternion yaw = Quaternion.Euler(0f, torsoYaw, 0f);
                Torso.SetPositionAndRotation(Head.position + yaw * TorsoOffset, yaw);
            }
            Solve(LeftArm, -1f);
            Solve(RightArm, 1f);
        }

        private void Solve(Arm arm, float side)
        {
            if (arm.Upper == null || arm.Lower == null || arm.Hand == null || arm.Target == null || Weight <= 0f) return;
            Vector3 hint = arm.Hint != null
                ? arm.Hint.position
                : arm.Upper.position + (Torso != null ? Torso.rotation : Quaternion.identity) * new Vector3(side * 0.3f, -0.5f, -0.3f);
            SolveTwoBone(arm.Upper, arm.Lower, arm.Hand, arm.Target.position, hint, Weight);
            if (arm.MatchRotation)
                arm.Hand.rotation = Quaternion.Slerp(arm.Hand.rotation, arm.Target.rotation, Weight);
        }

        /// <summary>Analytic two-bone IK (law of cosines). Bones keep their lengths.</summary>
        public static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight)
        {
            Vector3 a = upper.position;
            float upperLength = (lower.position - a).magnitude;
            float lowerLength = (end.position - lower.position).magnitude;
            if (upperLength < 1e-4f || lowerLength < 1e-4f) return;

            Vector3 toTarget = target - a;
            float distance = Mathf.Clamp(toTarget.magnitude, 1e-3f, upperLength + lowerLength - 1e-3f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;

            float cos = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * upperLength * distance), -1f, 1f);
            Vector3 normal = Vector3.Cross(dir, hint - a);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(dir, Vector3.up);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(dir, Vector3.forward);
            Vector3 bend = Vector3.Cross(normal, dir).normalized;
            Vector3 elbow = a + dir * (upperLength * cos) + bend * (upperLength * Mathf.Sqrt(1f - cos * cos));

            Quaternion upperTarget = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            upper.rotation = Quaternion.Slerp(upper.rotation, upperTarget, weight);
            Vector3 wrist = a + dir * distance;
            Quaternion lowerTarget = Quaternion.FromToRotation(end.position - lower.position, wrist - lower.position) * lower.rotation;
            lower.rotation = Quaternion.Slerp(lower.rotation, lowerTarget, weight);
        }
    }
}
