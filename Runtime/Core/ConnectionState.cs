namespace Photon.VR
{
    /// <summary>
    /// High-level connection state reported by <see cref="PhotonVRManager.GetConnectionState"/>.
    /// Same members and order as the original PhotonVR enum.
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>Not connected to Photon.</summary>
        Disconnected,
        /// <summary>Connecting (or reconnecting) to the master server.</summary>
        Connecting,
        /// <summary>Connected to the master server, not in a room.</summary>
        Connected,
        /// <summary>Matchmaking / joining a room.</summary>
        JoiningRoom,
        /// <summary>Inside a room.</summary>
        InRoom
    }
}
