#if PHOTONVR_PLAYFAB
using System;
using System.Collections.Generic;

using Photon.Pun;
using Photon.Realtime;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Photon.VR.PlayFab
{
    /// <summary>
    /// Logs in to PlayFab, swaps the session for a Photon custom-auth token and connects through PhotonVRManager.
    /// With PlayFab set as the custom auth provider in the Photon dashboard (and anonymous clients disallowed),
    /// Photon only accepts players holding a valid PlayFab ticket, and their UserId is their PlayFabId.
    /// </summary>
    public class PhotonVRPlayFabAuth : MonoBehaviour
    {
        public enum LoginMode { DeviceId, CustomId }

        public bool LoginOnStart = true;
        public LoginMode Mode = LoginMode.DeviceId;
        [Tooltip("Used with LoginMode.CustomId. Leave empty to call LoginWithCustomId from code.")]
        public string CustomId = "";
        [Tooltip("Load and save name / colour / cosmetics in PlayFab user data.")]
        public bool UsePlayFabSaves = true;
        [Tooltip("After logging in, get a Photon token from PlayFab and connect with Photon custom auth (needs PlayFab set as the auth provider in the Photon dashboard). Off = PhotonVRManager connects on its own.")]
        public bool UsePhotonCustomAuth = true;
        [Tooltip("Push every name change to the PlayFab title display name (UpdateUserTitleDisplayName). Works with your own login too: untick Login On Start.")]
        public bool SyncDisplayName = false;
        [Tooltip("Load the player's PlayFab inventory as their owned cosmetics (used when Require Owned Cosmetics is on in the manager).")]
        public bool LoadOwnedCosmetics = false;
        [Tooltip("Prefix on PlayFab ItemIds, e.g. \"cosmetic_\" makes item \"cosmetic_VRTopHat\" own cosmetic \"VRTopHat\". Empty = ItemId is the cosmetic ID.")]
        public string CosmeticItemPrefix = "";

        public static string PlayFabId { get; private set; }
        public static bool LoggedIn => !string.IsNullOrEmpty(PlayFabId) && PlayFabClientAPI.IsClientLoggedIn();

        public static event Action<string> LoginSucceeded;
        public static event Action<string> LoginFailed;

        // Domain reload can be turned off (Enter Play Mode Options); start every play session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAuthStatics()
        {
            PlayFabId = null;
            LoginSucceeded = null;
            LoginFailed = null;
            pushedName = null;
        }

        private PhotonVRPlayFabSaveProvider saveProvider;

        protected virtual void OnEnable()
        {
            if (SyncDisplayName) PhotonVRManager.UsernameChanged += PushDisplayName;
            if (LoadOwnedCosmetics) PhotonVRManager.ConnectedToMaster += LoadOwnedCosmeticsIfNeeded;
        }

        protected virtual void OnDisable()
        {
            PhotonVRManager.UsernameChanged -= PushDisplayName;
            PhotonVRManager.ConnectedToMaster -= LoadOwnedCosmeticsIfNeeded;
        }

        protected virtual void Start()
        {
            if (!LoginOnStart) return;
            if (Mode == LoginMode.CustomId && !string.IsNullOrEmpty(CustomId)) LoginWithCustomId(CustomId);
            else LoginWithDeviceId();
        }

        public void LoginWithDeviceId() => LoginWithCustomId(DeviceLoginId());

        private const string FallbackIdKey = "PhotonVR_PlayFabDeviceId";

        // Some platforms (WebGL, some Android setups) return "n/a" or an empty device ID. Logging in with that
        // would put every one of those players on the same PlayFab account, so use a random ID kept on this device.
        private static string DeviceLoginId()
        {
            string id = SystemInfo.deviceUniqueIdentifier;
            if (!string.IsNullOrEmpty(id) && id != SystemInfo.unsupportedIdentifier && id.Trim('0', '-').Length > 0)
                return id;
            string saved = PlayerPrefs.GetString(FallbackIdKey, "");
            if (string.IsNullOrEmpty(saved))
            {
                saved = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(FallbackIdKey, saved);
                PlayerPrefs.Save();
            }
            return saved;
        }

        public void LoginWithCustomId(string id)
        {
            ApplyTitleId();
            if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
            {
                Fail("PlayFab Title ID is not set (Window > PlayFab > Editor Extensions, or PlayFabSharedSettings).");
                return;
            }
            lastLoginId = id;
            PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest { CustomId = id, CreateAccount = true, InfoRequestParameters = LoginInfo },
                OnLoggedIn, OnLoginError);
        }

        private string lastLoginId;

        /// <summary>Logs in again with the last ID used (for retries).</summary>
        protected void RetryLastLogin()
        {
            if (!string.IsNullOrEmpty(lastLoginId)) LoginWithCustomId(lastLoginId);
        }

        /// <summary>Extra data to ask for with the login (null = none).</summary>
        protected virtual GetPlayerCombinedInfoRequestParams LoginInfo => null;

        protected virtual void OnLoginError(PlayFabError error) => Fail(error.GenerateErrorReport());

        /// <summary>Call this yourself if you log in through another PlayFab flow (Oculus, Steam, email...).</summary>
        public virtual void OnLoggedIn(LoginResult result)
        {
            PlayFabId = result.PlayFabId;
            if (UsePlayFabSaves && saveProvider == null)
            {
                saveProvider = new PhotonVRPlayFabSaveProvider();
                Photon.VR.Saving.PhotonVRSaveSystem.Register(saveProvider);
            }

            if (LoadOwnedCosmetics) RefreshOwnedCosmetics();
            LoginSucceeded?.Invoke(PlayFabId);
            if (!UsePhotonCustomAuth) return;

            string appId = PhotonVRManager.Manager != null ? PhotonVRManager.Manager.AppId
                : PhotonNetwork.PhotonServerSettings != null && PhotonNetwork.PhotonServerSettings.AppSettings != null
                    ? PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime : null;
            if (string.IsNullOrEmpty(appId))
            {
                Fail("No Photon App ID set, so there's nothing to get a Photon token for. Set it with Tools > PhotonVR > Set App IDs.");
                return;
            }
            PlayFabClientAPI.GetPhotonAuthenticationToken(new GetPhotonAuthenticationTokenRequest { PhotonApplicationId = appId },
                token => ConnectToPhoton(token.PhotonCustomAuthenticationToken),
                e => Fail(e.GenerateErrorReport()));
        }

        private void ConnectToPhoton(string photonToken)
        {
            var auth = new AuthenticationValues { AuthType = CustomAuthenticationType.Custom, UserId = PlayFabId };
            auth.AddAuthParameter("username", PlayFabId);
            auth.AddAuthParameter("token", photonToken);
            PhotonVRManager.ConnectWithAuthentication(auth);
        }

        /// <summary>Gives the PlayFab SDK its Title ID before logging in. PhotonVRPlayFabManager uses its encrypted field.</summary>
        protected virtual void ApplyTitleId() { }

        protected virtual void OnDestroy()
        {
            if (saveProvider != null) Photon.VR.Saving.PhotonVRSaveSystem.Unregister(saveProvider);
        }

        private bool cosmeticsLoaded;
        private void LoadOwnedCosmeticsIfNeeded()
        {
            if (!cosmeticsLoaded) RefreshOwnedCosmetics();
        }

        /// <summary>Reads the PlayFab inventory into PhotonVRManager.SetOwnedCosmetics. Call again after a purchase.</summary>
        public void RefreshOwnedCosmetics() =>
            PhotonVRPlayFabManager.RefreshInventory(ReferenceEquals(this, PhotonVRPlayFabManager.Instance) ? null : (Action)ApplyOwnedCosmetics);

        // Inventory item IDs (minus the prefix) become the owned cosmetics checked by RequireOwnedCosmetics.
        internal void ApplyOwnedCosmetics()
        {
            cosmeticsLoaded = true;
            string prefix = CosmeticItemPrefix ?? "";
            var ids = new List<string>();
            foreach (string itemId in PhotonVRPlayFabManager.OwnedItemIds)
                if (itemId.StartsWith(prefix, StringComparison.Ordinal))
                    ids.Add(itemId.Substring(prefix.Length));
            PhotonVRManager.SetOwnedCosmetics(ids);
        }

        private static string pushedName;

        /// <summary>Sets the PlayFab title display name. Skips repeats and names PlayFab won't take (3-25 characters).</summary>
        public static void PushDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 25 || name == pushedName) return;
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            pushedName = name;
            PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = name },
                _ => { },
                e =>
                {
                    if (pushedName == name) pushedName = null;
                    PhotonVRLog.Warn("PlayFab display name not updated: " + e.ErrorMessage);
                });
        }

        protected static void Fail(string error)
        {
            PhotonVRLog.Error("PlayFab login failed: " + error);
            LoginFailed?.Invoke(error);
        }
    }
}
#endif
