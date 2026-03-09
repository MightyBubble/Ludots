using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using RtsDemoMod.Components;

namespace RtsDemoMod.Systems
{
    internal static class RtsUnitRuntimeSetup
    {
        private static readonly int MoveSpeedAttr = AttributeRegistry.Register("MoveSpeedCmPerSec");
        private static readonly int RadiusAttr = AttributeRegistry.Register("NavRadiusCm");
        private static readonly int MaxAccelAttr = AttributeRegistry.Register("NavMaxAccelCmPerSec2");
        private static readonly int NeighborDistAttr = AttributeRegistry.Register("NavNeighborDistCm");
        private static readonly int TimeHorizonAttr = AttributeRegistry.Register("NavTimeHorizonSec");
        private static readonly int MaxNeighborsAttr = AttributeRegistry.Register("NavMaxNeighbors");
        private static readonly int StaticAttr = AttributeRegistry.Register("NavStatic");
        private static readonly int VisualDiameterAttr = AttributeRegistry.Register("RtsVisualDiameterCm");
        private static readonly int VisualHeightAttr = AttributeRegistry.Register("RtsVisualHeightCm");

        internal readonly struct RtsVisualSpec
        {
            public RtsVisualSpec(float diameterCm, float heightCm)
            {
                DiameterCm = diameterCm;
                HeightCm = heightCm;
            }

            public float DiameterCm { get; }
            public float HeightCm { get; }
        }

        public static Entity EnsureController(World world, Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(Ludots.Core.Scripting.CoreServiceKeys.LocalPlayerEntity.Name, out var obj) && obj is Entity existing && world.IsAlive(existing) && world.Has<RtsControllerTag>(existing))
            {
                return existing;
            }

            var controller = world.Create(
                new Name { Value = "RTS Controller" },
                new PlayerOwner { PlayerId = 1 },
                new GameplayTagContainer(),
                new SelectionBuffer(),
                new SelectionGroupBuffer(),
                new RtsControllerTag());
            globals[Ludots.Core.Scripting.CoreServiceKeys.LocalPlayerEntity.Name] = controller;
            return controller;
        }

        public static void EnsureRuntimeComponents(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                return;
            }

            if (!world.Has<PlayerOwner>(entity) && world.TryGet(entity, out Team team))
            {
                world.Add(entity, new PlayerOwner { PlayerId = team.Id });
            }

            var pos = world.Get<WorldPositionCm>(entity).Value;
            if (!world.Has<Position2D>(entity))
            {
                world.Add(entity, new Position2D { Value = pos });
            }
            else
            {
                var position = world.Get<Position2D>(entity);
                position.Value = pos;
                world.Set(entity, position);
            }

            if (!world.Has<PreviousPosition2D>(entity))
            {
                world.Add(entity, new PreviousPosition2D { Value = pos });
            }

            if (!world.Has<Velocity2D>(entity))
            {
                world.Add(entity, Velocity2D.Zero);
            }

            bool isStatic = false;
            float moveSpeed = 0f;
            float radius = 40f;
            float maxAccel = 1800f;
            float neighborDist = 240f;
            float timeHorizon = 2f;
            int maxNeighbors = 10;

            if (world.TryGet(entity, out AttributeBuffer attrs))
            {
                moveSpeed = attrs.GetCurrent(MoveSpeedAttr);
                radius = ReadPositive(attrs.GetCurrent(RadiusAttr), radius);
                maxAccel = ReadPositive(attrs.GetCurrent(MaxAccelAttr), maxAccel);
                neighborDist = ReadPositive(attrs.GetCurrent(NeighborDistAttr), Math.Max(radius * 6f, neighborDist));
                timeHorizon = ReadPositive(attrs.GetCurrent(TimeHorizonAttr), timeHorizon);
                maxNeighbors = Math.Max(0, (int)MathF.Round(ReadPositive(attrs.GetCurrent(MaxNeighborsAttr), maxNeighbors)));
                isStatic = attrs.GetCurrent(StaticAttr) > 0.5f || moveSpeed <= 0.01f;
            }

            if (!world.Has<Mass2D>(entity))
            {
                world.Add(entity, isStatic ? Mass2D.Static : Mass2D.FromFloat(1f, 1f));
            }
            else if (isStatic)
            {
                world.Set(entity, Mass2D.Static);
            }

            if (!world.Has<NavAgent2D>(entity))
            {
                world.Add(entity, new NavAgent2D());
            }

            if (!world.Has<NavGoal2D>(entity))
            {
                world.Add(entity, new NavGoal2D());
            }

            var kinematics = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromFloat(moveSpeed <= 0f ? 1f : moveSpeed),
                MaxAccelCmPerSec2 = Fix64.FromFloat(maxAccel),
                RadiusCm = Fix64.FromFloat(radius),
                NeighborDistCm = Fix64.FromFloat(neighborDist),
                TimeHorizonSec = Fix64.FromFloat(timeHorizon),
                MaxNeighbors = maxNeighbors
            };

            if (!world.Has<NavKinematics2D>(entity))
            {
                world.Add(entity, kinematics);
            }
            else
            {
                world.Set(entity, kinematics);
            }
        }

        public static float GetRadiusCm(World world, Entity entity, float fallback = 40f)
        {
            if (world.TryGet(entity, out NavKinematics2D kinematics))
            {
                return kinematics.RadiusCm.ToFloat();
            }

            if (world.TryGet(entity, out AttributeBuffer attrs))
            {
                return ReadPositive(attrs.GetCurrent(RadiusAttr), fallback);
            }

            return fallback;
        }

        public static RtsVisualSpec GetVisualSpec(World world, Entity entity)
        {
            float radiusCm = GetRadiusCm(world, entity);
            float defaultDiameterCm = Math.Max(radiusCm * 2f, 20f);
            float diameterCm = defaultDiameterCm;
            float heightCm = defaultDiameterCm;

            if (world.TryGet(entity, out AttributeBuffer attrs))
            {
                diameterCm = ReadPositive(attrs.GetCurrent(VisualDiameterAttr), defaultDiameterCm);
                heightCm = ReadPositive(attrs.GetCurrent(VisualHeightAttr), diameterCm);
            }

            return new RtsVisualSpec(diameterCm, heightCm);
        }

        private static float ReadPositive(float value, float fallback)
        {
            return value > 0f ? value : fallback;
        }
    }
}
