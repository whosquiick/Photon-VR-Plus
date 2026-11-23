using Photon.Realtime;

namespace Photon.VR
{
    /// <summary>
    /// Optional source of truth for security decisions, e.g. a PlayFab CloudScript / your own backend that knows
    /// who is allowed to host, who is banned and which UserId belongs to which account.
    /// Assign an instance to <see cref="PhotonVRSecurity.Authority"/>. Every method has a safe default.
    /// </summary>
    public abstract class PhotonVRAuthority
    {
        /// <summary>Called when the master client changed without a handoff. Return true/false to decide, null to use the built-in rules.</summary>
        public virtual bool? ValidateMasterClient(Photon.Realtime.Player newMaster, Photon.Realtime.Player previousMaster) => null;

        /// <summary>Called on the master when a player joins. Return false to kick them (e.g. their auth ticket did not check out).</summary>
        public virtual bool ValidateIdentity(Photon.Realtime.Player player) => true;

        /// <summary>Called on the kicked client before it obeys a kick from the master.</summary>
        public virtual bool ValidateKick(Photon.Realtime.Player master, Photon.Realtime.Player target, string reason) => true;

        /// <summary>Called for every detected violation. Forward to your backend (report / ban) here.</summary>
        public virtual void OnViolation(PhotonVRViolation violation) { }
    }
}
