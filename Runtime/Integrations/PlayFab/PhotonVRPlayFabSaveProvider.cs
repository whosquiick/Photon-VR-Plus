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

            if (SyncDisplayName) PhotonVRPlayFabAuth.PushDisplayName(data.Username);
        }
    }
}
#endif
