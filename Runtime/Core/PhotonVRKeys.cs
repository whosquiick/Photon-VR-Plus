namespace Photon.VR
{
    /// <summary>
    /// Photon custom property, PlayerPrefs and Resources keys. All values are identical to the
    /// original PhotonVR so data stays compatible.
    /// </summary>
    public static class PhotonVRKeys
    {
        /// <summary>Player custom property: colour as <c>JsonUtility</c> JSON string.</summary>
        public const string Colour = "Colour";
        /// <summary>Player custom property: <c>Dictionary&lt;string,string&gt;</c> of slot name to cosmetic ID.</summary>
        public const string Cosmetics = "Cosmetics";

        /// <summary>Player custom property: slot name to tint ("#RRGGBBAA"). New in 1.1.0.</summary>
        public const string CosmeticTints = "CosmeticTints";
        /// <summary>Player custom property: true for spectators.</summary>
        public const string Spectator = "pvr_spectator";
        /// <summary>Player custom property: published ping (for master handoff).</summary>
        public const string Ping = "pvr_ping";
        /// <summary>Room custom property: true when the room was created as private / join-by-code.</summary>
        public const string PrivateRoom = "pvr_private";

        /// <summary>Room custom property (lobby-visible): matchmaking queue.</summary>
        public const string Queue = "queue";
        /// <summary>Room custom property (lobby-visible): <c>Application.version</c>.</summary>
        public const string Version = "version";

        /// <summary>PlayerPrefs key of the nickname.</summary>
        public const string PrefsUsername = "Username";
        /// <summary>PlayerPrefs key of the colour JSON.</summary>
        public const string PrefsColour = "Colour";
        /// <summary>PlayerPrefs location of the cosmetics dictionary (see <see cref="Saving.PhotonVRValueSaver"/>).</summary>
        public const string PrefsCosmetics = "Cosmetics";

        /// <summary>PlayerPrefs location of the cosmetic tints dictionary.</summary>
        public const string PrefsCosmeticTints = "CosmeticTints";
        /// <summary>PlayerPrefs key of the local mute list.</summary>
        public const string PrefsMuted = "PhotonVRMuted";

        /// <summary>Default Resources path of the networked player prefab.</summary>
        public const string DefaultPlayerPrefab = "PhotonVR/Player";

        /// <summary>RPC name used to ask every client to re-apply a player's values.</summary>
        public const string RefreshRpc = "RPCRefreshPlayerValues";
    }
}
