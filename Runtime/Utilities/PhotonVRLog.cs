using UnityEngine;

namespace Photon.VR
{
    /// <summary>Prefixed logging for Photon VR Plus. Info messages respect <see cref="Verbose"/>.</summary>
    public static class PhotonVRLog
    {
        /// <summary>When false, <see cref="Info"/> messages are suppressed (warnings and errors always show).</summary>
        public static bool Verbose = true;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Verbose = true;
        }

        private const string Prefix = "[PhotonVR] ";

        /// <summary>Logs an informational message when <see cref="Verbose"/> is on.</summary>
        public static void Info(string message, Object context = null)
        {
            if (Verbose) Debug.Log(Prefix + message, context);
        }

        /// <summary>Logs a warning.</summary>
        public static void Warn(string message, Object context = null) => Debug.LogWarning(Prefix + message, context);

        /// <summary>Logs an error.</summary>
        public static void Error(string message, Object context = null) => Debug.LogError(Prefix + message, context);
    }
}
