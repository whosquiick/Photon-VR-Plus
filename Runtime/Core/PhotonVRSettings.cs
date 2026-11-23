using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Photon.VR
{
    /// <summary>
    /// Optional project-wide settings asset. When assigned to <see cref="PhotonVRManager.Settings"/> (or placed at
    /// <c>Resources/PhotonVRSettings</c>), every non-empty value here overrides the matching manager field when connecting.
    /// The App IDs are stored encrypted (<see cref="PhotonVRProtectedString"/>); set them with the masked fields in the
    /// inspector or Tools > PhotonVR > Set App IDs. Create the asset with Assets > Create > PhotonVR > Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "PhotonVRSettings", menuName = "PhotonVR/Settings", order = 0)]
    public class PhotonVRSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>Resources path used when no settings asset is assigned on the manager.</summary>
        public const string DefaultResourcesPath = "PhotonVRSettings";

        [Header("Photon IDs (stored encrypted)")]
        [Tooltip("Photon PUN (Realtime) App ID from dashboard.photonengine.com")]
        [SerializeField] private PhotonVRProtectedString appId = new PhotonVRProtectedString();
        [Tooltip("Photon Voice App ID from dashboard.photonengine.com")]
        [SerializeField] private PhotonVRProtectedString voiceAppId = new PhotonVRProtectedString();

        // v1.0.0 stored these in plaintext; migrated into the encrypted fields and cleared.
        [FormerlySerializedAs("AppId")] [SerializeField, HideInInspector] private string legacyAppId = "";
        [FormerlySerializedAs("VoiceAppId")] [SerializeField, HideInInspector] private string legacyVoiceAppId = "";

        [Header("Connection")]
        [Tooltip("Fixed region code (eu, us, usw, asia, jp, au, ...). Empty = use the manager value; \"best\" = best region.")]
        public string Region = "";
        [Tooltip("Game version. Players only meet players on the same version. Empty = leave PhotonServerSettings unchanged.")]
        public string GameVersion = "";

        [Header("Security")]
        [Tooltip("Keep the IDs encrypted in memory with a per-session key and scrub them from Photon while connected. Off by default. See README > Protecting your App IDs.")]
        public bool HardenMemory = false;

        [Header("Rates (0 = keep manager value)")]
        [Tooltip("PhotonNetwork.SendRate (packets per second).")]
        public int SendRate = 0;
        [Tooltip("PhotonNetwork.SerializationRate (OnPhotonSerializeView calls per second).")]
        public int SerializationRate = 0;

        /// <summary>Photon PUN App ID (getter decrypts, setter encrypts). Same name as in v1.0.0.</summary>
        public string AppId { get => appId.Get(); set => appId.Set(value); }

        /// <summary>Photon Voice App ID (getter decrypts, setter encrypts). Same name as in v1.0.0.</summary>
        public string VoiceAppId { get => voiceAppId.Get(); set => voiceAppId.Set(value); }

        /// <summary>True when an App ID is stored.</summary>
        public bool HasAppId => appId.HasValue;

        /// <summary>True when a Voice App ID is stored.</summary>
        public bool HasVoiceAppId => voiceAppId.HasValue;

        internal byte[] GetIdBytes(PhotonVRAppIdKind kind) =>
            kind == PhotonVRAppIdKind.Realtime ? appId.GetBytes() : voiceAppId.GetBytes();

        internal void ForgetIdsInMemory()
        {
            appId.ForgetInMemory();
            voiceAppId.ForgetInMemory();
        }

        /// <summary>Loads the settings asset from Resources/PhotonVRSettings (null if none exists).</summary>
        public static PhotonVRSettings LoadDefault() => Resources.Load<PhotonVRSettings>(DefaultResourcesPath);

        [NonSerialized] private bool migratedLegacyIds;

        void ISerializationCallbackReceiver.OnBeforeSerialize() => MigrateLegacyIds();
        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacyIds();

        private void MigrateLegacyIds()
        {
            if (!string.IsNullOrEmpty(legacyAppId))
            {
                if (!appId.HasValue) appId.Set(legacyAppId);
                legacyAppId = string.Empty;
                migratedLegacyIds = true;
            }
            if (!string.IsNullOrEmpty(legacyVoiceAppId))
            {
                if (!voiceAppId.HasValue) voiceAppId.Set(legacyVoiceAppId);
                legacyVoiceAppId = string.Empty;
                migratedLegacyIds = true;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!migratedLegacyIds) return;
            migratedLegacyIds = false;
            PhotonVREditMode.Delay(() =>
            {
                if (this == null) return;
                PhotonVREditMode.SetDirty(this);
                PhotonVREditMode.SaveAsset(this);
                PhotonVRLog.Warn("Moved plaintext App IDs in " + name + " into encrypted storage.", this);
            });
        }
#endif
    }
}
