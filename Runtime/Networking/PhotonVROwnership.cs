using System;
using System.Collections;

using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>PhotonView ownership helpers.</summary>
    public static class PhotonVROwnership
    {
        public static bool IsMine(PhotonView view) => view != null && view.IsMine;

        /// <summary>Asks the current owner for the view (needs OwnershipOption Request or Takeover on the view).</summary>
        public static void Request(PhotonView view)
        {
            if (view != null && !view.IsMine) view.RequestOwnership();
        }

        /// <summary>Gives the view to <paramref name="player"/>. Allowed for the owner and the legitimate master.</summary>
        public static bool TransferTo(PhotonView view, Photon.Realtime.Player player)
        {
            if (view == null || player == null) return false;
            if (!view.IsMine && !PhotonVRMasterClient.IsLocalLegitimateMaster)
            {
                PhotonVRLog.Warn($"Cannot transfer view {view.ViewID}: you neither own it nor are the master.");
                return false;
            }
            view.TransferOwnership(player);
            return true;
        }

        /// <summary>Gives the view to the legitimate master (e.g. before you leave, so objects keep simulating).</summary>
        public static bool TransferToMaster(PhotonView view) => TransferTo(view, PhotonVRMasterClient.LegitimateMaster);

        /// <summary>Requests ownership and reports whether it arrived within <paramref name="timeout"/> seconds.</summary>
        public static IEnumerator RequestAndWait(PhotonView view, float timeout, Action<bool> done)
        {
            Request(view);
            float end = Time.realtimeSinceStartup + timeout;
            while (view != null && !view.IsMine && Time.realtimeSinceStartup < end)
                yield return null;
            done?.Invoke(view != null && view.IsMine);
        }
    }
}
