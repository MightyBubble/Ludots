using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SpawnedUnitRuntimeSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            UnitTypeRegistry.Clear();
        }

        [Test]
        public void SpawnedUnitRuntimeSystem_CreatesNamedUnit_FromUnitType()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var system = new SpawnedUnitRuntimeSystem(world, requests);

            int wolfType = UnitTypeRegistry.Register("Unit.Wolf");
            var spawner = world.Create(
                new Team { Id = 1 },
                new WorldPositionCm { Value = Fix64Vec2.FromInt(300, 500) });

            world.Create(new SpawnedUnitState
            {
                UnitTypeId = wolfType,
                OffsetRadius = 0,
                OnSpawnEffectTemplateId = 0,
                Spawner = spawner
            });

            system.Update(0f);

            bool foundNamedUnit = false;
            var q = new QueryDescription().WithAll<Name, Team, WorldPositionCm>();
            world.Query(in q, (Entity e, ref Name name, ref Team team, ref WorldPositionCm pos) =>
            {
                if (name.Value == "Unit:Unit.Wolf")
                {
                    foundNamedUnit = true;
                    That(team.Id, Is.EqualTo(1));
                    That(pos.Value.X.ToFloat(), Is.EqualTo(300f).Within(0.1f));
                    That(pos.Value.Y.ToFloat(), Is.EqualTo(500f).Within(0.1f));
                }
            });

            That(foundNamedUnit, Is.True, "Spawned unit should include Name component with UnitType prefix");
        }

        [Test]
        public void SpawnedUnitRuntimeSystem_UnknownUnitType_UsesUnknownName()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var system = new SpawnedUnitRuntimeSystem(world, requests);

            var spawner = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            world.Create(new SpawnedUnitState
            {
                UnitTypeId = 999,
                OffsetRadius = 0,
                OnSpawnEffectTemplateId = 0,
                Spawner = spawner
            });

            system.Update(0f);

            bool foundUnknown = false;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (ref Name name) =>
            {
                if (name.Value == "Unit:Unknown")
                    foundUnknown = true;
            });

            That(foundUnknown, Is.True);
        }
    }
}
