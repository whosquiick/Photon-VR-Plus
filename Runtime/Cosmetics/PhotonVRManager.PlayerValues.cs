using System;
using System.Collections.Generic;

using UnityEngine;

using Photon.Pun;
using Photon.Realtime;
using Photon.VR.Saving;

using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Photon.VR
{
    public partial class PhotonVRManager
    {
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

            // Same name again (e.g. a script calling this every frame): nothing to send.
            if (Name == acceptedName && PhotonNetwork.LocalPlayer.NickName == Name)
            {
                pendingName = null;
                return true;
            }

            // Every change is a network message, a PlayerPrefs write and maybe a PlayFab call, so quick bursts
            // (typing on a name keyboard) are merged: the latest name goes out as soon as the limit allows.
            if (Manager != null && !NameLimit.TryConsume())
            {
                pendingName = Name;
                return true;
            }

            pendingName = null;
            // Our own actor property: no master client involved, so it works whoever is (or isn't) master.
            SetLocalNickName(Name);
            AcceptUsername(Name);
            return true;
        }

        private static PhotonVRRateLimiter nameLimit;
        private static string pendingName;
        private static PhotonVRRateLimiter NameLimit => nameLimit ?? (nameLimit = new PhotonVRRateLimiter(1f, 4f));

        // Called from Update: sends a name that was held back by the rate limit.
        private static void FlushPendingName()
        {
            if (pendingName == null || !NameLimit.TryConsume()) return;
            string name = pendingName;
            pendingName = null;
            SetLocalNickName(name);
            AcceptUsername(name);
        }

        // Names saved by older versions (or edited in PlayerPrefs) go through the same checks as new ones.
        private static string SanitizeSavedName(string saved)
        {
            string name = PhotonVRWordFilter.StripMarkup(saved ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > PhotonVRProfiles.MaxNameLength || PhotonVRWordFilter.Default.IsBlocked(name))
                return null;
            return name;
        }

        /// <summary>Raised when a name was refused by the word filter.</summary>
        public static event Action<string> UsernameRejected;

        /// <summary>Raised after the local name changed, through PhotonVR or by your own code setting PhotonNetwork.NickName.</summary>
        public static event Action<string> UsernameChanged;

        /// <summary>Raised when an equip was refused because the cosmetic isn't owned (slot, cosmetic ID).</summary>
        public static event Action<string, string> CosmeticRejected;

        // Owned at runtime (PlayFab inventory, purchases, your backend), on top of the inspector's OwnedCosmetics.
        private static readonly HashSet<string> grantedCosmetics = new HashSet<string>();

        /// <summary>True when the local player may wear <paramref name="CosmeticId"/>. Always true with RequireOwnedCosmetics off.</summary>
        public static bool OwnsCosmetic(string CosmeticId)
        {
            if (string.IsNullOrEmpty(CosmeticId) || Manager == null || !Manager.RequireOwnedCosmetics) return true;
            return grantedCosmetics.Contains(CosmeticId) || (Manager.OwnedCosmetics != null && Manager.OwnedCosmetics.Contains(CosmeticId));
        }

        /// <summary>Replaces the runtime owned list (e.g. with the player's PlayFab inventory) and re-sends what may now be worn.</summary>
        public static void SetOwnedCosmetics(IEnumerable<string> CosmeticIds)
        {
            grantedCosmetics.Clear();
            if (CosmeticIds != null)
                foreach (string id in CosmeticIds)
                    if (!string.IsNullOrEmpty(id)) grantedCosmetics.Add(id);
            if (Manager != null && Manager.RequireOwnedCosmetics) Manager.RepublishLocalProperties();
        }

        /// <summary>Marks one cosmetic as owned (after a purchase).</summary>
        public static void GrantCosmetic(string CosmeticId)
        {
            if (string.IsNullOrEmpty(CosmeticId) || !grantedCosmetics.Add(CosmeticId)) return;
            if (Manager != null && Manager.RequireOwnedCosmetics) Manager.RepublishLocalProperties();
        }

        private static void RejectCosmetic(string slot, string id)
        {
            PhotonVRLog.Warn($"Cosmetic '{id}' is not owned; not equipping it.");
            CosmeticRejected?.Invoke(slot, id);
        }

        // What gets sent to other players: equipped cosmetics with anything not owned cleared.
        internal Dictionary<string, string> OwnedOnly(Dictionary<string, string> equipped)
        {
            var result = new Dictionary<string, string>(equipped);
            if (!RequireOwnedCosmetics) return result;
            foreach (var kv in equipped)
                if (!OwnsCosmetic(kv.Value)) result[kv.Key] = string.Empty;
            return result;
        }

        // Last name that passed the filter; what we restore when a blocked or foreign name shows up.
        private static string acceptedName;

        private static void AcceptUsername(string name)
        {
            bool changed = acceptedName != name;
            acceptedName = name;
            PlayerPrefs.SetString(PhotonVRKeys.PrefsUsername, name);
            PlayerPrefs.Save();
            PhotonVRProfiles.PublishLocal();
            PhotonVRSaveSystem.SaveLocalPlayer();
            RefreshLocal();
            if (changed) UsernameChanged?.Invoke(name);
        }

        internal static string AcceptedName
        {
            get
            {
                if (!string.IsNullOrEmpty(acceptedName)) return acceptedName;
                string saved = SanitizeSavedName(PlayerPrefs.GetString(PhotonVRKeys.PrefsUsername));
                return !string.IsNullOrEmpty(saved) ? saved : "Player" + (PhotonNetwork.LocalPlayer?.ActorNumber ?? 0);
            }
        }

        private static void SetLocalNickName(string name)
        {
            PhotonVRProfiles.NoteOwnWrite(ActorProperties.PlayerName);
            PhotonNetwork.LocalPlayer.NickName = name;
        }

        private static void SetLocalProperties(Hashtable properties)
        {
            foreach (object key in properties.Keys)
                PhotonVRProfiles.NoteOwnWrite(key);
            PhotonNetwork.LocalPlayer.SetCustomProperties(properties);
        }

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
            SetLocalProperties(new Hashtable { { PhotonVRKeys.Colour, json } });
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
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    string id = kv.Value ?? string.Empty;
                    if (!OwnsCosmetic(id))
                    {
                        RejectCosmetic(kv.Key, id);
                        // Keep whatever was in that slot before.
                        if (Manager.Cosmetics.TryGetValue(kv.Key, out string previous)) copy[kv.Key] = previous;
                        continue;
                    }
                    copy[kv.Key] = id;
                }
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
            if (!OwnsCosmetic(CosmeticId))
            {
                RejectCosmetic(Type, CosmeticId);
                return;
            }
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
                SetLocalNickName(data.Username);
                acceptedName = data.Username;
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
            SetLocalProperties(new Hashtable
            {
                { PhotonVRKeys.Cosmetics, OwnedOnly(Cosmetics) },
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
            SetLocalProperties(new Hashtable
            {
                { PhotonVRKeys.Colour, JsonUtility.ToJson(Colour) },
                { PhotonVRKeys.Cosmetics, OwnedOnly(Cosmetics) },
                { PhotonVRKeys.CosmeticTints, new Dictionary<string, string>(CosmeticTints) }
            });
        }

        // Our own client changed our properties without going through PhotonVR (e.g. a name computer script setting
        // PhotonNetwork.NickName). Take the change on board instead of undoing it, as long as it passes the filter and
        // the ownership gate.
        internal void AdoptLocalChange(Hashtable changed)
        {
            bool cosmeticsChanged = false, republish = false;

            if (changed.TryGetValue(ActorProperties.PlayerName, out object nameObj) && nameObj is string rawName)
            {
                string name = PhotonVRWordFilter.StripMarkup(rawName).Trim();
                if (name.Length == 0 || name.Length > PhotonVRProfiles.MaxNameLength || PhotonVRWordFilter.Default.IsBlocked(name))
                {
                    PhotonVRLog.Warn("That name is not allowed; keeping the previous one.");
                    UsernameRejected?.Invoke(rawName);
                    SetLocalNickName(AcceptedName);
                }
                else
                {
                    if (name != rawName) SetLocalNickName(name);
                    AcceptUsername(name);
                }
            }

            if (changed.TryGetValue(PhotonVRKeys.Colour, out object colourObj) && colourObj is string colourJson)
            {
                try
                {
                    Colour = JsonUtility.FromJson<Color>(colourJson);
                    PlayerPrefs.SetString(PhotonVRKeys.PrefsColour, colourJson);
                    ColourChanged?.Invoke(Colour);
                    republish = true;
                }
                catch (Exception) { republish = true; }
            }

            if (changed.TryGetValue(PhotonVRKeys.Cosmetics, out object cosObj))
            {
                Dictionary<string, string> incoming = PhotonVRCosmeticUtility.ToStringDictionary(cosObj);
                Dictionary<string, string> allowed = OwnedOnly(incoming);
                foreach (var kv in incoming)
                    if (allowed[kv.Key] != kv.Value) RejectCosmetic(kv.Key, kv.Value);
                Cosmetics = incoming;
                cosmeticsChanged = true;
                // Something unowned was written: send the filtered set instead.
                if (allowed.Count != incoming.Count || !PhotonVRCosmeticUtility.SameDictionary(allowed, incoming))
                    SetLocalProperties(new Hashtable { { PhotonVRKeys.Cosmetics, allowed } });
            }

            if (changed.TryGetValue(PhotonVRKeys.CosmeticTints, out object tintObj))
            {
                CosmeticTints = PhotonVRCosmeticUtility.ToStringDictionary(tintObj);
                cosmeticsChanged = true;
            }

            if (cosmeticsChanged)
            {
                PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmetics, Cosmetics);
                PhotonVRValueSaver.SaveDictionary(PhotonVRKeys.PrefsCosmeticTints, CosmeticTints);
                CosmeticsChanged?.Invoke(new Dictionary<string, string>(Cosmetics));
                republish = true;
            }

            if (!republish) return;
            PlayerPrefs.Save();
            PhotonVRProfiles.PublishLocal();
            PhotonVRSaveSystem.SaveLocalPlayer();
            RefreshLocal();
        }

        // ResyncOnMasterSwitch: compare our properties with what we meant to send and fix them once. No network
        // traffic unless something actually differs.
        internal void ResyncLocalProperties()
        {
            if (!PhotonNetwork.InRoom) return;
            string name = AcceptedName;
            if (!string.IsNullOrEmpty(name) && PhotonNetwork.LocalPlayer.NickName != name)
                SetLocalNickName(name);

            var props = PhotonNetwork.LocalPlayer.CustomProperties;
            var fix = new Hashtable();
            string colour = JsonUtility.ToJson(Colour);
            if (!(props.TryGetValue(PhotonVRKeys.Colour, out object c) && c as string == colour))
                fix[PhotonVRKeys.Colour] = colour;
            var cosmetics = OwnedOnly(Cosmetics);
            if (!(props.TryGetValue(PhotonVRKeys.Cosmetics, out object cos) && PhotonVRCosmeticUtility.SameDictionary(PhotonVRCosmeticUtility.ToStringDictionary(cos), cosmetics)))
                fix[PhotonVRKeys.Cosmetics] = cosmetics;
            if (!(props.TryGetValue(PhotonVRKeys.CosmeticTints, out object t) && PhotonVRCosmeticUtility.SameDictionary(PhotonVRCosmeticUtility.ToStringDictionary(t), CosmeticTints)))
                fix[PhotonVRKeys.CosmeticTints] = new Dictionary<string, string>(CosmeticTints);
            if (fix.Count > 0) SetLocalProperties(fix);
        }

        // Applies the change on our own player right away. Remote clients update through OnPlayerPropertiesUpdate;
        // the RPC sent by RefreshPlayerValues is kept for clients still running the original PhotonVR.
        private static void RefreshLocal()
        {
            if (PhotonNetwork.InRoom && Manager != null && Manager.LocalPlayer != null)
                Manager.LocalPlayer.RefreshPlayerValues();
        }
    }
}
