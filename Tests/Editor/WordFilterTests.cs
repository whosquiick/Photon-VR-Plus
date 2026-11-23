using NUnit.Framework;

namespace Photon.VR.Tests
{
    public class WordFilterTests
    {
        private static PhotonVRWordFilter Filter => PhotonVRWordFilter.Default;

        [Test]
        public void BlocksPlainAndDisguisedSlurs()
        {
            Assert.IsTrue(Filter.IsBlocked("nigger"));
            Assert.IsTrue(Filter.IsBlocked("N1GG3R"));
            Assert.IsTrue(Filter.IsBlocked("niiiggga"));
            Assert.IsTrue(Filter.IsBlocked("f a g g o t"));
            Assert.IsTrue(Filter.IsBlocked("fággot"));
        }

        [Test]
        public void BlocksSwearsWithLeetspeak()
        {
            Assert.IsTrue(Filter.IsBlocked("b!tch"));
            Assert.IsTrue(Filter.IsBlocked("fuuuuck"));
            Assert.IsTrue(Filter.IsBlocked("$h1t"));
            Assert.IsTrue(Filter.IsBlocked("xXfuckXx"));
        }

        [Test]
        public void BlocksExtremistNumbers()
        {
            Assert.IsTrue(Filter.IsBlocked("1488"));
        }

        [Test]
        public void AllowsInnocentWords()
        {
            foreach (string ok in new[] { "assassin", "classic", "Scunthorpe", "raccoon", "night", "grape", "cockpit", "therapist", "Nigeria", "Montenegro", "Player123", "Isaac" })
                Assert.IsFalse(Filter.IsBlocked(ok), ok);
        }

        [Test]
        public void RoomCodes()
        {
            Assert.IsTrue(Filter.IsRoomCodeAllowed("K7MQ2X"));
            Assert.IsTrue(Filter.IsRoomCodeAllowed("123456"));
            Assert.IsFalse(Filter.IsRoomCodeAllowed("FUCK12"));
            Assert.IsFalse(Filter.IsRoomCodeAllowed("bad code"));
            Assert.IsFalse(Filter.IsRoomCodeAllowed(""));
            Assert.IsFalse(Filter.IsRoomCodeAllowed(new string('A', 40)));
        }

        [Test]
        public void SanitizeNameFallsBack()
        {
            Assert.AreEqual("Player7", Filter.SanitizeName("shithead", 7));
            Assert.AreEqual("Player3", Filter.SanitizeName("", 3));
            Assert.AreEqual("bold", Filter.SanitizeName("<b>bold</b>", 1));
            Assert.AreEqual("big", Filter.SanitizeName("<size=99>big", 1));
        }

        [Test]
        public void CustomListSyntax()
        {
            var f = new PhotonVRWordFilter(new[] { "# comment", "=ass", "raw:69", "zorp" }, new[] { "zorpline" });
            Assert.IsTrue(f.IsBlocked("ass"));
            Assert.IsFalse(f.IsBlocked("grass"));
            Assert.IsTrue(f.IsBlocked("69"));
            Assert.IsTrue(f.IsBlocked("z0rrp"));
            Assert.IsFalse(f.IsBlocked("zorpline"));
        }
    }
}
