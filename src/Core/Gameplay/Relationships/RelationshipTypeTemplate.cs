using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.Core.Utils;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships.Config;
using ComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Birth patch for materialized relationship entities of one type. Baked once at catalog install
    /// through the ComponentRegistry authoring chain onto a throwaway prototype entity; application at
    /// materialization copies the baked values with zero JSON parsing.
    /// </summary>
    public sealed class RelationshipTypeTemplate
    {
        private const string RuntimeOwnedIdentityComponentName = "RelationshipInstanceCm";

        /// <summary>Components materialization already creates on the relationship entity; the patch overwrites them.</summary>
        private static readonly ComponentType[] MaterializationComponentTypes =
        {
            Component<RelationshipInstanceCm>.ComponentType,
            Component<AttributeBuffer>.ComponentType,
            Component<GameplayTagContainer>.ComponentType,
            Component<TagCountContainer>.ComponentType,
            Component<DirtyFlags>.ComponentType,
            Component<ActiveEffectContainer>.ComponentType,
        };

        private readonly object[] _addValues;
        private readonly object[] _setValueOverrides;

        private RelationshipTypeTemplate(object[] addValues, object[] setValueOverrides)
        {
            _addValues = addValues;
            _setValueOverrides = setValueOverrides;
        }

        public static RelationshipTypeTemplate Bake(
            World world,
            string relationshipTypeName,
            RelationshipTypeTemplateConfig config,
            ComponentAuthoringContext? authoringContext)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(config);
            authoringContext ??= ComponentAuthoringContext.Empty;

            Entity prototype = world.Create();
            try
            {
                foreach (KeyValuePair<string, JsonNode> entry in config.Components)
                {
                    if (string.Equals(entry.Key, RuntimeOwnedIdentityComponentName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Relationship type '{relationshipTypeName}' template cannot author component " +
                            $"'{RuntimeOwnedIdentityComponentName}'; relationship identity is runtime-owned.");
                    }

                    ComponentRegistry.Apply(
                        prototype,
                        entry.Key,
                        entry.Value,
                        authoringContext,
                        $"relationship type '{relationshipTypeName}' template component '{entry.Key}'");
                }

                return CompileBakedPatch(world, prototype);
            }
            finally
            {
                world.Destroy(prototype);
            }
        }

        public void Apply(World world, Entity entity)
        {
            // Per-component Add (cached archetype add-edge) instead of AddRange: World.AddRange sizes a
            // bitset stackalloc from the global component count, which the JIT heap-materializes inside
            // loops once the registry grows, making every materialization allocate.
            for (int i = 0; i < _addValues.Length; i++)
            {
                world.Add(entity, _addValues[i]);
            }

            for (int i = 0; i < _setValueOverrides.Length; i++)
            {
                world.Set(entity, _setValueOverrides[i]);
            }
        }

        private static RelationshipTypeTemplate CompileBakedPatch(World world, Entity prototype)
        {
            var setValueOverrides = new List<object>();
            var addValues = new List<object>();
            foreach (ComponentType componentType in world.GetArchetype(prototype).Signature)
            {
                object? value = prototype.Get(componentType);
                if (value == null)
                {
                    throw new InvalidOperationException(
                        $"Relationship template prototype component '{componentType}' produced a null value.");
                }

                if (IsMaterializationComponent(componentType))
                {
                    setValueOverrides.Add(value);
                }
                else
                {
                    addValues.Add(value);
                }
            }

            return new RelationshipTypeTemplate(addValues.ToArray(), setValueOverrides.ToArray());
        }

        private static bool IsMaterializationComponent(ComponentType componentType)
        {
            for (int i = 0; i < MaterializationComponentTypes.Length; i++)
            {
                if (MaterializationComponentTypes[i].Equals(componentType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
