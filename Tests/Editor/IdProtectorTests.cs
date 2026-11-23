using NUnit.Framework;

namespace Photon.VR.Tests
{
    public class IdProtectorTests
    {
        private const string Id = "0b3c2f7e-1234-4a5b-9c8d-abcdefabcdef";

        [Test]
        public void RoundTripsAndHidesPlaintext()
        {
            string salt = PhotonVRIdProtector.NewSalt();
            string stored = PhotonVRIdProtector.Protect(Id, salt);
            Assert.IsFalse(stored.Contains(Id));
            Assert.AreEqual(Id, PhotonVRIdProtector.Unprotect(stored, salt));
            Assert.AreEqual("", PhotonVRIdProtector.Unprotect(stored, PhotonVRIdProtector.NewSalt()));
        }

        [Test]
        public void VaultKeepsEachKindSeparate()
        {
            var vault = new PhotonVRIdVault();
            vault.Store(PhotonVRAppIdKind.Realtime, Id);
            vault.Store(PhotonVRAppIdKind.PlayFabTitle, "A1B2C");
            Assert.AreEqual(Id, vault.Reveal(PhotonVRAppIdKind.Realtime));
            Assert.AreEqual("A1B2C", vault.Reveal(PhotonVRAppIdKind.PlayFabTitle));
            Assert.AreEqual("", vault.Reveal(PhotonVRAppIdKind.Voice));
            vault.Clear();
            Assert.IsFalse(vault.Has(PhotonVRAppIdKind.PlayFabTitle));
        }
    }
}
