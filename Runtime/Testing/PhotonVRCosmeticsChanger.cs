// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR.Testing
{
    /// <summary>Test helper: press "Change" in the inspector (in play mode, in a room) to equip the listed cosmetics.</summary>
    public class PhotonVRCosmeticsChanger : MonoBehaviour
    {
        public List<PhotonVRCosmeticTest> Cosmetics = new List<PhotonVRCosmeticTest>();

        public void ChangeCosmetics(Dictionary<string, string> Cosmetics)
        {
            PhotonVRManager.SetCosmetics(Cosmetics);
        }

        /// <summary>Applies the list from the inspector (handy for UnityEvents / buttons).</summary>
        public void ChangeCosmetics()
        {
            var dictionary = new Dictionary<string, string>();
            foreach (PhotonVRCosmeticTest cosmetic in Cosmetics)
                if (cosmetic != null && !string.IsNullOrEmpty(cosmetic.SlotName))
                    dictionary[cosmetic.SlotName] = cosmetic.Cosmetic ?? string.Empty;
            ChangeCosmetics(dictionary);
        }

        [Serializable]
        public class PhotonVRCosmeticTest
        {
            public string SlotName;
            public string Cosmetic;
        }
    }
}
