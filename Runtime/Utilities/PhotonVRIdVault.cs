using System;
using System.Text;

namespace Photon.VR
{
    public enum PhotonVRAppIdKind
    {
        Realtime,
        Voice,
        PlayFabTitle
    }

    /// <summary>
    /// In-memory store for the App IDs (and the PlayFab Title ID) when memory hardening is on. Each ID is AES encrypted with a random
    /// key generated for this session and MAC'd so tampering is noticed. Plaintext only exists while
    /// <see cref="Reveal"/>'s caller hands it to Photon.
    /// </summary>
    public sealed class PhotonVRIdVault
    {
        private readonly byte[] sessionKey = PhotonVRIdProtector.RandomBytes(32);
        private readonly byte[] macKey = PhotonVRIdProtector.RandomBytes(32);
        private readonly byte[][] blobs = new byte[3][];
        private readonly byte[][] macs = new byte[3][];

        public bool TamperDetected { get; private set; }

        public bool Has(PhotonVRAppIdKind kind) => blobs[(int)kind] != null;

        /// <summary>Stores the UTF-8 bytes and zeroes the input array.</summary>
        public void Store(PhotonVRAppIdKind kind, byte[] plain)
        {
            int i = (int)kind;
            Wipe(blobs[i]);
            blobs[i] = null;
            macs[i] = null;
            if (plain == null || plain.Length == 0) return;

            try
            {
                blobs[i] = PhotonVRIdProtector.EncryptRaw(plain, sessionKey);
                macs[i] = PhotonVRIdProtector.Mac(blobs[i], macKey);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public void Store(PhotonVRAppIdKind kind, string plain) =>
            Store(kind, string.IsNullOrEmpty(plain) ? null : Encoding.UTF8.GetBytes(plain));

        /// <summary>Decrypts an ID. Keep the returned string only as long as Photon needs it.</summary>
        public string Reveal(PhotonVRAppIdKind kind)
        {
            int i = (int)kind;
            byte[] blob = blobs[i];
            if (blob == null) return string.Empty;

            if (!SameBytes(PhotonVRIdProtector.Mac(blob, macKey), macs[i]))
            {
                TamperDetected = true;
                PhotonVRLog.Error("Stored App ID failed its integrity check - refusing to use it.");
                return string.Empty;
            }

            byte[] plain = null;
            try
            {
                plain = PhotonVRIdProtector.DecryptRaw(blob, sessionKey);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < blobs.Length; i++)
            {
                Wipe(blobs[i]);
                blobs[i] = null;
                macs[i] = null;
            }
        }

        private static void Wipe(byte[] data)
        {
            if (data != null) Array.Clear(data, 0, data.Length);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
