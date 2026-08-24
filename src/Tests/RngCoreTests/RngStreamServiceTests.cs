using System.Linq;
using Ludots.Core.Engine.Randomization;
using NUnit.Framework;

namespace RngCoreTests
{
    [TestFixture]
    [Category("ci-gate")]
    public class RngStreamServiceTests
    {
        [Test]
        public void RngStreamService_GetStream_UndeclaredStream_Throws()
        {
            var service = new RngStreamService();

            Assert.That(
                () => service.GetStream("luck"),
                Throws.InvalidOperationException.With.Message.Contains("luck").And.Message.Contains("not declared"));
        }

        [Test]
        public void RngStreamService_DeclareStream_DuplicateId_Throws()
        {
            var service = new RngStreamService();
            service.DeclareStream("luck", 1u);

            Assert.That(
                () => service.DeclareStream("luck", 2u),
                Throws.InvalidOperationException.With.Message.Contains("already declared"));
        }

        [Test]
        public void RngStreamService_DeclareStream_WhitespaceId_Throws()
        {
            var service = new RngStreamService();

            Assert.That(() => service.DeclareStream("", 1u), Throws.ArgumentException);
            Assert.That(() => service.DeclareStream("   ", 1u), Throws.ArgumentException);
        }

        [Test]
        public void RngStreamService_DeclaredStreamIds_ListsAllDeclarations()
        {
            var service = new RngStreamService();
            service.DeclareStream("luck", 1u);
            service.DeclareStream("combat", 2u);

            Assert.That(service.DeclaredStreamIds.OrderBy(id => id), Is.EqualTo(new[] { "combat", "luck" }));
        }

        [Test]
        public void RngStreamService_GetStream_DeclaredStream_ReturnsLiveInstance()
        {
            var service = new RngStreamService();
            service.DeclareStream("luck", 7u);

            var first = service.GetStream("luck");
            var second = service.GetStream("luck");
            first.NextUInt();

            Assert.That(second.Position, Is.EqualTo(1), "GetStream must return the live stream, not a fresh copy.");
        }
    }

    [TestFixture]
    public class RngSeedTests
    {
        [Test]
        public void RngSeed_Begin_ZeroSeed_FallsBackToOffsetBasis()
        {
            Assert.That(RngSeed.Begin(0u), Is.EqualTo(2166136261u));
            Assert.That(RngSeed.Begin(5u), Is.EqualTo(5u));
        }

        [Test]
        public void RngSeed_Finalize_ZeroHash_EscapesToOne()
        {
            Assert.That(RngSeed.Finalize(0u), Is.EqualTo(1u));
            Assert.That(RngSeed.Finalize(7u), Is.EqualTo(7u));
        }

        [Test]
        public void RngSeed_MixChain_SameInputs_ProduceSameHash()
        {
            var first = RngSeed.Finalize(RngSeed.Mix(RngSeed.Mix(RngSeed.Begin(3u), 11), -7));
            var second = RngSeed.Finalize(RngSeed.Mix(RngSeed.Mix(RngSeed.Begin(3u), 11), -7));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(0u));
        }
    }
}
