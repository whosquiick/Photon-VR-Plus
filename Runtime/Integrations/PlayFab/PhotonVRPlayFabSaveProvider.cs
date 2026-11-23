#if PHOTONVR_PLAYFAB
using System;
using System.Collections.Generic;

using Photon.VR.Saving;
using PlayFab;
using PlayFab.ClientModels;

namespace Photon.VR.PlayFab
{
    /// <summary>Stores the PhotonVR save (name, colour, cosmetics, tints) in PlayFab user data under one key.</summary>
    public class PhotonVRPlayFabSaveProvider : IPhotonVRSaveProvider
    {
        public string Key = "PhotonVRSave";
        public bool SyncDisplayName;
        public string Name => "PlayFab";

        private string pushedName;

        public void Load(Action<PhotonVRSaveData> done)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) { done(null); return; }
            PlayFabClientAPI.GetUserData(new GetUserDataRequest { Keys = new List<string> { Key } },
                result =>
                {
                    done(result.Data != null && result.Data.TryGetValue(Key, out UserDataRecord record)
                        ? PhotonVRSaveJson.FromJson(record.Value)
                        : null);
                },
                e =>
                {
                    PhotonVRLog.Warn("PlayFab load failed: " + e.ErrorMessage);
                    done(null);
                });
        }

        public void Save(PhotonVRSaveData data)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn() || data == null) return;
            PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest
                {
                    Data = new Dictionary<string, string> { { Key, PhotonVRSaveJson.ToJson(data) } },
                    Permission = UserDataPermission.Public
                },
                _ => { }, e => PhotonVRLog.Warn("PlayFab save failed: " + e.ErrorMessage));

            // PlayFab display names must be 3-25 chars; PlayFab can also run its own profanity check on them.
            if (SyncDisplayName && !string.IsNullOrEmpty(data.Username) && data.Username.Length >= 3 && data.Username != pushedName)
            {
                pushedName = data.Username;
                PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = data.Username },
                    _ => { }, e => PhotonVRLog.Warn("PlayFab display name: " + e.ErrorMessage));
            }
        }
    }
}
#endif
