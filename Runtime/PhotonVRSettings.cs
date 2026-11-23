// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// Optional project-wide settings asset. When assigned to <see cref="PhotonVRManager.Settings"/>
    /// (or placed at Resources/PhotonVRSettings), every non-empty value here overrides the matching
    /// field on the manager when it connects.
    /// Create one with Assets > Create > PhotonVR > Settings, or Tools > PhotonVR > Create Settings Asset.
    /// </summary>
    [CreateAssetMenu(fileName = "PhotonVRSettings", menuName = "PhotonVR/Settings", order = 0)]
    public class PhotonVRSettings : ScriptableObject
    {
        /// <summary>Resources path used when no settings asset is assigned on the manager.</summary>
        public const string DefaultResourcesPath = "PhotonVRSettings";

        [Header("Photon IDs")]
        [Tooltip("Photon PUN (Realtime) App ID from dashboard.photonengine.com")]
        public string AppId;
        [Tooltip("Photon Voice App ID from dashboard.photonengine.com")]
        public string VoiceAppId;

        [Header("Connection")]
        [Tooltip("Fixed region code (eu, us, usw, asia, jp, au, ...). Leave empty to use the manager value, or set to \"best\" for the best region.")]
        public string Region = "";
        [Tooltip("Game version. Players only meet players on the same version. Empty = leave PhotonServerSettings unchanged.")]
        public string GameVersion = "";

        [Header("Rates (0 = keep manager value)")]
        [Tooltip("PhotonNetwork.SendRate (packets per second).")]
        public int SendRate = 0;
        [Tooltip("PhotonNetwork.SerializationRate (OnPhotonSerializeView calls per second).")]
        public int SerializationRate = 0;

        /// <summary>Loads the settings asset from Resources/PhotonVRSettings (null if none exists).</summary>
        public static PhotonVRSettings LoadDefault() => Resources.Load<PhotonVRSettings>(DefaultResourcesPath);
    }
}
