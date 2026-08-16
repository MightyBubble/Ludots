using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;
using Ludots.Core.Gameplay.GAS.Registry;

namespace DesertStrikeShowcaseMod.Runtime
{
    public struct DesertStrikeUnit
    {
    }

    public struct DesertStrikeBase
    {
    }

    internal static class DesertStrikeComponentAuthoring
    {
        public static void Register(string modId)
        {
            CoreComponentRegistry.Register<DesertStrikeUnit>("DesertStrikeUnit", AddEmptyMarker<DesertStrikeUnit>, modId);
            CoreComponentRegistry.Register<DesertStrikeBase>("DesertStrikeBase", AddEmptyMarker<DesertStrikeBase>, modId);
        }

        private static void AddEmptyMarker<T>(Entity entity, JsonNode data)
            where T : struct
        {
            if (data is not JsonObject)
            {
                throw new InvalidOperationException($"{typeof(T).Name} requires an object payload.");
            }

            entity.Add<T>(default);
        }
    }

    internal static class DesertStrikeAttributeSetup
    {
        public static void EnsureRegistered()
        {
            EnsureRegistered("Health");
            EnsureRegistered("Minerals");
            EnsureRegistered("MoveSpeed");
            EnsureRegistered("AttackRange");
        }

        private static void EnsureRegistered(string attributeName)
        {
            if (AttributeRegistry.GetId(attributeName) <= 0)
            {
                AttributeRegistry.Register(attributeName);
            }
        }
    }
}
