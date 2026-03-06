using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Registers the framework's built-in <see cref="PerformerDefinition"/> entries
    /// using string keys from <see cref="WellKnownPerformerKeys"/>.
    /// Mesh references are resolved through <see cref="MeshAssetRegistry"/> at registration time.
    /// </summary>
    public static class BuiltinPerformerDefinitions
    {
        public static void Register(PerformerDefinitionRegistry registry, MeshAssetRegistry meshes)
        {
            int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);

            RegisterCastCommittedMarker(registry, sphereId);
            RegisterCastFailedMarker(registry, sphereId);
            RegisterFloatingCombatText(registry);
            RegisterEntityHealthBar(registry);
        }

        private static void RegisterCastCommittedMarker(PerformerDefinitionRegistry registry, int sphereId)
        {
            string key = WellKnownPerformerKeys.CastCommittedMarker;
            int id = registry.GetOrRegisterId(key);
            registry.Register(key, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = sphereId,
                DefaultColor = new Vector4(0f, 1f, 1f, 0.9f),
                DefaultScale = 0.4f,
                DefaultLifetime = 0.5f,
                AlphaFadeOverLifetime = true,
                PositionOffset = new Vector3(0f, 1.2f, 0f),
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.CastCommitted,
                            KeyId = -1
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        private static void RegisterCastFailedMarker(PerformerDefinitionRegistry registry, int sphereId)
        {
            string key = WellKnownPerformerKeys.CastFailedMarker;
            int id = registry.GetOrRegisterId(key);
            registry.Register(key, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = sphereId,
                DefaultColor = new Vector4(1f, 0.3f, 0.3f, 0.8f),
                DefaultScale = 0.3f,
                DefaultLifetime = 0.4f,
                AlphaFadeOverLifetime = true,
                PositionOffset = new Vector3(0f, 1.4f, 0f),
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.CastFailed,
                            KeyId = -1
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        private static void RegisterFloatingCombatText(PerformerDefinitionRegistry registry)
        {
            string key = WellKnownPerformerKeys.FloatingCombatText;
            int id = registry.GetOrRegisterId(key);
            registry.Register(key, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldText,
                DefaultColor = new Vector4(1f, 0.2f, 0.1f, 1f),
                DefaultFontSize = 24,
                DefaultLifetime = 1.5f,
                PositionOffset = new Vector3(0f, 1.6f, 0f),
                PositionYDriftPerSecond = 1.0f,
                AlphaFadeOverLifetime = true,
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EffectApplied,
                            KeyId = -1
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        private static void RegisterEntityHealthBar(PerformerDefinitionRegistry registry)
        {
            registry.Register(WellKnownPerformerKeys.EntityHealthBar, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                EntityScope = EntityScopeFilter.AllWithAttributes,
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
                DefaultColor = new Vector4(0f, 1f, 0f, 1f),
                DefaultScale = 1f,
                PositionOffset = new Vector3(0f, 1.5f, 0f),
            });
        }
    }
}
