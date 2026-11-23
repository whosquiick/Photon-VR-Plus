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
    }
}
