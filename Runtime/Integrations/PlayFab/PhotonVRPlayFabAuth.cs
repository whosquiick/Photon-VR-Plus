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

        private PhotonVRPlayFabSaveProvider saveProvider;

        private void OnEnable()
        {
            if (SyncDisplayName) PhotonVRManager.UsernameChanged += PushDisplayName;
            if (LoadOwnedCosmetics) PhotonVRManager.ConnectedToMaster += LoadOwnedCosmeticsIfNeeded;
        }

        private void OnDisable()
        {
            PhotonVRManager.UsernameChanged -= PushDisplayName;
            PhotonVRManager.ConnectedToMaster -= LoadOwnedCosmeticsIfNeeded;
        }

        private void Start()
        {
            if (!LoginOnStart) return;
            if (Mode == LoginMode.CustomId && !string.IsNullOrEmpty(CustomId)) LoginWithCustomId(CustomId);
            else LoginWithDeviceId();
        }

        public void LoginWithDeviceId() => LoginWithCustomId(SystemInfo.deviceUniqueIdentifier);

        public void LoginWithCustomId(string id)
        {
            if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
            {
                Fail("PlayFab Title ID is not set (Window > PlayFab > Editor Extensions, or PlayFabSharedSettings).");
                return;
            }
            PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest { CustomId = id, CreateAccount = true },
                OnLoggedIn, e => Fail(e.GenerateErrorReport()));
        }

        /// <summary>Call this yourself if you log in through another PlayFab flow (Oculus, Steam, email...).</summary>
        public void OnLoggedIn(LoginResult result)
        {
            PlayFabId = result.PlayFabId;
            if (UsePlayFabSaves && saveProvider == null)
            {
                saveProvider = new PhotonVRPlayFabSaveProvider();
                Photon.VR.Saving.PhotonVRSaveSystem.Register(saveProvider);
            }

            if (LoadOwnedCosmetics) RefreshOwnedCosmetics();

            string appId = PhotonVRManager.Manager != null ? PhotonVRManager.Manager.AppId : PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;
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
            LoginSucceeded?.Invoke(PlayFabId);
        }

        private void OnDestroy()
        {
            if (saveProvider != null) Photon.VR.Saving.PhotonVRSaveSystem.Unregister(saveProvider);
        }

        private bool cosmeticsLoaded;
        private void LoadOwnedCosmeticsIfNeeded()
        {
            if (!cosmeticsLoaded) RefreshOwnedCosmetics();
        }

        /// <summary>Reads the PlayFab inventory into PhotonVRManager.SetOwnedCosmetics. Call again after a purchase.</summary>
        public void RefreshOwnedCosmetics()
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), result =>
            {
                cosmeticsLoaded = true;
                var ids = new List<string>();
                if (result.Inventory != null)
                    foreach (ItemInstance item in result.Inventory)
                        if (item != null && item.ItemId != null && item.ItemId.StartsWith(CosmeticItemPrefix ?? "", StringComparison.Ordinal))
                            ids.Add(item.ItemId.Substring((CosmeticItemPrefix ?? "").Length));
                PhotonVRManager.SetOwnedCosmetics(ids);
            }, e => PhotonVRLog.Warn("PlayFab inventory not loaded: " + e.ErrorMessage));
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

        private static void Fail(string error)
        {
            PhotonVRLog.Error("PlayFab login failed: " + error);
            LoginFailed?.Invoke(error);
        }
    }
}
#endif
