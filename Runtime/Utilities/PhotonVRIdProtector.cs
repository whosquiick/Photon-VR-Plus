using System;
using System.Security.Cryptography;
using System.Text;

namespace Photon.VR
{
    /// <summary>
    /// Obfuscates Photon App IDs at rest (AES-256-CBC over an XOR-masked payload, Base64 encoded, random IV,
    /// per-value random salt, key derived at runtime). This is <b>defense in depth only</b>: the client must use the
    /// real ID at runtime, so a determined attacker with a debugger, memory dumper or decompiler can still recover it.
    /// Use Photon's server-side protections (custom authentication, app version / bundle restrictions, CCU limits) as
    /// the real fix - see the README section "Protecting your App IDs".
    /// </summary>
    public static class PhotonVRIdProtector
    {
        /// <summary>Prefix of values produced by <see cref="Protect"/> (AES).</summary>
        public const string Prefix = "pvr1:";
        /// <summary>Prefix of the XOR-only fallback used when AES is unavailable on a platform.</summary>
        public const string FallbackPrefix = "pvx1:";

        // Key material is assembled at runtime so no readable key string sits in the binary's metadata.
        private static readonly int[] PepperA = { 0x51, 0x2f, 0x7a, 0x13, 0x66, 0x3c, 0x08, 0x5e, 0x71, 0x24, 0x39, 0x4b, 0x1d, 0x62, 0x55, 0x0e };
        private static readonly int[] PepperB = { 0x0c, 0x5a, 0x1f, 0x61, 0x33, 0x48, 0x6d, 0x2a, 0x17, 0x59, 0x4e, 0x3b, 0x70, 0x06, 0x21, 0x6f };
        private const int ChecksumLength = 4;

        /// <summary>Creates a new random 16-byte salt (Base64).</summary>
        public static string NewSalt()
        {
            return Convert.ToBase64String(RandomBytes(16));
        }

        /// <summary>Encrypts <paramref name="plain"/> with <paramref name="salt"/>. Empty input gives "".</summary>
        public static string Protect(string plain, string salt)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            byte[] key = DeriveKey(salt);
            byte[] payload = BuildPayload(Encoding.UTF8.GetBytes(plain), key);
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.GenerateIV();
                    using (ICryptoTransform enc = aes.CreateEncryptor())
                    {
                        byte[] cipher = enc.TransformFinalBlock(payload, 0, payload.Length);
                        byte[] all = new byte[aes.IV.Length + cipher.Length];
                        Buffer.BlockCopy(aes.IV, 0, all, 0, aes.IV.Length);
                        Buffer.BlockCopy(cipher, 0, all, aes.IV.Length, cipher.Length);
                        return Prefix + Convert.ToBase64String(all);
                    }
                }
            }
            catch (Exception)
            {
                // Platforms without AES: XOR-masked payload only.
                return FallbackPrefix + Convert.ToBase64String(payload);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>Decrypts a value produced by <see cref="Protect"/>. Returns "" for empty or invalid data.</summary>
        public static string Unprotect(string protectedValue, string salt)
        {
            byte[] plain = UnprotectBytes(protectedValue, salt);
            if (plain == null) return string.Empty;
            string result = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return result;
        }

        /// <summary>Like <see cref="Unprotect"/> but returns the UTF-8 bytes (null when invalid) so the caller can zero them.</summary>
        public static byte[] UnprotectBytes(string protectedValue, string salt)
        {
            if (string.IsNullOrEmpty(protectedValue)) return null;
            byte[] key = DeriveKey(salt);
            try
            {
                byte[] payload;
                if (protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    byte[] all = Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
                    if (all.Length < 32) return null;
                    using (Aes aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = key;
                        byte[] iv = new byte[16];
                        Buffer.BlockCopy(all, 0, iv, 0, 16);
                        aes.IV = iv;
                        using (ICryptoTransform dec = aes.CreateDecryptor())
                            payload = dec.TransformFinalBlock(all, 16, all.Length - 16);
                    }
                }
                else if (protectedValue.StartsWith(FallbackPrefix, StringComparison.Ordinal))
                {
                    payload = Convert.FromBase64String(protectedValue.Substring(FallbackPrefix.Length));
                }
                else
                {
                    return null;
                }

                byte[] plain = OpenPayload(payload, key);
                Array.Clear(payload, 0, payload.Length);
                return plain;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        // AES-256-CBC with a caller supplied 32 byte key. Output: IV || ciphertext.
        internal static byte[] EncryptRaw(byte[] plain, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
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

        /// <summary>True when the value looks like output of <see cref="Protect"/>.</summary>
        public static bool IsProtected(string value) =>
            !string.IsNullOrEmpty(value) && (value.StartsWith(Prefix, StringComparison.Ordinal) || value.StartsWith(FallbackPrefix, StringComparison.Ordinal));

        // payload = XOR(plain || checksum(plain), keystream(key))
        private static byte[] BuildPayload(byte[] plain, byte[] key)
        {
            byte[] sum = Checksum(plain, key);
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
            byte[] sum = Checksum(plain, key);
            for (int i = 0; i < ChecksumLength; i++)
                if (payload[plain.Length + i] != sum[i])
                    return null; // wrong key / corrupted
            return plain;
        }

        private static byte[] Checksum(byte[] data, byte[] key)
        {
            return Mac(data, key);
        }

        private static void Xor(byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] ^= (byte)(key[(i * 7 + 3) % key.Length] ^ (i * 31));
        }

        private static byte[] DeriveKey(string salt)
        {
            byte[] saltBytes;
            try { saltBytes = string.IsNullOrEmpty(salt) ? new byte[0] : Convert.FromBase64String(salt); }
            catch (FormatException) { saltBytes = Encoding.UTF8.GetBytes(salt); }

            byte[] material = new byte[PepperA.Length + saltBytes.Length];
            for (int i = 0; i < PepperA.Length; i++)
                material[i] = (byte)(PepperA[i] ^ PepperB[(i + 5) % PepperB.Length] ^ (i * 13));
            Buffer.BlockCopy(saltBytes, 0, material, PepperA.Length, saltBytes.Length);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] key = sha.ComputeHash(material);
                // a few extra rounds so the key is not a single hash of visible bytes
                for (int i = 0; i < 1000; i++)
                    key = sha.ComputeHash(key);
                Array.Clear(material, 0, material.Length);
                return key;
            }
        }
    }
}
