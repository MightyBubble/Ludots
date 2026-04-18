using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Registers the framework's built-in <see cref="PerformerDefinition"/> entries
    /// using string keys from <see cref="WellKnownPerformerKeys"/>.
    /// Mesh references are resolved through <see cref="MeshAssetRegistry"/> at registration time.
    /// </summary>
    public static class BuiltinPerformerDefinitions
    {
        public static void Register(
            PerformerDefinitionRegistry registry,
            MeshAssetRegistry meshes,
            Func<string, int>? resolveTextTokenId = null)
        {
            resolveTextTokenId ??= _ => 0;
            int sphereId = meshes.GetId(WellKnownMeshKeys.Sphere);

            RegisterCastCommittedMarker(registry, sphereId);
            RegisterCastFailedMarker(registry, sphereId);
            RegisterFloatingCombatText(registry, resolveTextTokenId);
        }

        private static void RegisterCastCommittedMarker(PerformerDefinitionRegistry registry, int sphereId)
        {
            string key = WellKnownPerformerKeys.CastCommittedMarker;
            int id = registry.GetOrRegisterId(key);
            registry.Register(key, new PerformerDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = sphereId,
                            RenderPath = Components.VisualRenderPath.StaticMesh,
                            Mobility = Components.VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalScale = new Vector3(0.55f, 0.55f, 0.55f),
                        },
                    },
                },
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = sphereId,
                DefaultColor = new Vector4(0f, 1f, 1f, 0.9f),
                DefaultScale = 0.55f,
                DefaultLifetime = 0.22f,
                AlphaFadeOverLifetime = true,
                PositionOffset = new Vector3(0f, 0.6f, 0f),
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
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeTag = -1,
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
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = sphereId,
                            RenderPath = Components.VisualRenderPath.StaticMesh,
                            Mobility = Components.VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalScale = new Vector3(0.2f, 0.2f, 0.2f),
                        },
                    },
                },
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = sphereId,
                DefaultColor = new Vector4(0.7f, 0.7f, 0.7f, 0.6f),
                DefaultScale = 0.2f,
                DefaultLifetime = 0.15f,
                AlphaFadeOverLifetime = true,
                PositionOffset = new Vector3(0f, 0.9f, 0f),
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
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeTag = -1,
                        }
                    }
                },
            });
        }

        private static void RegisterFloatingCombatText(PerformerDefinitionRegistry registry, Func<string, int> resolveTextTokenId)
        {
            string key = WellKnownPerformerKeys.FloatingCombatText;
            int id = registry.GetOrRegisterId(key);
            int textTokenId = resolveTextTokenId(WellKnownHudTextKeys.CombatDelta);
            if (textTokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Built-in performer '{WellKnownPerformerKeys.FloatingCombatText}' requires text token '{WellKnownHudTextKeys.CombatDelta}'.");
            }

            registry.Register(key, new PerformerDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.WorldText,
                            AssetId = textTokenId,
                            Mobility = Components.VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalScale = Vector3.One,
                        },
                    },
                },
                VisualKind = PerformerVisualKind.WorldText,
                DefaultColor = new Vector4(1f, 0.2f, 0.1f, 1f),
                DefaultFontSize = 18,
                DefaultTextId = textTokenId,
                LegacyWorldTextMode = WorldHudValueMode.AttributeCurrent,
                DefaultLifetime = 1.2f,
                PositionOffset = new Vector3(0f, 1.0f, 0f),
                PositionYDriftPerSecond = 0.8f,
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
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = id,
                            ScopeTag = -1,
                        }
                    }
                },
            });
        }
    }
}
