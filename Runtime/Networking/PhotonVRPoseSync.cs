using System;
using System.Collections.Generic;
using UnityEngine;

using Photon.Pun;

namespace Photon.VR
{
    /// <summary>
    /// Network transform sync for a VR avatar's head and both hands.
    /// <para>Owner: copies the local rig (PhotonVRManager.Head / LeftHand / RightHand, falling back to Camera.main)
    /// onto <see cref="Head"/>, <see cref="LeftHand"/> and <see cref="RightHand"/> every frame and sends them.</para>
    /// <para>Everyone else: buffers the received poses and renders them slightly in the past with snapshot
    /// interpolation, short extrapolation when packets are late, and snapping on teleports.</para>
    /// Must be in the PhotonView's Observed Components (PhotonVRPlayer arranges that automatically).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PhotonVR/PhotonVR Pose Sync")]
    public class PhotonVRPoseSync : MonoBehaviourPun, IPunObservable
    {
        [Header("Synced parts (of the player model)")]
        public Transform Head;
        public Transform LeftHand;
        public Transform RightHand;

        [Header("Owner")]
        [Tooltip("On the owner, copy the local rig (PhotonVRManager.Head / LeftHand / RightHand) onto the parts every frame.")]
        public bool FollowLocalRig = true;

        [Header("Remote smoothing")]
        [Tooltip("How far in the past remote players are rendered, in seconds. 0 = automatic (2 serialization intervals).")]
        public float InterpolationBackTime = 0f;
        [Tooltip("How long, in seconds, positions are extrapolated when packets are late.")]
        public float MaxExtrapolation = 0.2f;
        [Tooltip("If the head moves farther than this between two updates, snap instead of sliding (respawns / teleports).")]
        public float TeleportDistance = 4f;
        [Tooltip("With automatic back time, add this many standard deviations of measured packet jitter (lag compensation).")]
        [Range(0f, 4f)] public float JitterMargin = 2f;
        [Tooltip("Extra smoothing on remote hands (0 = none). Hides small network jitter on fast hand movement.")]
        [Range(0f, 0.9f)] public float HandSmoothing = 0.3f;

        [Header("Validation")]
        [Tooltip("Remote hands farther than this from the head are pulled back (blocks 'long arm' cheats and bad packets).")]
        public float MaxHandReach = 2.5f;
        [Tooltip("Ignore pose packets beyond this many per second from one sender.")]
        public int MaxPacketsPerSecond = 90;

        /// <summary>Raised on the owner after the rig pose was copied this frame (before it is sent).</summary>
        public event Action<PhotonVRPoseSync> LocalPoseUpdated;

        /// <summary>Server time of the newest received pose (remote only).</summary>
        public double LastReceivedTime => count > 0 ? buffer[0].Time : 0;

        private struct Snapshot
        {
            public double Time;
            public Vector3 HeadPos, LeftPos, RightPos;
            public Quaternion HeadRot, LeftRot, RightRot;
        }

        private const int BufferSize = 16;
        private double lastArrival;
        private float avgInterval, jitter;
        private readonly PhotonVRRateLimiter packetLimit = new PhotonVRRateLimiter(90, 90);
        private readonly Snapshot[] buffer = new Snapshot[BufferSize];
        private int count;

        /// <summary>
        /// Registers this component as the PhotonView's pose source and removes components that would fight it
        /// (legacy PhotonTransformViews on the parts, observed components that are not IPunObservable).
        /// Call before the first serialization (PhotonVRPlayer does this in Awake).
        /// </summary>
        public void ConfigureObservation()
        {
            PhotonView view = photonView;
            if (view == null) return;
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

            view.ObservedComponents.RemoveAll(c => c == null || !(c is IPunObservable));
            if (!view.ObservedComponents.Contains(this))
                view.ObservedComponents.Insert(0, this);

            view.observableSearch = PhotonView.ObservableSearch.Manual;
            if (view.Synchronization == ViewSynchronization.Off)
                view.Synchronization = ViewSynchronization.UnreliableOnChange;
        }

        private void Update()
        {
            if (!photonView.IsMine)
                Interpolate();
        }

        private void LateUpdate()
        {
            // Late so tracking has already moved the camera and controllers this frame.
            if (photonView.IsMine)
                FollowRig();
        }

        /// <summary>Copies the local rig onto the parts now (owner only).</summary>
        public void FollowRig()
        {
            if (!FollowLocalRig) return;

            PhotonVRManager m = PhotonVRManager.Manager;
            Transform headSource = m != null && m.Head != null ? m.Head : FallbackHead();
            CopyPose(headSource, Head);
            CopyPose(m != null ? m.LeftHand : null, LeftHand);
            CopyPose(m != null ? m.RightHand : null, RightHand);
            LocalPoseUpdated?.Invoke(this);
        }

        private Transform fallbackHead;

        private Transform FallbackHead()
        {
            if (fallbackHead == null && Camera.main != null) fallbackHead = Camera.main.transform;
            return fallbackHead;
        }

        private static void CopyPose(Transform from, Transform to)
        {
            if (from != null && to != null)
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
                // PUN does not check who sent a serialization update, so we do.
                if (info.Sender != null && photonView.Owner != null && info.Sender.ActorNumber != photonView.Owner.ActorNumber)
                {
                    PhotonVRSecurity.Report(info.Sender, PhotonVRViolationType.NotOwner, "pose update for a view it does not own");
                    return;
                }
                packetLimit.Configure(MaxPacketsPerSecond, MaxPacketsPerSecond);
                if (!packetLimit.TryConsume())
                    return;

                Snapshot s = new Snapshot { Time = info.SentServerTime };
                ReadPose(stream, out s.HeadPos, out s.HeadRot);
                ReadPose(stream, out s.LeftPos, out s.LeftRot);
                ReadPose(stream, out s.RightPos, out s.RightRot);
                if (!Sanitize(ref s))
                    return;
                TrackJitter();
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

        private bool Sanitize(ref Snapshot s)
        {
            if (!Valid(s.HeadPos) || !Valid(s.LeftPos) || !Valid(s.RightPos))
                return false;
            s.LeftPos = ClampReach(s.HeadPos, s.LeftPos);
            s.RightPos = ClampReach(s.HeadPos, s.RightPos);
            return true;
        }

        private static bool Valid(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && v.sqrMagnitude < 1e10f;

        private Vector3 ClampReach(Vector3 head, Vector3 hand)
        {
            if (MaxHandReach <= 0f) return hand;
            Vector3 d = hand - head;
            return d.sqrMagnitude > MaxHandReach * MaxHandReach ? head + d.normalized * MaxHandReach : hand;
        }

        // Running average of the packet interval and its deviation, used for the automatic back time.
        private void TrackJitter()
        {
            double now = Time.realtimeSinceStartup;
            if (lastArrival > 0)
            {
                float interval = (float)(now - lastArrival);
                if (interval < 1f)
                {
                    avgInterval = avgInterval <= 0f ? interval : Mathf.Lerp(avgInterval, interval, 0.1f);
                    jitter = Mathf.Lerp(jitter, Mathf.Abs(interval - avgInterval), 0.1f);
                }
            }
            lastArrival = now;
        }

        /// <summary>The back time currently used for remote rendering, in seconds.</summary>
        public float CurrentBackTime
        {
            get
            {
                if (InterpolationBackTime > 0f) return InterpolationBackTime;
                float nominal = 2f / Mathf.Max(1, PhotonNetwork.SerializationRate);
                float measured = avgInterval + jitter * JitterMargin * 1.25f;
                return Mathf.Clamp(Mathf.Max(nominal, measured), 0.03f, 0.5f);
            }
        }

        /// <summary>Drops buffered poses so the next received pose snaps (use after teleporting a remote avatar).</summary>
        public void ResetInterpolation() => count = 0;

        private void AddSnapshot(Snapshot s)
        {
            if (count > 0)
            {
                Snapshot newest = buffer[0];
                if (s.Time <= newest.Time)
                {
                    // Out-of-order packet: ignore. Server clock wrap / big jump back: reset.
                    if (newest.Time - s.Time < 1000.0) return;
                    count = 0;
                }
                else if ((s.HeadPos - newest.HeadPos).sqrMagnitude > TeleportDistance * TeleportDistance)
                {
                    count = 0; // teleport: drop history so we snap
                }
            }

            for (int i = Mathf.Min(count, BufferSize - 1); i > 0; i--)
                buffer[i] = buffer[i - 1];
            buffer[0] = s;
            count = Mathf.Min(count + 1, BufferSize);

            if (count == 1)
                Apply(s); // first packet: snap
        }

        private void Interpolate()
        {
            if (count == 0) return;

            float back = CurrentBackTime;
            double renderTime = PhotonNetwork.Time - back;

            Snapshot newest = buffer[0];
            if (renderTime >= newest.Time || count == 1)
            {
                Extrapolate(newest, renderTime);
                return;
            }

            // buffer[0] is newest; find the pair around renderTime.
            for (int i = 0; i < count - 1; i++)
            {
                Snapshot newer = buffer[i];
                Snapshot older = buffer[i + 1];
                if (older.Time <= renderTime)
                {
                    double span = newer.Time - older.Time;
                    float t = span > 0.0001 ? Mathf.Clamp01((float)((renderTime - older.Time) / span)) : 1f;
                    Set(Head, Vector3.Lerp(older.HeadPos, newer.HeadPos, t), Quaternion.Slerp(older.HeadRot, newer.HeadRot, t));
                    SetHand(LeftHand, Vector3.Lerp(older.LeftPos, newer.LeftPos, t), Quaternion.Slerp(older.LeftRot, newer.LeftRot, t));
                    SetHand(RightHand, Vector3.Lerp(older.RightPos, newer.RightPos, t), Quaternion.Slerp(older.RightRot, newer.RightRot, t));
                    return;
                }
            }

            Apply(buffer[count - 1]); // renderTime older than everything buffered
        }

        private void Extrapolate(Snapshot newest, double renderTime)
        {
            if (count > 1 && MaxExtrapolation > 0f)
            {
                Snapshot prev = buffer[1];
                double span = newest.Time - prev.Time;
                float extra = (float)Math.Min(renderTime - newest.Time, MaxExtrapolation);
                if (span > 0.0001 && extra > 0f)
                {
                    float k = (float)(extra / span);
                    Set(Head, newest.HeadPos + (newest.HeadPos - prev.HeadPos) * k, newest.HeadRot);
                    SetHand(LeftHand, newest.LeftPos + (newest.LeftPos - prev.LeftPos) * k, newest.LeftRot);
                    SetHand(RightHand, newest.RightPos + (newest.RightPos - prev.RightPos) * k, newest.RightRot);
                    return;
                }
            }
            Apply(newest);
        }

        private void SetHand(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t == null) return;
            if (HandSmoothing > 0f && (t.position - pos).sqrMagnitude < 0.25f)
            {
                float k = 1f - HandSmoothing;
                pos = Vector3.Lerp(t.position, pos, k);
                rot = Quaternion.Slerp(t.rotation, rot, k);
            }
            t.SetPositionAndRotation(pos, rot);
        }

        private void Apply(Snapshot s)
        {
            Set(Head, s.HeadPos, s.HeadRot);
            Set(LeftHand, s.LeftPos, s.LeftRot);
            Set(RightHand, s.RightPos, s.RightRot);
        }

        private static void Set(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t != null) t.SetPositionAndRotation(pos, rot);
        }
    }
}
