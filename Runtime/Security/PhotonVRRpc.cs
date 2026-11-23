using Photon.Pun;
using Photon.Realtime;

namespace Photon.VR
{
    /// <summary>
    /// Small helpers for sending and validating RPCs. PUN delivers an RPC from anyone to anyone, so check the
    /// sender at the top of every <c>[PunRPC]</c> method that changes state:
    /// <code>
    /// [PunRPC] void RpcSetColor(float r, float g, float b, PhotonMessageInfo info)
    /// {
    ///     if (!PhotonVRRpc.FromOwner(photonView, info, "color")) return;
    ///     ...
    /// }
    /// </code>
    /// The sender in <see cref="PhotonMessageInfo"/> is stamped by the Photon server and cannot be faked.
    /// </summary>
    public static class PhotonVRRpc
    {
        /// <summary>True when the RPC was sent by the view's owner (controller) and is within the rate limit.</summary>
        public static bool FromOwner(PhotonView view, PhotonMessageInfo info, string key, float perSecond = 10f, float burst = 20f)
        {
            Photon.Realtime.Player sender = info.Sender;
            if (view == null || sender == null) return false;
            Photon.Realtime.Player owner = view.Controller ?? view.Owner;
            if (owner != null && owner.ActorNumber != sender.ActorNumber)
            {
                PhotonVRSecurity.Report(sender, PhotonVRViolationType.NotOwner, $"RPC '{key}' on view {view.ViewID}");
                return false;
            }
            return PhotonVRSecurity.CheckRate(sender, "rpc:" + key, perSecond, burst);
        }

        /// <summary>True when the RPC came from the legitimate master client.</summary>
        public static bool FromMaster(PhotonMessageInfo info, string key, float perSecond = 20f, float burst = 40f)
        {
            Photon.Realtime.Player master = PhotonVRMasterClient.LegitimateMaster;
            if (info.Sender == null || master == null || info.Sender.ActorNumber != master.ActorNumber)
            {
                PhotonVRSecurity.Report(info.Sender, PhotonVRViolationType.NotOwner, $"master-only RPC '{key}'");
                return false;
            }
            return PhotonVRSecurity.CheckRate(info.Sender, "rpc:" + key, perSecond, burst);
        }

        /// <summary>Rate limit only (anyone may send).</summary>
        public static bool Rate(PhotonMessageInfo info, string key, float perSecond = 5f, float burst = 10f) =>
            info.Sender != null && PhotonVRSecurity.CheckRate(info.Sender, "rpc:" + key, perSecond, burst);

        public static void ToAll(PhotonView view, string method, params object[] args) => view.RPC(method, RpcTarget.All, args);
        public static void ToOthers(PhotonView view, string method, params object[] args) => view.RPC(method, RpcTarget.Others, args);
        public static void ToMaster(PhotonView view, string method, params object[] args) => view.RPC(method, RpcTarget.MasterClient, args);
        public static void ToPlayer(PhotonView view, Photon.Realtime.Player player, string method, params object[] args) => view.RPC(method, player, args);
        /// <summary>Buffered for late joiners (removed when the sender leaves if CleanupCacheOnLeave is on).</summary>
        public static void ToAllBuffered(PhotonView view, string method, params object[] args) => view.RPC(method, RpcTarget.AllBuffered, args);
    }
}
