using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SearchPhaseConvergenceTests
    {
        [Test]
        public void SearchHandlers_RunThroughEffectPhaseExecutor_AndCollectFanOutCommands()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var source = world.Create(WorldPositionCm.FromCm(0, 0));
            var center = world.Create(WorldPositionCm.FromCm(0, 0));
            var resolved = world.Create(WorldPositionCm.FromCm(100, 0));

            var preset = new PresetTypeDefinition { Type = EffectPresetType.Search };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnResolve] = PhaseHandler.Builtin(BuiltinHandlerId.SpatialQuery);
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.DispatchPayload);
            presetTypes.Register(in preset);

            const int templateId = 200;
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.Search,
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor
                    {
                        Shape = SpatialShape.Circle,
                        RadiusCm = 500,
                        RelationFilter = RelationshipFilter.All,
                        MaxTargets = 0,
                    }
                },
                TargetFilter = new TargetFilterDescriptor
                {
                    RelationFilter = RelationshipFilter.All,
                    MaxTargets = 0,
                },
                TargetDispatch = new TargetDispatchDescriptor
                {
                    PayloadEffectTemplateId = 901,
                    ContextMapping = TargetResolverContextMapping.Default,
                },
            });

            var runtime = new BuiltinHandlerExecutionContext
            {
                SpatialQueries = new StubSpatialQueryService(resolved),
                FanOutBudget = new RootBudgetTable(16),
                FanOutCommands = new List<FanOutCommand>(),
                ResolverBuffer = new Entity[8],
            };
            runtime.ResetPerEffect();

            var behavior = new EffectPhaseGraphBindings();
            executor.ExecutePhase(world, api, source, center, default, default,
                EffectPhaseId.OnResolve, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);
            executor.ExecutePhase(world, api, source, center, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);

            That(runtime.FanOutCommands, Has.Count.EqualTo(1));
            var cmd = runtime.FanOutCommands![0];
            That(cmd.PayloadEffectTemplateId, Is.EqualTo(901));
            That(cmd.OriginalSource, Is.EqualTo(source));
            That(cmd.OriginalTarget, Is.EqualTo(center));
            That(cmd.ResolvedEntity, Is.EqualTo(resolved));
        }

        [Test]
        public void PeriodicSearchHandler_RunThroughEffectPhaseExecutor_AndCollectFanOutCommands()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var source = world.Create(WorldPositionCm.FromCm(0, 0));
            var center = world.Create(WorldPositionCm.FromCm(0, 0));
            var resolved = world.Create(WorldPositionCm.FromCm(200, 0));

            var preset = new PresetTypeDefinition { Type = EffectPresetType.PeriodicSearch };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnPeriod] = PhaseHandler.Builtin(BuiltinHandlerId.ReResolveAndDispatch);
            presetTypes.Register(in preset);

            const int templateId = 201;
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.PeriodicSearch,
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor
                    {
                        Shape = SpatialShape.Circle,
                        RadiusCm = 500,
                        RelationFilter = RelationshipFilter.All,
                        MaxTargets = 0,
                    }
                },
                TargetFilter = new TargetFilterDescriptor
                {
                    RelationFilter = RelationshipFilter.All,
                    MaxTargets = 0,
                },
                TargetDispatch = new TargetDispatchDescriptor
                {
                    PayloadEffectTemplateId = 902,
                    ContextMapping = TargetResolverContextMapping.Default,
                },
            });

            var runtime = new BuiltinHandlerExecutionContext
            {
                SpatialQueries = new StubSpatialQueryService(resolved),
                FanOutBudget = new RootBudgetTable(16),
                FanOutCommands = new List<FanOutCommand>(),
                ResolverBuffer = new Entity[8],
            };
            runtime.ResetPerEffect();

            var behavior = new EffectPhaseGraphBindings();
            executor.ExecutePhase(world, api, source, center, default, default,
                EffectPhaseId.OnPeriod, in behavior, EffectPresetType.PeriodicSearch, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);

            That(runtime.FanOutCommands, Has.Count.EqualTo(1));
            var cmd = runtime.FanOutCommands![0];
            That(cmd.PayloadEffectTemplateId, Is.EqualTo(902));
            That(cmd.ResolvedEntity, Is.EqualTo(resolved));
        }

        [Test]
        public void SearchHandler_HostileFilter_SkipsCandidatesWithoutTeam()
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            try
            {
                using var world = World.Create();
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                var templates = new EffectTemplateRegistry();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var source = world.Create(WorldPositionCm.FromCm(0, 0), new Team { Id = 1 });
                var center = world.Create(WorldPositionCm.FromCm(0, 0));
                var nonTeamSpatial = world.Create(WorldPositionCm.FromCm(100, 0));
                var hostile = world.Create(WorldPositionCm.FromCm(120, 0), new Team { Id = 2 });

                var preset = new PresetTypeDefinition { Type = EffectPresetType.Search };
                preset.DefaultPhaseHandlers[EffectPhaseId.OnResolve] = PhaseHandler.Builtin(BuiltinHandlerId.SpatialQuery);
                preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.DispatchPayload);
                presetTypes.Register(in preset);

                const int templateId = 202;
                templates.Register(templateId, new EffectTemplateData
                {
                    PresetType = EffectPresetType.Search,
                    TargetQuery = new TargetQueryDescriptor
                    {
                        Kind = TargetResolverKind.BuiltinSpatial,
                        Spatial = new BuiltinSpatialDescriptor
                        {
                            Shape = SpatialShape.Circle,
                            RadiusCm = 500,
                        }
                    },
                    TargetFilter = new TargetFilterDescriptor
                    {
                        RelationFilter = RelationshipFilter.Hostile,
                        ExcludeSource = true,
                        MaxTargets = 1,
                    },
                    TargetDispatch = new TargetDispatchDescriptor
                    {
                        PayloadEffectTemplateId = 903,
                        ContextMapping = TargetResolverContextMapping.Default,
                    },
                });

                var runtime = new BuiltinHandlerExecutionContext
                {
                    SpatialQueries = new StubSpatialQueryService(nonTeamSpatial, hostile),
                    FanOutBudget = new RootBudgetTable(16),
                    FanOutCommands = new List<FanOutCommand>(),
                    ResolverBuffer = new Entity[8],
                };
                runtime.ResetPerEffect();

                var behavior = new EffectPhaseGraphBindings();
                executor.ExecutePhase(world, api, source, center, default, default,
                    EffectPhaseId.OnResolve, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);
                executor.ExecutePhase(world, api, source, center, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);

                That(runtime.FanOutCommands, Has.Count.EqualTo(1));
                That(runtime.FanOutCommands![0].ResolvedEntity, Is.EqualTo(hostile));
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void SearchHandler_HostileFilter_RejectsWhenSourceHasNoTeam()
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            try
            {
                using var world = World.Create();
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                var templates = new EffectTemplateRegistry();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var sourceWithoutTeam = world.Create(WorldPositionCm.FromCm(0, 0));
                var center = world.Create(WorldPositionCm.FromCm(0, 0));
                var hostile = world.Create(WorldPositionCm.FromCm(120, 0), new Team { Id = 2 });

                var preset = new PresetTypeDefinition { Type = EffectPresetType.Search };
                preset.DefaultPhaseHandlers[EffectPhaseId.OnResolve] = PhaseHandler.Builtin(BuiltinHandlerId.SpatialQuery);
                preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.DispatchPayload);
                presetTypes.Register(in preset);

                const int templateId = 203;
                templates.Register(templateId, new EffectTemplateData
                {
                    PresetType = EffectPresetType.Search,
                    TargetQuery = new TargetQueryDescriptor
                    {
                        Kind = TargetResolverKind.BuiltinSpatial,
                        Spatial = new BuiltinSpatialDescriptor
                        {
                            Shape = SpatialShape.Circle,
                            RadiusCm = 500,
                        }
                    },
                    TargetFilter = new TargetFilterDescriptor
                    {
                        RelationFilter = RelationshipFilter.Hostile,
                        ExcludeSource = true,
                        MaxTargets = 1,
                    },
                    TargetDispatch = new TargetDispatchDescriptor
                    {
                        PayloadEffectTemplateId = 904,
                        ContextMapping = TargetResolverContextMapping.Default,
                    },
                });

                var runtime = new BuiltinHandlerExecutionContext
                {
                    SpatialQueries = new StubSpatialQueryService(hostile),
                    FanOutBudget = new RootBudgetTable(16),
                    FanOutCommands = new List<FanOutCommand>(),
                    ResolverBuffer = new Entity[8],
                };
                runtime.ResetPerEffect();

                var behavior = new EffectPhaseGraphBindings();
                executor.ExecutePhase(world, api, sourceWithoutTeam, center, default, default,
                    EffectPhaseId.OnResolve, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);
                executor.ExecutePhase(world, api, sourceWithoutTeam, center, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.Search, effectTagId: 0, effectTemplateId: templateId, builtinRuntime: runtime);

                That(runtime.FanOutCommands, Is.Empty);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity[] _entities;

            public StubSpatialQueryService(params Entity[] entities)
            {
                _entities = entities;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

            private SpatialQueryResult Write(Span<Entity> buffer)
            {
                if (buffer.Length == 0 || _entities.Length == 0)
                {
                    return new SpatialQueryResult(0, _entities.Length);
                }

                int count = Math.Min(buffer.Length, _entities.Length);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _entities[i];
                }

                return new SpatialQueryResult(count, _entities.Length - count);
            }
        }
    }
}
