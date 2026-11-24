using System;
using UnityEngine;
using Photon.VR.Player;

namespace Photon.VR
{
    /// <summary>
    /// Finds the local XR rig (headset camera and controllers) by convention, for auto-assignment.
    /// Works with XR Origin ("Left Controller"), XRI starter assets ("LeftHand Controller"),
    /// OVRCameraRig ("LeftHandAnchor") and most custom rigs (names containing Left/Right + Hand/Controller).
    /// </summary>
    public static class PhotonVRRigFinder
    {
        /// <summary>The headset transform: <c>Camera.main</c>, or any camera named Camera/Head.</summary>
        public static Transform FindHead()
        {
            if (Camera.main != null)
                return Camera.main.transform;

            foreach (Camera cam in PhotonVRFind.All<Camera>())
                if (cam.name.Contains("Camera") || cam.name.Contains("Head"))
                    return cam.transform;
            return null;
        }

        /// <summary>The left controller / hand transform, or null.</summary>
        public static Transform FindLeftHand() => FindHand("Left");

        /// <summary>The right controller / hand transform, or null.</summary>
        public static Transform FindRightHand() => FindHand("Right");

        /// <summary>Finds a hand by side ("Left" / "Right"), preferring objects named "...Controller".</summary>
        public static Transform FindHand(string side)
        {
            Transform best = null;
            int bestScore = 0;
            foreach (Transform t in PhotonVRFind.All<Transform>())
            {
                string n = t.name;
                if (!Contains(n, side) || Contains(n, "Cosmetic"))
                    continue;
                if (t.GetComponentInParent<PhotonVRPlayer>() != null)
                    continue; // never pick a networked player model

                int score = Contains(n, "Controller") ? 3 : Contains(n, "Anchor") ? 2 : Contains(n, "Hand") ? 1 : 0;
                if (score > bestScore)
                {
                    best = t;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool Contains(string s, string value) => s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
