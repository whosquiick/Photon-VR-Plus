using System.Runtime.CompilerServices;

// The Editor assembly fills in the edit-mode hooks (PhotonVREditMode) and uses the shared find helpers.
[assembly: InternalsVisibleTo("PhotonVR.Editor")]
// The optional PlayFab adapter drops the at-rest Title ID copy in player builds (PhotonVRProtectedString.ForgetInMemory).
// Without this, PHOTONVR_PLAYFAB projects compiled in the editor but failed when building a player.
[assembly: InternalsVisibleTo("PhotonVR.PlayFab")]
