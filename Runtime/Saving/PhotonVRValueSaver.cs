// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR.Saving
{
    /// <summary>
    /// Saves string dictionaries to PlayerPrefs. Same storage format as the original PhotonVR:
    /// <c>PlayerPrefs[location]</c> = comma-joined keys, <c>PlayerPrefs[location + key]</c> = value.
    /// (Still a MonoBehaviour so existing references to the component keep working.)
    /// </summary>
    public class PhotonVRValueSaver : MonoBehaviour
    {
        public static void SaveDictionary(string location, Dictionary<string, string> value)
        {
            if (string.IsNullOrEmpty(location))
                return;

            if (value == null)
                value = new Dictionary<string, string>();

            // Keys containing ',' would break the format; skip them with a warning.
            var keys = new List<string>(value.Count);
            foreach (KeyValuePair<string, string> kv in value)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Key.Contains(","))
                {
                    Debug.LogWarning($"[PhotonVR] Skipping invalid dictionary key '{kv.Key}' when saving '{location}'");
                    continue;
                }
                keys.Add(kv.Key);
                PlayerPrefs.SetString(location + kv.Key, kv.Value ?? string.Empty);
            }

            PlayerPrefs.SetString(location, string.Join(",", keys));
            PlayerPrefs.Save();
        }

        public static Dictionary<string, string> GetDictionary(string location)
        {
            var value = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(location))
                return value;

            string stored = PlayerPrefs.GetString(location);
            if (string.IsNullOrEmpty(stored))
                return value;

            foreach (string key in stored.Split(','))
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                value[key] = PlayerPrefs.GetString(location + key);
            }
            return value;
        }

        /// <summary>True if a dictionary was saved at <paramref name="location"/>.</summary>
        public static bool HasDictionary(string location) => !string.IsNullOrEmpty(PlayerPrefs.GetString(location));
    }
}
