using System;
using System.Collections.Generic;

using UnityEngine;

using Photon.Pun;
using Photon.VR.Saving;

using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Photon.VR
{
    public partial class PhotonVRManager
    {
        #region Public API

        /// <summary>Sets the Photon nickname to something (refused when the word filter blocks it)</summary>
        public static void SetUsername(string Name) => TrySetUsername(Name);

        /// <summary>Sets the nickname. Returns false (and raises <see cref="UsernameRejected"/>) when it is blocked or too long.</summary>
        public static bool TrySetUsername(string Name)
        {
            Name = PhotonVRWordFilter.StripMarkup(Name ?? string.Empty).Trim();
            if (Name.Length > PhotonVRProfiles.MaxNameLength || PhotonVRWordFilter.Default.IsBlocked(Name))
            {
                PhotonVRLog.Warn("That name is not allowed.");
                UsernameRejected?.Invoke(Name);
                return false;
            }

            PhotonNetwork.LocalPlayer.NickName = Name;
            PlayerPrefs.SetString(PhotonVRKeys.PrefsUsername, Name);
            PlayerPrefs.Save();
            PhotonVRProfiles.PublishLocal();
            PhotonVRSaveSystem.SaveLocalPlayer();

            RefreshLocal();
            return true;
        }

        /// <summary>Raised when a name was refused by the word filter.</summary>
        public static event Action<string> UsernameRejected;

        /// <summary>Tints a cosmetic slot (applied to <see cref="PhotonVRCosmetic"/> renderers marked as tintable).</summary>
        public static void SetCosmeticTint(string Type, Color tint)
        {
            if (string.IsNullOrEmpty(Type) || !EnsureManager("SetCosmeticTint")) return;
            Manager.CosmeticTints[Type] = "#" + ColorUtility.ToHtmlStringRGBA(tint);
            Manager.CommitCosmetics();
        }

        /// <summary>Removes the tint from a slot.</summary>
        public static void ClearCosmeticTint(string Type)
        {
            if (string.IsNullOrEmpty(Type) || !EnsureManager("ClearCosmeticTint")) return;
            if (Manager.CosmeticTints.Remove(Type))
                Manager.CommitCosmetics();
        }

        /// <summary>Sets the colour</summary>
        public static void SetColour(Color PlayerColour)
        {
            if (!EnsureManager("SetColour")) return;
            Manager.Colour = PlayerColour;
            string json = JsonUtility.ToJson(PlayerColour);
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PhotonVRKeys.Colour, json } });
            PlayerPrefs.SetString(PhotonVRKeys.PrefsColour, json);
            PlayerPrefs.Save();
            PhotonVRProfiles.PublishLocal();
            PhotonVRSaveSystem.SaveLocalPlayer();

            ColourChanged?.Invoke(PlayerColour);
            RefreshLocal();
        }

        /// <summary>Sets the cosmetics</summary>
        public static void SetCosmetics(Dictionary<string, string> PlayerCosmetics)
        {
            if (!EnsureManager("SetCosmetics")) return;
            var copy = new Dictionary<string, string>();
            if (PlayerCosmetics != null)
                foreach (KeyValuePair<string, string> kv in PlayerCosmetics)
                    if (!string.IsNullOrEmpty(kv.Key))
                        copy[kv.Key] = kv.Value ?? string.Empty;
            Manager.Cosmetics = copy;
            Manager.CommitCosmetics();
        }

        /// <summary>Sets a specific cosmetic</summary>
        public static void SetCosmetic(string Type, string CosmeticId)
        {
            if (string.IsNullOrEmpty(Type))
            {
                PhotonVRLog.Warn("SetCosmetic called with an empty slot name");
                return;
            }
            if (!EnsureManager("SetCosmetic")) return;
            Manager.Cosmetics[Type] = CosmeticId ?? string.Empty;
            Manager.CommitCosmetics();
        }

        /// <summary>Clears one cosmetic slot. Same as <c>SetCosmetic(slot, "")</c>.</summary>
        public static void ClearCosmetic(string Type) => SetCosmetic(Type, string.Empty);

        /// <summary>Removes every equipped cosmetic.</summary>
        public static void ClearAllCosmetics()
        {
            if (!EnsureManager("ClearAllCosmetics")) return;
            var cleared = new Dictionary<string, string>();
            foreach (string key in Manager.Cosmetics.Keys)
                cleared[key] = string.Empty;
            SetCosmetics(cleared);
        }

        /// <summary>Returns the cosmetic equipped in a slot, or "" if none.</summary>
        public static string GetCosmetic(string Type)
        {
            if (Manager == null || string.IsNullOrEmpty(Type)) return string.Empty;
            return Manager.Cosmetics.TryGetValue(Type, out string id) ? id ?? string.Empty : string.Empty;
        }

        /// <summary>True when <paramref name="CosmeticId"/> is equipped in <paramref name="Type"/>.</summary>
        public static bool IsCosmeticEquipped(string Type, string CosmeticId) =>
            !string.IsNullOrEmpty(CosmeticId) && GetCosmetic(Type) == CosmeticId;

        /// <summary>Equips <paramref name="CosmeticId"/> in <paramref name="Type"/>, or clears the slot if it is already equipped.</summary>
        public static void ToggleCosmetic(string Type, string CosmeticId) =>
            SetCosmetic(Type, IsCosmeticEquipped(Type, CosmeticId) ? string.Empty : CosmeticId);

        #endregion

        #region Internals

        private void LoadSavedValues()
        {
            string colour = PlayerPrefs.GetString(PhotonVRKeys.PrefsColour);
            if (!string.IsNullOrEmpty(colour))
            {
                try { Colour = JsonUtility.FromJson<Color>(colour); }
                catch (Exception e) { PhotonVRLog.Warn("Could not read saved colour: " + e.Message); }
            }

            if (PhotonVRValueSaver.HasDictionary(PhotonVRKeys.PrefsCosmetics))
                Cosmetics = PhotonVRValueSaver.GetDictionary(PhotonVRKeys.PrefsCosmetics);
            if (PhotonVRValueSaver.HasDictionary(PhotonVRKeys.PrefsCosmeticTints))
                CosmeticTints = PhotonVRValueSaver.GetDictionary(PhotonVRKeys.PrefsCosmeticTints);

            // Remote save providers (PlayFab, your backend) answer later and overwrite these values.
            PhotonVRSaveSystem.LoadLocalPlayer();
        }

        // Applies data from a save provider (see PhotonVRSaveSystem).
        internal void ApplySaveData(PhotonVRSaveData data)
        {
            if (data == null) return;
            if (!string.IsNullOrEmpty(data.Username) && !PhotonVRWordFilter.Default.IsBlocked(data.Username))
            {
                PhotonNetwork.LocalPlayer.NickName = data.Username;
                PlayerPrefs.SetString(PhotonVRKeys.PrefsUsername, data.Username);
            }
            if (data.HasColour) Colour = data.Colour;
            if (data.Cosmetics != null) Cosmetics = new Dictionary<string, string>(data.Cosmetics);
            if (data.CosmeticTints != null) CosmeticTints = new Dictionary<string, string>(data.CosmeticTints);

            PlayerPrefs.SetString(PhotonVRKeys.PrefsColour, JsonUtility.ToJson(Colour));
            PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmetics, Cosmetics);
            PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmeticTints, CosmeticTints);
            PlayerPrefs.Save();
            RepublishLocalProperties();
            CosmeticsChanged?.Invoke(new Dictionary<string, string>(Cosmetics));
        }

        internal PhotonVRSaveData CaptureSaveData() => new PhotonVRSaveData
        {
            Username = PhotonNetwork.LocalPlayer.NickName,
            Colour = Colour,
            HasColour = true,
            Cosmetics = new Dictionary<string, string>(Cosmetics),
            CosmeticTints = new Dictionary<string, string>(CosmeticTints)
        };

        internal void RepublishLocalProperties()
        {
            PushLocalProperties();
            PhotonVRProfiles.PublishLocal();
            RefreshLocal();
        }

        private void CommitCosmetics()
        {
            // Always send a fresh copy so Photon never holds a reference we mutate later.
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { PhotonVRKeys.Cosmetics, new Dictionary<string, string>(Cosmetics) },
                { PhotonVRKeys.CosmeticTints, new Dictionary<string, string>(CosmeticTints) }
            });
            PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmetics, Cosmetics);
            PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmeticTints, CosmeticTints);
            PhotonVRProfiles.PublishLocal();
            PhotonVRSaveSystem.SaveLocalPlayer();

            CosmeticsChanged?.Invoke(new Dictionary<string, string>(Cosmetics));
            RefreshLocal();
        }

        // Pushes Colour + Cosmetics into the local player's custom properties.
        private void PushLocalProperties()
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { PhotonVRKeys.Colour, JsonUtility.ToJson(Colour) },
                { PhotonVRKeys.Cosmetics, new Dictionary<string, string>(Cosmetics) },
                { PhotonVRKeys.CosmeticTints, new Dictionary<string, string>(CosmeticTints) }
            });
        }

        // Applies the change on our own player right away. Remote clients update through OnPlayerPropertiesUpdate;
        // the RPC sent by RefreshPlayerValues is kept for clients still running the original PhotonVR.
        private static void RefreshLocal()
        {
            if (PhotonNetwork.InRoom && Manager != null && Manager.LocalPlayer != null)
                Manager.LocalPlayer.RefreshPlayerValues();
        }

        #endregion
    }
}
