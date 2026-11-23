#if PHOTONVR_PLAYFAB
using System;

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
        [Tooltip("Also push the name to the PlayFab title display name (PlayFab can run its own profanity filter on it).")]
        public bool SyncDisplayName = false;

        public static string PlayFabId { get; private set; }
        public static bool LoggedIn => !string.IsNullOrEmpty(PlayFabId) && PlayFabClientAPI.IsClientLoggedIn();

        public static event Action<string> LoginSucceeded;
        public static event Action<string> LoginFailed;

        private PhotonVRPlayFabSaveProvider saveProvider;

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
                saveProvider = new PhotonVRPlayFabSaveProvider { SyncDisplayName = SyncDisplayName };
                Photon.VR.Saving.PhotonVRSaveSystem.Register(saveProvider);
            }

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

        private static void Fail(string error)
        {
            PhotonVRLog.Error("PlayFab login failed: " + error);
            LoginFailed?.Invoke(error);
        }
    }
}
#endif
