namespace Photon.VR
{
    // FindObjectOfType / FindObjectsOfType are marked obsolete from Unity 2023.1, which spams warnings
    // (and fails builds that treat warnings as errors). This picks the right call for the editor version.
    internal static class PhotonVRFind
    {
        internal static T First<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>();
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        internal static T[] All<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            // Sorted so "first match" picks stay the same as before.
            return UnityEngine.Object.FindObjectsByType<T>(UnityEngine.FindObjectsSortMode.InstanceID);
#else
            return UnityEngine.Object.FindObjectsOfType<T>();
#endif
        }
    }
}
