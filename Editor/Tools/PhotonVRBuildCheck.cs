#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Photon.VR
{
    // Warns when a build would ship the App IDs as plain text in PhotonServerSettings.
    public class PhotonVRBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PhotonVRAppIdWindow.HasPlaintextInServerSettings())
                PhotonVRLog.Warn("PhotonServerSettings contains plaintext App IDs that will be included in this build. " +
                                 "Use Tools > PhotonVR > Move App IDs Out Of PhotonServerSettings.");
        }
    }
}
#endif
