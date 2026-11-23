namespace Photon.VR
{
    /// <summary>
    /// Slot names used by the default player prefab. Identical to the strings used by the original
    /// PhotonVR, so <c>SetCosmetic(PhotonVRCosmeticSlots.Head, "VRTopHat")</c> equals <c>SetCosmetic("Head", "VRTopHat")</c>.
    /// </summary>
    public static class PhotonVRCosmeticSlots
    {
        /// <summary>Hats etc. Container: Head/HeadCosmetics.</summary>
        public const string Head = "Head";
        /// <summary>Glasses, masks, eyes. Container: Head/FaceCosmetics.</summary>
        public const string Face = "Face";
        /// <summary>Shirts, badges. Container: Body/BodyCosmetics.</summary>
        public const string Body = "Body";
        /// <summary>Left hand items. Container: LeftHand/LeftHandCosmetics.</summary>
        public const string LeftHand = "LeftHand";
        /// <summary>Right hand items. Container: RightHand/RightHandCosmetics.</summary>
        public const string RightHand = "RightHand";

        /// <summary>All default slots, in prefab order.</summary>
        public static readonly string[] All = { Head, Face, Body, LeftHand, RightHand };

        /// <summary>Name of the container object for a slot (e.g. "Head" to "HeadCosmetics").</summary>
        public static string ContainerName(string slot) => slot + "Cosmetics";
    }
}
