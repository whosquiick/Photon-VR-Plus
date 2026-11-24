using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Photon.VR
{
    /// <summary>
    /// Scrambles the App IDs (and the PlayFab Title ID) at rest: an XOR-masked payload with a checksum inside
    /// AES-256-CBC, random IV and salt per value.
    /// <para>From 1.3.0 the key also mixes in a random key made for your project (a small generated script in
    /// <c>Assets/PhotonVR-ProjectKey</c>), so a tool written for one Photon VR Plus game can't read another's IDs.
    /// It's still obfuscation: the game has to hand the real ID to Photon, so anyone patient enough can find it.
    /// Lock your app down in the Photon dashboard as well (see the README).</para>
    /// </summary>
    public static class PhotonVRIdProtector
    {
        /// <summary>Prefix of values from 1.0-1.2 (package key only). Still read, re-saved as v2 in the editor.</summary>
        public const string Prefix = "pvr1:";
        /// <summary>Prefix of the XOR-only fallback used when AES is unavailable on a platform.</summary>
        public const string FallbackPrefix = "pvx1:";
        /// <summary>Prefix of values that also use the project key (1.3.0+).</summary>
        public const string ProjectPrefix = "pvr2:";

        private static readonly int[] PepperA = { 0x51, 0x2f, 0x7a, 0x13, 0x66, 0x3c, 0x08, 0x5e, 0x71, 0x24, 0x39, 0x4b, 0x1d, 0x62, 0x55, 0x0e };
        private static readonly int[] PepperB = { 0x0c, 0x5a, 0x1f, 0x61, 0x33, 0x48, 0x6d, 0x2a, 0x17, 0x59, 0x4e, 0x3b, 0x70, 0x06, 0x21, 0x6f };
        private const int ChecksumLength = 4;
        private const int FingerprintLength = 4;

        // Stored as two halves that are only combined here.
        private static byte[] projectA, projectB;

        /// <summary>True once the project key is loaded (the generated script does this before the first scene).</summary>
        public static bool HasProjectKey => projectA != null;

        /// <summary>Called by the generated PhotonVRProjectKey script. You don't need to call this yourself.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void UseProjectKey(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length < 16) return;
            projectA = (byte[])a.Clone();
            projectB = (byte[])b.Clone();
        }

        public static string NewSalt() => Convert.ToBase64String(RandomBytes(16));

        /// <summary>Encrypts <paramref name="plain"/> with <paramref name="salt"/>. Empty input gives "".</summary>
        public static string Protect(string plain, string salt)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            bool v2 = HasProjectKey;
            byte[] key = DeriveKey(salt, v2);
            byte[] payload = BuildPayload(Encoding.UTF8.GetBytes(plain), key);
            try
            {
                byte[] blob = EncryptRaw(payload, key);
                if (!v2) return Prefix + Convert.ToBase64String(blob);

                byte[] all = new byte[FingerprintLength + blob.Length];
                Buffer.BlockCopy(ProjectFingerprint(), 0, all, 0, FingerprintLength);
                Buffer.BlockCopy(blob, 0, all, FingerprintLength, blob.Length);
                return ProjectPrefix + Convert.ToBase64String(all);
            }
            catch (Exception)
            {
                // Platforms without AES: XOR-masked payload only, with the package key.
                byte[] fallbackKey = DeriveKey(salt, false);
                byte[] masked = BuildPayload(Encoding.UTF8.GetBytes(plain), fallbackKey);
                Array.Clear(fallbackKey, 0, fallbackKey.Length);
                return FallbackPrefix + Convert.ToBase64String(masked);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(payload, 0, payload.Length);
            }
        }

        /// <summary>Decrypts a value from <see cref="Protect"/>. Returns "" for empty or unreadable data.</summary>
        public static string Unprotect(string protectedValue, string salt)
        {
            byte[] plain = UnprotectBytes(protectedValue, salt);
            if (plain == null) return string.Empty;
            string result = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return result;
        }

        /// <summary>Like <see cref="Unprotect"/> but returns the UTF-8 bytes (null when unreadable) so you can zero them.</summary>
        public static byte[] UnprotectBytes(string protectedValue, string salt)
        {
            if (string.IsNullOrEmpty(protectedValue)) return null;
            byte[] key = null, payload = null;
            try
            {
                if (protectedValue.StartsWith(ProjectPrefix, StringComparison.Ordinal))
                {
                    if (!HasProjectKey) return null;
                    byte[] all = Convert.FromBase64String(protectedValue.Substring(ProjectPrefix.Length));
                    if (all.Length < FingerprintLength + 32 || !FingerprintMatches(all)) return null;
                    key = DeriveKey(salt, true);
                    byte[] blob = new byte[all.Length - FingerprintLength];
                    Buffer.BlockCopy(all, FingerprintLength, blob, 0, blob.Length);
                    payload = DecryptRaw(blob, key);
                }
                else if (protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    byte[] blob = Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
                    if (blob.Length < 32) return null;
                    key = DeriveKey(salt, false);
                    payload = DecryptRaw(blob, key);
                }
                else if (protectedValue.StartsWith(FallbackPrefix, StringComparison.Ordinal))
                {
                    key = DeriveKey(salt, false);
                    payload = Convert.FromBase64String(protectedValue.Substring(FallbackPrefix.Length));
                }
                else
                {
                    return null;
                }
                return OpenPayload(payload, key);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (key != null) Array.Clear(key, 0, key.Length);
                if (payload != null) Array.Clear(payload, 0, payload.Length);
            }
        }

        /// <summary>True when the value looks like output of <see cref="Protect"/>.</summary>
        public static bool IsProtected(string value) =>
            !string.IsNullOrEmpty(value) && (value.StartsWith(ProjectPrefix, StringComparison.Ordinal) ||
                                             value.StartsWith(Prefix, StringComparison.Ordinal) ||
                                             value.StartsWith(FallbackPrefix, StringComparison.Ordinal));

        /// <summary>True when the value was saved with an older format and should be re-saved with the project key.</summary>
        public static bool NeedsUpgrade(string value) =>
            HasProjectKey && !string.IsNullOrEmpty(value) && !value.StartsWith(ProjectPrefix, StringComparison.Ordinal);

        /// <summary>True when the value needs a project key that isn't loaded (or a different project's key).</summary>
        public static bool IsFromOtherProject(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(ProjectPrefix, StringComparison.Ordinal)) return false;
            if (!HasProjectKey) return true;
            try { return !FingerprintMatches(Convert.FromBase64String(value.Substring(ProjectPrefix.Length))); }
            catch (FormatException) { return true; }
        }

        internal static byte[] EncryptRaw(byte[] plain, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.GenerateIV();
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                    byte[] all = new byte[16 + cipher.Length];
                    Buffer.BlockCopy(aes.IV, 0, all, 0, 16);
                    Buffer.BlockCopy(cipher, 0, all, 16, cipher.Length);
                    return all;
                }
            }
        }

        internal static byte[] DecryptRaw(byte[] blob, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                byte[] iv = new byte[16];
                Buffer.BlockCopy(blob, 0, iv, 0, 16);
                aes.IV = iv;
                using (ICryptoTransform dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(blob, 16, blob.Length - 16);
            }
        }

        internal static byte[] Mac(byte[] data, byte[] key)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(data);
        }

        internal static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        // payload = XOR(plain || checksum(plain), keystream(key))
        private static byte[] BuildPayload(byte[] plain, byte[] key)
        {
            byte[] sum = Mac(plain, key);
            byte[] data = new byte[plain.Length + ChecksumLength];
            Buffer.BlockCopy(plain, 0, data, 0, plain.Length);
            Buffer.BlockCopy(sum, 0, data, plain.Length, ChecksumLength);
            Xor(data, key);
            Array.Clear(plain, 0, plain.Length);
            return data;
        }

        private static byte[] OpenPayload(byte[] payload, byte[] key)
        {
            if (payload.Length < ChecksumLength) return null;
            Xor(payload, key);
            byte[] plain = new byte[payload.Length - ChecksumLength];
            Buffer.BlockCopy(payload, 0, plain, 0, plain.Length);
            byte[] sum = Mac(plain, key);
            for (int i = 0; i < ChecksumLength; i++)
                if (payload[plain.Length + i] != sum[i])
                {
                    Array.Clear(plain, 0, plain.Length);
                    return null; // wrong key or corrupted
                }
            return plain;
        }

        private static void Xor(byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] ^= (byte)(key[(i * 7 + 3) % key.Length] ^ (i * 31));
        }

        private static byte[] DeriveKey(string salt, bool withProjectKey)
        {
            byte[] saltBytes;
            try { saltBytes = string.IsNullOrEmpty(salt) ? new byte[0] : Convert.FromBase64String(salt); }
            catch (FormatException) { saltBytes = Encoding.UTF8.GetBytes(salt); }

            int projectLength = withProjectKey ? projectA.Length : 0;
            byte[] material = new byte[PepperA.Length + saltBytes.Length + projectLength];
            for (int i = 0; i < PepperA.Length; i++)
                material[i] = (byte)(PepperA[i] ^ PepperB[(i + 5) % PepperB.Length] ^ (i * 13));
            Buffer.BlockCopy(saltBytes, 0, material, PepperA.Length, saltBytes.Length);
            for (int i = 0; i < projectLength; i++)
                material[PepperA.Length + saltBytes.Length + i] = (byte)(projectA[i] ^ projectB[i]);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] key = sha.ComputeHash(material);
                for (int i = 0; i < 1000; i++)
                    key = sha.ComputeHash(key);
                Array.Clear(material, 0, material.Length);
                return key;
            }
        }

        private static byte[] ProjectFingerprint()
        {
            byte[] joined = new byte[projectA.Length];
            for (int i = 0; i < joined.Length; i++) joined[i] = (byte)(projectA[i] ^ projectB[i]);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(joined);
                Array.Clear(joined, 0, joined.Length);
                return hash;
            }
        }

        private static bool FingerprintMatches(byte[] all)
        {
            byte[] fp = ProjectFingerprint();
            for (int i = 0; i < FingerprintLength; i++)
                if (all[i] != fp[i]) return false;
            return true;
        }
    }
}
