using UnityEngine;

using Photon.Pun;
using Photon.Voice.PUN;
using Photon.Voice.Unity;

namespace Photon.VR
{
    /// <summary>
    /// Per-player voice glue on the networked player prefab. Makes sure a <see cref="PhotonVoiceView"/> and a
    /// 3D <see cref="Speaker"/> (on the head) exist, exposes <see cref="IsSpeaking"/>, toggles an optional
    /// speaking indicator, and lets you mute or change the volume of a remote player locally.
    /// The local microphone is the voice client's PrimaryRecorder (see <see cref="PhotonVRVoice"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PhotonVR/PhotonVR Player Voice")]
    public class PhotonVRPlayerVoice : MonoBehaviourPun
    {
        [Tooltip("Where to create the Speaker when the prefab has none (usually the Head).")]
        public Transform SpeakerParent;
        [Tooltip("Optional object shown while this player is talking.")]
        public GameObject SpeakingIndicator;
        [Tooltip("Spatial blend of a Speaker created at runtime (1 = full 3D).")]
        [Range(0f, 1f)] public float SpatialBlend = 1f;
        [Tooltip("Max hearing distance of a Speaker created at runtime.")]
        public float MaxDistance = 50f;
        [Tooltip("Applies a spatial voice preset to the speaker. Custom = keep the AudioSource settings.")]
        public PhotonVRVoicePreset Preset = PhotonVRVoicePreset.Custom;

        /// <summary>The PhotonVoiceView of this player.</summary>
        public PhotonVoiceView VoiceView { get; private set; }

        /// <summary>The Speaker used for this player (may be null before setup).</summary>
        public Speaker Speaker { get; private set; }

        /// <summary>The AudioSource that plays this player's voice.</summary>
        public AudioSource Source { get; private set; }

        /// <summary>True while this player is talking (remote: audio playing; local: transmitting).</summary>
        public bool IsSpeaking => VoiceView != null && (photonView.IsMine ? VoiceView.IsRecording : VoiceView.IsSpeaking);

        /// <summary>Local volume of this player's voice (only on your client).</summary>
        public float Volume
        {
            get => Source != null ? Source.volume : 1f;
            set { if (Source != null) Source.volume = Mathf.Clamp01(value); }
        }

        /// <summary>Mutes this player for you only (e.g. a "mute player" button on the scoreboard).</summary>
        public bool LocallyMuted
        {
            get => Source != null && Source.mute;
            set { if (Source != null) Source.mute = value; }
        }

        private void Awake() => Setup();

        /// <summary>Creates the missing voice pieces. Safe to call more than once.</summary>
        public void Setup()
        {
            Speaker = GetComponentInChildren<Speaker>(true);
            if (Speaker == null)
            {
                var speakerGo = new GameObject("Speaker");
                if (SpeakerParent == null)
                {
                    var player = GetComponent<Photon.VR.Player.PhotonVRPlayer>();
                    SpeakerParent = player != null && player.Head != null ? player.Head : transform;
                }
                speakerGo.transform.SetParent(SpeakerParent, false);
                var source = speakerGo.AddComponent<AudioSource>();
                source.spatialBlend = SpatialBlend;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.maxDistance = MaxDistance;
                source.playOnAwake = true;
                Speaker = speakerGo.AddComponent<Speaker>();
            }
            Source = Speaker.GetComponent<AudioSource>();
            PhotonVRVoicePresets.Apply(Source, Preset);

            VoiceView = GetComponent<PhotonVoiceView>();
            if (VoiceView == null)
                VoiceView = gameObject.AddComponent<PhotonVoiceView>();
        }

        /// <summary>Changes the voice preset of this player at runtime.</summary>
        public void ApplyPreset(PhotonVRVoicePreset preset)
        {
            Preset = preset;
            PhotonVRVoicePresets.Apply(Source, preset);
        }

        /// <summary>Applies a preset to every spawned player.</summary>
        public static void ApplyPresetToAll(PhotonVRVoicePreset preset)
        {
            foreach (var p in Photon.VR.Player.PhotonVRPlayer.All)
                if (p != null && p.Voice != null) p.Voice.ApplyPreset(preset);
        }

        private void OnEnable()
        {
            PhotonVRMuteList.Changed += ApplyMuteList;
            ApplyMuteList();
        }

        private void OnDisable() => PhotonVRMuteList.Changed -= ApplyMuteList;

        private void ApplyMuteList()
        {
            if (photonView != null && !photonView.IsMine && Source != null)
                Source.mute = PhotonVRMuteList.IsMuted(photonView.Owner);
        }

        private void Update()
        {
            if (SpeakingIndicator == null) return;
            bool speaking = IsSpeaking;
            if (SpeakingIndicator.activeSelf != speaking)
                SpeakingIndicator.SetActive(speaking);
        }
    }
}
