using System;
using System.Collections;
using System.Collections.Generic;

using Photon.Pun;
using UnityEngine;

namespace Photon.VR.Player
{
    /// <summary>
    /// Networked emotes and gestures. Add next to PhotonVRPlayer and fill the list; the index is sent, so keep
    /// the list identical on every client. Only the avatar's owner can trigger its emotes, and it is rate limited.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class PhotonVREmotes : MonoBehaviourPun
    {
        [Serializable]
        public class Emote
        {
            public string Id;
            [Tooltip("Object shown while the emote plays (e.g. a speech bubble or particle).")]
            public GameObject Visual;
            public AudioClip Sound;
            [Tooltip("Animator trigger on the avatar, optional.")]
            public string AnimatorTrigger;
            public float Duration = 2f;
        }

        public List<Emote> Emotes = new List<Emote>();
        public Animator Animator;
        public AudioSource AudioSource;
        public float Cooldown = 0.5f;

        /// <summary>Fired on every client when an emote plays (player, emote id).</summary>
        public static event Action<PhotonVRPlayer, string> EmotePlayed;

        private float nextAllowed;
        private readonly Dictionary<GameObject, Coroutine> hideRoutines = new Dictionary<GameObject, Coroutine>();

        private void Awake()
        {
            foreach (Emote e in Emotes)
                if (e.Visual != null) e.Visual.SetActive(false);
        }

        /// <summary>Plays an emote on the local player's avatar.</summary>
        public static bool PlayLocal(string id)
        {
            foreach (PhotonVRPlayer p in PhotonVRPlayer.All)
                if (p != null && p.IsLocal && p.GetComponent<PhotonVREmotes>() is PhotonVREmotes emotes)
                    return emotes.Play(id);
            return false;
        }

        public bool Play(string id)
        {
            if (!photonView.IsMine || Time.unscaledTime < nextAllowed) return false;
            int index = Emotes.FindIndex(e => e.Id == id);
            if (index < 0 || index > byte.MaxValue) return false;
            nextAllowed = Time.unscaledTime + Cooldown;
            if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_PhotonVREmote), RpcTarget.All, (byte)index);
            else Show(index);
            return true;
        }

        [PunRPC]
        private void RPC_PhotonVREmote(byte index, PhotonMessageInfo info)
        {
            if (!PhotonVRRpc.FromOwner(photonView, info, "emote", 2f, 4f)) return;
            if (index >= Emotes.Count) return;
            Show(index);
        }

        private void Show(int index)
        {
            Emote e = Emotes[index];
            if (e.Visual != null)
            {
                if (hideRoutines.TryGetValue(e.Visual, out Coroutine running) && running != null) StopCoroutine(running);
                e.Visual.SetActive(true);
                hideRoutines[e.Visual] = StartCoroutine(Hide(e.Visual, e.Duration));
            }
            if (e.Sound != null && AudioSource != null) AudioSource.PlayOneShot(e.Sound);
            if (Animator != null && !string.IsNullOrEmpty(e.AnimatorTrigger)) Animator.SetTrigger(e.AnimatorTrigger);
            EmotePlayed?.Invoke(GetComponent<PhotonVRPlayer>(), e.Id);
        }

        private IEnumerator Hide(GameObject visual, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (visual != null) visual.SetActive(false);
        }
    }
}
