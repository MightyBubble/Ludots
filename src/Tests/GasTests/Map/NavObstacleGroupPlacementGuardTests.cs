using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Physics2D.Navigation;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public class NavObstacleGroupPlacementGuardTests
    {
        [Test]
        public void BuildFromMapAuthoring_WithGroupPlacement_ThrowsExplicitBoundary()
        {
            var map = new MapConfig { Id = "map.group" };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "camp",
                Group = "some.group",
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavObstacleAuthoringAdapter.BuildFromMapAuthoring(
                    map,
                    new Dictionary<string, EntityTemplate>(StringComparer.Ordinal),
                    "Ground"))!;

            Assert.That(ex.Message, Does.Contain("group placement 'some.group'"));
        }

        [Test]
        public void BuildFromMapAuthoring_WithTemplateOnly_DoesNotHitGroupGuard()
        {
            var map = new MapConfig { Id = "map.plain" };
            map.Entities.Add(new EntitySpawnData { Template = "plain.template" });

            // No exception: a template-only entry passes the group guard and the valid
            // template builds (no obstacle components → no NavObstacle entries).
            Assert.DoesNotThrow(() =>
                NavObstacleAuthoringAdapter.BuildFromMapAuthoring(
                    map,
                    new Dictionary<string, EntityTemplate>(StringComparer.Ordinal)
                    {
                        ["plain.template"] = new EntityTemplate { Id = "plain.template" },
                    },
                    "Ground"));
        }
    }
}
