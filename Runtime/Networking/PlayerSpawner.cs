using System;
using UnityEngine;

using Photon.Pun;

namespace Photon.VR.Player
{
    /// <summary>
    /// Spawns the networked player prefab (<c>Resources/</c><see cref="PrefabLocation"/>) for the local player when
    /// you join a room, and removes it when you leave. Works standalone or as a child of the PhotonVRManager prefab.
    /// </summary>
    [AddComponentMenu("PhotonVR/PhotonVR Player Spawner")]
    public class PlayerSpawner : MonoBehaviourPunCallbacks
    {
        [Tooltip("The location of the player prefab (inside a Resources folder, without extension)")]
        public string PrefabLocation = PhotonVRKeys.DefaultPlayerPrefab;

        [Tooltip("Optional spawn point. The model follows your rig anyway; this only sets the root transform.")]
        public Transform SpawnPoint;

        [Tooltip("Spawn automatically in OnJoinedRoom. Turn off to call Spawn() yourself (e.g. after a loading screen).")]
        public bool SpawnOnJoin = true;

        /// <summary>Raised after the local player object was instantiated.</summary>
        public event Action<GameObject> PlayerSpawned;

        /// <summary>The spawned local player object (null when not in a room).</summary>
        public GameObject SpawnedPlayer => playerTemp;

        private GameObject playerTemp;

        private void Awake() => DontDestroyOnLoad(transform.root.gameObject);

        /// <summary>Instantiates the player prefab over the network now (no-op when already spawned or not in a room).</summary>
        public GameObject Spawn()
        {
            if (playerTemp != null || !PhotonNetwork.InRoom)
                return playerTemp;

            if (Resources.Load<GameObject>(PrefabLocation) == null)
            {
                PhotonVRLog.Error($"PlayerSpawner: no prefab at Resources/{PrefabLocation}. Use Tools > PhotonVR > Create Player Prefab.", this);
                return null;
            }

            Vector3 pos = SpawnPoint != null ? SpawnPoint.position : Vector3.zero;
            Quaternion rot = SpawnPoint != null ? SpawnPoint.rotation : Quaternion.identity;
            playerTemp = PhotonNetwork.Instantiate(PrefabLocation, pos, rot);
            PlayerSpawned?.Invoke(playerTemp);
            return playerTemp;
        }

        /// <summary>Destroys the local player object (network-wide when still in a room).</summary>
        public void Despawn()
        {
            if (playerTemp == null) return;
            if (PhotonNetwork.InRoom && playerTemp.GetComponent<PhotonView>() is PhotonView view && view.IsMine)
                PhotonNetwork.Destroy(playerTemp);
            else
                Destroy(playerTemp);
            playerTemp = null;
        }

        public override void OnJoinedRoom()
        {
            if (SpawnOnJoin)
                Spawn();
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
