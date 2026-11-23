using System;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// A string that is serialized only in encrypted form (see <see cref="PhotonVRIdProtector"/>), used for the
    /// Photon App ID and Voice App ID. The plaintext is never stored in a serialized field: it exists only in memory
    /// while <see cref="Get"/>'s caller uses it. In the inspector it is edited through a masked field.
    /// <para>Obfuscation is defense in depth, not real security - see the README.</para>
    /// </summary>
    [Serializable]
    public class PhotonVRProtectedString
    {
        [SerializeField] private string cipher = "";
        [SerializeField] private string salt = "";

        /// <summary>True when a value is stored.</summary>
        public bool HasValue => !string.IsNullOrEmpty(cipher);

        /// <summary>The encrypted, Base64-encoded value as stored on disk.</summary>
        public string Cipher => cipher;

        /// <summary>Decrypts and returns the value ("" when empty or unreadable). Do not cache the result in serialized fields.</summary>
        public string Get() => HasValue ? PhotonVRIdProtector.Unprotect(cipher, salt) : string.Empty;

        /// <summary>Decrypts into a byte array the caller must zero (null when empty).</summary>
        public byte[] GetBytes() => HasValue ? PhotonVRIdProtector.UnprotectBytes(cipher, salt) : null;

        // Drops the value from this in-memory instance without touching anything on disk.
        // Only used in player builds with memory hardening, where assets are read-only copies anyway.
        internal void ForgetInMemory()
        {
            cipher = string.Empty;
            salt = string.Empty;
        }

        /// <summary>Encrypts and stores <paramref name="value"/> (null or "" clears it). A fresh salt and IV are used every time.</summary>
        public void Set(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                cipher = string.Empty;
                salt = string.Empty;
                return;
            }
            salt = PhotonVRIdProtector.NewSalt();
            cipher = PhotonVRIdProtector.Protect(value.Trim(), salt);
        }

        /// <summary>Removes the stored value.</summary>
        public void Clear() => Set(null);

        /// <summary>Returns a masked preview such as "1a2b••••••••9f0e" (for UIs; reveals 4+4 characters).</summary>
        public string Preview()
        {
            string v = Get();
            if (string.IsNullOrEmpty(v)) return "(not set)";
            return v.Length <= 8 ? new string('•', v.Length) : v.Substring(0, 4) + new string('•', 8) + v.Substring(v.Length - 4);
        }

        /// <summary>Never returns the plaintext (prevents accidental logging).</summary>
        public override string ToString() => HasValue ? "(encrypted)" : "(empty)";
    }
}
