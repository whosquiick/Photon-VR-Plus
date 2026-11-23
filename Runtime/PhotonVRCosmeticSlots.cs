// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
namespace Photon.VR
{
    /// <summary>
    /// The slot names used by the default player prefab. These are the same strings the original
    /// PhotonVR used ("Head", "Face", "Body", "LeftHand", "RightHand"), so
    /// <c>PhotonVRManager.SetCosmetic(PhotonVRCosmeticSlots.Head, "VRTopHat")</c> is identical to
    /// <c>PhotonVRManager.SetCosmetic("Head", "VRTopHat")</c>.
    /// </summary>
    public static class PhotonVRCosmeticSlots
    {
        public const string Head = "Head";
        public const string Face = "Face";
        public const string Body = "Body";
        public const string LeftHand = "LeftHand";
        public const string RightHand = "RightHand";

        /// <summary>All default slots, in prefab order.</summary>
        public static readonly string[] All = { Head, Face, Body, LeftHand, RightHand };
    }

    /// <summary>Photon custom property / PlayerPrefs keys. Same values as the original PhotonVR.</summary>
    public static class PhotonVRKeys
    {
        // Player custom properties
        public const string Colour = "Colour";
        public const string Cosmetics = "Cosmetics";
        // Room custom properties
        public const string Queue = "queue";
        public const string Version = "version";
        // PlayerPrefs
        public const string PrefsUsername = "Username";
        public const string PrefsColour = "Colour";
        public const string PrefsCosmetics = "Cosmetics";
    }
}
