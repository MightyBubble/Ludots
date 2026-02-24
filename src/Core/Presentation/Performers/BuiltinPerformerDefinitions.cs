using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Well-known definition IDs for built-in performers.
    /// These are registered by <see cref="BuiltinPerformerDefinitions.Register"/>.
    /// </summary>
    public static class BuiltinPerformerIds
    {
        /// <summary>Cast committed marker — brief sphere flash on target/actor.</summary>
        public const int CastCommittedMarker = 9001;
        /// <summary>Cast failed marker — brief small grey sphere.</summary>
        public const int CastFailedMarker = 9002;
        /// <summary>Floating combat text — rising, fading damage/heal number.</summary>
        public const int FloatingCombatText = 9003;
        /// <summary>Entity-scoped health bar (attribute-driven).</summary>
        public const int EntityHealthBar = 9010;
    }

    /// <summary>
    /// Registers the framework's built-in <see cref="PerformerDefinition"/> entries
    /// that replicate the behavior previously hardcoded in CastFeedbackSystem,
    /// FloatingCombatTextSystem, and WorldHudCollectorSystem.
    /// </summary>
    public static class BuiltinPerformerDefinitions
    {
        public static void Register(PerformerDefinitionRegistry registry)
        {
            RegisterCastCommittedMarker(registry);
            RegisterCastFailedMarker(registry);
            RegisterFloatingCombatText(registry);
            RegisterEntityHealthBar(registry);
        }

        /// <summary>
        /// CastCommitted → short-lived Marker3D sphere.
        /// Triggered by a PerformerRule matching CastCommitted events.
        /// </summary>
        private static void RegisterCastCommittedMarker(PerformerDefinitionRegistry registry)
        {
            registry.Register(BuiltinPerformerIds.CastCommittedMarker, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = PrimitiveMeshAssetIds.Sphere,
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
                            KeyId = -1 // any ability
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = BuiltinPerformerIds.CastCommittedMarker,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        /// <summary>
        /// CastFailed → brief small grey sphere.
        /// </summary>
        private static void RegisterCastFailedMarker(PerformerDefinitionRegistry registry)
        {
            registry.Register(BuiltinPerformerIds.CastFailedMarker, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = PrimitiveMeshAssetIds.Sphere,
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
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = BuiltinPerformerIds.CastFailedMarker,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        /// <summary>
        /// EffectApplied → floating combat text (WorldText) that drifts up and fades.
        /// </summary>
        private static void RegisterFloatingCombatText(PerformerDefinitionRegistry registry)
        {
            registry.Register(BuiltinPerformerIds.FloatingCombatText, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldText,
                DefaultColor = new Vector4(1f, 0.2f, 0.1f, 1f), // damage color default
                DefaultFontSize = 18,
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
                            KeyId = -1 // any effect
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = BuiltinPerformerIds.FloatingCombatText,
                            ScopeId = -1,
                        }
                    }
                },
            });
        }

        /// <summary>
        /// Entity-scoped health bar — rendered for every entity with AttributeBuffer.
        /// Uses ParamKey 0 (FillRatio) bound to attribute-based computation via JSON config.
        /// This is a placeholder; actual attribute bindings are loaded from Presentation/performers.json.
        /// </summary>
        private static void RegisterEntityHealthBar(PerformerDefinitionRegistry registry)
        {
            registry.Register(BuiltinPerformerIds.EntityHealthBar, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                EntityScope = EntityScopeFilter.AllWithAttributes,
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
                DefaultColor = new Vector4(0f, 1f, 0f, 1f), // foreground green
                DefaultScale = 1f,
                PositionOffset = new Vector3(0f, 1.5f, 0f),
                // Bindings are expected to be overridden by JSON config with actual attribute IDs.
                // The defaults here provide a minimal visual (full bar, standard size).
            });
        }
    }
}
