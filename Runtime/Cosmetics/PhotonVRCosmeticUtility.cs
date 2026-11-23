using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>Helpers shared by the cosmetic slots, the player and editor tools.</summary>
    public static class PhotonVRCosmeticUtility
    {
        /// <summary>
        /// The cosmetic ID of a model under a slot container: its <see cref="PhotonVRCosmetic.Id"/> when it has the
        /// component, otherwise its GameObject name (original PhotonVR behaviour).
        /// </summary>
        public static string GetId(Transform cosmetic)
        {
            if (cosmetic == null) return string.Empty;
            var marker = cosmetic.GetComponent<PhotonVRCosmetic>();
            return marker != null ? marker.Id : cosmetic.name;
        }

        /// <summary>True when the model under a container represents <paramref name="cosmeticId"/>.</summary>
        public static bool Matches(Transform cosmetic, string cosmeticId)
        {
            if (cosmetic == null || string.IsNullOrEmpty(cosmeticId)) return false;
            return cosmetic.name == cosmeticId || GetId(cosmetic) == cosmeticId;
        }

        /// <summary>
        /// Enables the child of <paramref name="container"/> matching <paramref name="cosmeticId"/> and disables all
        /// its siblings. An empty or unknown ID clears the slot. Returns true when something was equipped.
        /// </summary>
        public static bool Equip(Transform container, string cosmeticId)
        {
            if (container == null) return false;
            bool found = false;
            foreach (Transform cos in container)
            {
                bool on = !found && Matches(cos, cosmeticId);
                found |= on;
                if (cos.gameObject.activeSelf != on)
                    cos.gameObject.SetActive(on);
            }
            return found;
        }

        /// <summary>Tints every <see cref="PhotonVRCosmetic"/> under a container ("" or invalid = no tint).</summary>
        public static void ApplyTint(Transform container, string htmlColour)
        {
            if (container == null) return;
            Color? tint = null;
            if (!string.IsNullOrEmpty(htmlColour) && ColorUtility.TryParseHtmlString(htmlColour, out Color c)) tint = c;
            foreach (PhotonVRCosmetic cos in container.GetComponentsInChildren<PhotonVRCosmetic>(true))
                cos.ApplyTint(tint);
        }

        /// <summary>Lists the cosmetic IDs available under a container.</summary>
        public static List<string> GetAvailable(Transform container)
        {
            var list = new List<string>();
            if (container != null)
                foreach (Transform cos in container)
                    list.Add(GetId(cos));
            return list;
        }

        /// <summary>
        /// Converts the "Cosmetics" custom property to a string dictionary. Photon normally delivers a
        /// <c>Dictionary&lt;string,string&gt;</c>, but other clients may send a Hashtable / object dictionary.
        /// </summary>
        public static Dictionary<string, string> ToStringDictionary(object value)
        {
            if (value is Dictionary<string, string> typed)
                return typed;

            var result = new Dictionary<string, string>();
            if (value is IDictionary dict)
                foreach (DictionaryEntry e in dict)
                    if (e.Key != null)
                        result[e.Key.ToString()] = e.Value != null ? e.Value.ToString() : string.Empty;
            return result;
        }
    }
}
