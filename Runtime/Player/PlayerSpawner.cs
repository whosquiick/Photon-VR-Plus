// Photon VR Plus - a revamp of PhotonVR by fchb1239 (MIT). See LICENSE.md.
using System;
using UnityEngine;

using Photon.Pun;

namespace Photon.VR.Player
{
    /// <summary>
    /// Spawns the networked player prefab (Resources/<see cref="PrefabLocation"/>) when you join a room
    /// and removes it when you leave.
    /// </summary>
    public class PlayerSpawner : MonoBehaviourPunCallbacks
    {
        [Tooltip("The location of the player prefab (inside a Resources folder, without extension)")]
        public string PrefabLocation = "PhotonVR/Player";

        [Tooltip("Optional spawn point. The player model follows your rig anyway; this only sets the root transform.")]
        public Transform SpawnPoint;

        /// <summary>Raised after the local player object was instantiated.</summary>
        public event Action<GameObject> PlayerSpawned;

        /// <summary>The spawned local player object (null when not in a room).</summary>
        public GameObject SpawnedPlayer => playerTemp;

        private GameObject playerTemp;

        private void Awake() => DontDestroyOnLoad(gameObject);

        public override void OnJoinedRoom()
        {
            if (playerTemp != null)
                return; // already spawned (e.g. duplicate callback)

            if (Resources.Load<GameObject>(PrefabLocation) == null)
            {
                Debug.LogError($"[PhotonVR] PlayerSpawner: no prefab at Resources/{PrefabLocation}. Use Tools > PhotonVR > Create Player Prefab.");
                return;
            }

            Vector3 pos = SpawnPoint != null ? SpawnPoint.position : Vector3.zero;
            Quaternion rot = SpawnPoint != null ? SpawnPoint.rotation : Quaternion.identity;
            playerTemp = PhotonNetwork.Instantiate(PrefabLocation, pos, rot);
            PlayerSpawned?.Invoke(playerTemp);
        }

        public override void OnLeftRoom()
        {
            // PUN already cleans up our networked objects when we leave; only destroy what is left locally.
            if (playerTemp != null)
                Destroy(playerTemp);
            playerTemp = null;
        }
    }
}
