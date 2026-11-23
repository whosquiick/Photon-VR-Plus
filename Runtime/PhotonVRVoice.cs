// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
using System;
using UnityEngine;

using Photon.Realtime;
using Photon.Voice.Unity;

namespace Photon.VR
{
    /// <summary>
    /// Voice chat bootstrap. Put it next to the PUN voice client (PunVoiceClient in Voice 2.5x+,
    /// PhotonVoiceNetwork in older versions) and a <see cref="Recorder"/>. Missing pieces are added at runtime.
    /// The Recorder becomes the voice client's PrimaryRecorder, so the local player's PhotonVoiceView
    /// transmits your microphone, and every remote player's PhotonVoiceView plays through its Speaker.
    /// The Voice App ID comes from <see cref="PhotonVRManager.VoiceAppId"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public class PhotonVRVoice : MonoBehaviour
    {
        public static PhotonVRVoice Instance { get; private set; }

        [Tooltip("The microphone recorder. Found or created on this object if empty.")]
        public Recorder Recorder;
        [Tooltip("Start with the microphone transmitting.")]
        public bool TransmitOnStart = true;
        [Tooltip("Only transmit while you are talking (voice detection).")]
        public bool UseVoiceDetection = false;

        /// <summary>The PUN voice client (PunVoiceClient / PhotonVoiceNetwork).</summary>
        public VoiceConnection Connection { get; private set; }

        /// <summary>Raised when the mute state changes (true = muted).</summary>
        public static event Action<bool> MuteChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PhotonVR] Multiple PhotonVRVoice objects - destroying the duplicate on " + name);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Connection = FindVoiceConnection();
            if (Connection == null)
                Connection = AddPunVoiceClient(gameObject);

            if (Recorder == null)
                Recorder = GetComponent<Recorder>();
            if (Recorder == null)
                Recorder = gameObject.AddComponent<Recorder>();

            if (Connection != null)
                Connection.PrimaryRecorder = Recorder;
            else
                Debug.LogError("[PhotonVR] Could not find or create a Photon Voice PUN client. Is Photon Voice 2 imported?");

            Recorder.TransmitEnabled = TransmitOnStart;
            Recorder.VoiceDetection = UseVoiceDetection;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Mutes / unmutes your microphone for everyone.</summary>
        public static void SetMuted(bool muted)
        {
            if (Instance == null || Instance.Recorder == null) return;
            Instance.Recorder.TransmitEnabled = !muted;
            MuteChanged?.Invoke(muted);
        }

        /// <summary>True if your microphone is not transmitting.</summary>
        public static bool IsMuted => Instance == null || Instance.Recorder == null || !Instance.Recorder.TransmitEnabled;

        /// <summary>Toggles mute and returns the new state (true = muted).</summary>
        public static bool ToggleMute()
        {
            bool muted = !IsMuted;
            SetMuted(muted);
            return muted;
        }

        /// <summary>
        /// Called by PhotonVRManager on connect. The PUN voice client normally copies PhotonServerSettings
        /// ("Use PUN App Settings"); this also fills its own Settings in case that option is turned off.
        /// </summary>
        public static void ApplyAppSettings(string voiceAppId, string fixedRegion)
        {
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc == null || vc.Settings == null)
                return;
            vc.Settings.AppIdVoice = voiceAppId;
            if (!string.IsNullOrEmpty(fixedRegion))
                vc.Settings.FixedRegion = fixedRegion;
        }

        private static VoiceConnection FindVoiceConnection()
        {
            // Only PUN-linked clients (they follow the PUN room). Their type names differ by Voice version.
            foreach (VoiceConnection vc in FindObjectsOfType<VoiceConnection>())
            {
                string typeName = vc.GetType().Name;
                if (typeName == "PunVoiceClient" || typeName == "PhotonVoiceNetwork")
                    return vc;
            }
            return null;
        }

        private static VoiceConnection AddPunVoiceClient(GameObject go)
        {
            // Voice 2.5x+ calls it PunVoiceClient; older Voice 2 calls it PhotonVoiceNetwork.
            // Look it up by name so this compiles against either version.
            string[] candidates =
            {
                "Photon.Voice.PUN.PunVoiceClient, PhotonVoice.PUN",
                "Photon.Voice.PUN.PhotonVoiceNetwork, PhotonVoice.PUN",
                "Photon.Voice.PUN.PunVoiceClient, Assembly-CSharp",
                "Photon.Voice.PUN.PhotonVoiceNetwork, Assembly-CSharp",
            };
            foreach (string name in candidates)
            {
                Type t = Type.GetType(name, false);
                if (t != null && typeof(VoiceConnection).IsAssignableFrom(t))
                    return (VoiceConnection)go.AddComponent(t);
            }
            return null;
        }
    }
}
