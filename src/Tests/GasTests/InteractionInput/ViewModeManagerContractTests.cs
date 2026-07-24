using Arch.Core;
using CoreInputMod.ViewMode;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    [TestFixture]
    public sealed class ViewModeManagerContractTests
    {
        [Test]
        public void Register_RequiresExplicitInteractionMode()
        {
            using var world = World.Create();
            var manager = new ViewModeManager(world, new Dictionary<string, object>());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                manager.Register(new ViewModeConfig
                {
                    Id = "player",
                    VirtualCameraId = ""
                }));

            Assert.That(ex!.Message, Does.Contain("InteractionMode"));
        }

        [Test]
        public void Register_RejectsUnsupportedInteractionMode()
        {
            using var world = World.Create();
            var manager = new ViewModeManager(world, new Dictionary<string, object>());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                manager.Register(new ViewModeConfig
                {
                    Id = "player",
                    VirtualCameraId = "",
                    InteractionMode = "MaybeCast"
                }));

            Assert.That(ex!.Message, Does.Contain("MaybeCast"));
        }

        [Test]
        public void SwitchTo_AppliesExplicitInteractionModeWithoutFallback()
        {
            using var world = World.Create();
            var manager = new ViewModeManager(world, new Dictionary<string, object>());
            manager.Register(new ViewModeConfig
            {
                Id = "player",
                VirtualCameraId = "",
                InteractionMode = "SmartCast"
            });

            Assert.That(manager.SwitchTo("player"), Is.True);
            Assert.That(manager.ActiveMode?.Id, Is.EqualTo("player"));
        }
    }
}
