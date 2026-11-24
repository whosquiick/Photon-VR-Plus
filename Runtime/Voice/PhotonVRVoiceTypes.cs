using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Photon.VR
{
    public enum PhotonVRTalkMode
    {
        OpenMic,
        PushToTalk,
        VoiceActivation
    }

    public enum PhotonVRXRButton
    {
        None,
        Primary,
        Secondary,
        Trigger,
        Grip,
        Menu,
        Primary2DAxisClick
    }

    /// <summary>How a remote player's voice sounds in the world.</summary>
    public enum PhotonVRVoicePreset
    {
        /// <summary>Leave the AudioSource as configured.</summary>
        Custom,
        /// <summary>Full 3D, fades out over ~15 m. Good default for social VR.</summary>
        Proximity,
        /// <summary>3D, but audible across a large map (~40 m).</summary>
        LargeRoom,
        /// <summary>Spatialised up close, never fully silent (minimum 30% volume).</summary>
        Hybrid,
        /// <summary>2D: everyone hears everyone at full volume.</summary>
        Global
    }

    public static class PhotonVRVoicePresets
    {
        public static void Apply(AudioSource source, PhotonVRVoicePreset preset)
        {
            if (source == null || preset == PhotonVRVoicePreset.Custom) return;
            switch (preset)
            {
                case PhotonVRVoicePreset.Proximity:
                    Set(source, 1f, 1f, 15f, AudioRolloffMode.Logarithmic);
                    break;
                case PhotonVRVoicePreset.LargeRoom:
                    Set(source, 1f, 2f, 40f, AudioRolloffMode.Logarithmic);
                    break;
                case PhotonVRVoicePreset.Hybrid:
                    Set(source, 0.7f, 1f, 25f, AudioRolloffMode.Linear);
                    break;
                case PhotonVRVoicePreset.Global:
                    Set(source, 0f, 1f, 500f, AudioRolloffMode.Linear);
                    break;
            }
        }

        private static void Set(AudioSource s, float blend, float min, float max, AudioRolloffMode mode)
        {
            s.spatialBlend = blend;
            s.minDistance = min;
            s.maxDistance = max;
            s.rolloffMode = mode;
            s.dopplerLevel = 0f; // doppler on voices sounds wrong with network jitter
        }
    }

    /// <summary>Tiny wrapper over UnityEngine.XR input so buttons work with any XR plugin, without the Input System package.</summary>
    public static class PhotonVRXRInput
    {
        private static readonly List<InputDevice> devices = new List<InputDevice>();

        public static bool GetButton(XRNode hand, PhotonVRXRButton button)
        {
            if (button == PhotonVRXRButton.None) return false;
            InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
            if (!device.isValid) return false;
            InputFeatureUsage<bool> usage;
            switch (button)
            {
                case PhotonVRXRButton.Primary: usage = CommonUsages.primaryButton; break;
                case PhotonVRXRButton.Secondary: usage = CommonUsages.secondaryButton; break;
                case PhotonVRXRButton.Trigger: usage = CommonUsages.triggerButton; break;
                case PhotonVRXRButton.Grip: usage = CommonUsages.gripButton; break;
                case PhotonVRXRButton.Menu: usage = CommonUsages.menuButton; break;
                default: usage = CommonUsages.primary2DAxisClick; break;
            }
            return device.TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        public static float GetAxis(XRNode hand, bool grip)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
            return device.isValid && device.TryGetFeatureValue(grip ? CommonUsages.grip : CommonUsages.trigger, out float v) ? v : 0f;
        }
    }

    /// <summary>
    /// Players you muted for yourself. Stored by UserId (so it survives rejoining when the room publishes UserIds)
    /// and saved in PlayerPrefs.
    /// </summary>
    public static class PhotonVRMuteList
    {
        public static event System.Action Changed;

        private static HashSet<string> muted;

        // Domain reload can be turned off (Enter Play Mode Options); start every play session clean.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            muted = null;
            Changed = null;
        }

        private static HashSet<string> Set
        {
            get
            {
                if (muted == null)
                {
                    muted = new HashSet<string>();
                    foreach (string id in PlayerPrefs.GetString(PhotonVRKeys.PrefsMuted, "").Split('\n'))
                        if (!string.IsNullOrEmpty(id)) muted.Add(id);
                }
                return muted;
            }
        }

        private static string Key(Photon.Realtime.Player p) =>
            p == null ? null : !string.IsNullOrEmpty(p.UserId) ? p.UserId : "actor:" + p.ActorNumber;

        public static bool IsMuted(Photon.Realtime.Player player)
        {
            string key = Key(player);
            return key != null && Set.Contains(key);
        }

        public static void SetMuted(Photon.Realtime.Player player, bool mute)
        {
            string key = Key(player);
            if (key == null || player.IsLocal) return;
            bool changed = mute ? Set.Add(key) : Set.Remove(key);
            if (!changed) return;
            Save();
            Changed?.Invoke();
        }

        public static bool Toggle(Photon.Realtime.Player player)
        {
            bool now = !IsMuted(player);
            SetMuted(player, now);
            return now;
        }

        public static void Clear()
        {
            Set.Clear();
            Save();
            Changed?.Invoke();
        }

        private static void Save()
        {
            var persistent = new List<string>();
            foreach (string k in Set)
                if (!k.StartsWith("actor:")) persistent.Add(k);
            PlayerPrefs.SetString(PhotonVRKeys.PrefsMuted, string.Join("\n", persistent));
            PlayerPrefs.Save();
        }
    }
}
