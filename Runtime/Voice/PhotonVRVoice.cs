using System;
using System.Reflection;
using UnityEngine;

using Photon.Pun;
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
    [AddComponentMenu("PhotonVR/PhotonVR Voice")]
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
                PhotonVRLog.Warn("Multiple PhotonVRVoice objects - removing the duplicate on " + name, this);
                Destroy(this);
                return;
            }
            Instance = this;
            // Works both standalone and as a child of the PhotonVRManager prefab.
            DontDestroyOnLoad(transform.root.gameObject);

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
                PhotonVRLog.Error("Could not find or create a Photon Voice PUN client. Is Photon Voice 2 imported?", this);

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
        /// Called by PhotonVRManager right before connecting. Gives the voice client its own copy of the connection
        /// settings (with the decrypted Voice App ID) and turns off "Use PUN App Settings", so the Voice App ID never
        /// has to be written into the shared PhotonServerSettings asset.
        /// </summary>
        public static void ApplyAppSettings(AppSettings punSettings)
        {
            if (punSettings == null) return;
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc == null)
                return;

            var voiceSettings = new AppSettings();
            punSettings.CopyTo(voiceSettings);
            vc.Settings = voiceSettings;

            if (!SetMember(vc, "UsePunAppSettings", false) && !SetMember(vc, "usePunAppSettings", false))
            {
                // Very old Voice versions: fall back to the shared settings (in memory only).
                PhotonVRLog.Warn("This Photon Voice version has no UsePunAppSettings switch - the Voice App ID is passed through PhotonServerSettings in memory.");
                PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice = punSettings.AppIdVoice;
            }
        }

        /// <summary>v1.0.0 overload: applies a Voice App ID and region to the voice client's own settings.</summary>
        public static void ApplyAppSettings(string voiceAppId, string fixedRegion)
        {
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc == null) return;
            if (vc.Settings == null) vc.Settings = new AppSettings();
            vc.Settings.AppIdVoice = voiceAppId;
            if (!string.IsNullOrEmpty(fixedRegion))
                vc.Settings.FixedRegion = fixedRegion;
        }

        // Used by memory hardening: blank / restore the Voice App ID held by the voice client.
        internal static void ScrubAppSettings()
        {
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc != null && vc.Settings != null)
                vc.Settings.AppIdVoice = string.Empty;
        }

        internal static void RestoreVoiceAppId(string voiceAppId)
        {
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc == null) return;
            if (vc.Settings == null) vc.Settings = new AppSettings();
            vc.Settings.AppIdVoice = voiceAppId;
        }

        /// <summary>Makes voice connect to the same cloud region PUN ended up in (called on OnConnectedToMaster).</summary>
        public static void FollowRegion(string cloudRegion)
        {
            if (string.IsNullOrEmpty(cloudRegion)) return;
            VoiceConnection vc = Instance != null && Instance.Connection != null ? Instance.Connection : FindVoiceConnection();
            if (vc == null || vc.Settings == null) return;
            int slash = cloudRegion.IndexOf('/');
            vc.Settings.FixedRegion = slash > 0 ? cloudRegion.Substring(0, slash) : cloudRegion;
        }

        private static bool SetMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type t = target.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(memberName, flags);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return true; }
                FieldInfo f = t.GetField(memberName, flags);
                if (f != null) { f.SetValue(target, value); return true; }
            }
            return false;
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
