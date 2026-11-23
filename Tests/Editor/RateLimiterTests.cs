using NUnit.Framework;

namespace Photon.VR.Tests
{
    public class RateLimiterTests
    {
        private float now;

        [SetUp]
        public void SetUp()
        {
            now = 0f;
            PhotonVRRateLimiter.Clock = () => now;
        }

        [TearDown]
        public void TearDown() => PhotonVRRateLimiter.Clock = null;

        [Test]
        public void AllowsBurstThenRefills()
        {
            var limiter = new PhotonVRRateLimiter(2f, 3f);
            Assert.IsTrue(limiter.TryConsume());
            Assert.IsTrue(limiter.TryConsume());
            Assert.IsTrue(limiter.TryConsume());
            Assert.IsFalse(limiter.TryConsume());
            now = 0.5f;
            Assert.IsTrue(limiter.TryConsume());
            Assert.IsFalse(limiter.TryConsume());
        }

        [Test]
        public void TableTracksActorsSeparately()
        {
            var table = new PhotonVRRateLimiterTable();
            Assert.IsTrue(table.TryConsume(1, "rpc", 1f, 1f));
            Assert.IsFalse(table.TryConsume(1, "rpc", 1f, 1f));
            Assert.IsTrue(table.TryConsume(2, "rpc", 1f, 1f));
            Assert.IsTrue(table.TryConsume(1, "other", 1f, 1f));
            table.Forget(1);
            Assert.IsTrue(table.TryConsume(1, "rpc", 1f, 1f));
        }
    }
}
