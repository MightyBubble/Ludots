using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for the Preset Type System introduced in the architecture overhaul:
    ///   PhaseHandler, PhaseHandlerMap, BuiltinHandlerRegistry,
    ///   ComponentFlags, PhaseFlags, LifetimeFlags,
    ///   PresetTypeDefinition, PresetTypeRegistry, PresetTypeLoader,
    ///   EffectParamKeys, ConfigParamsMerger, EffectConfigParams.MergeFrom.
    /// </summary>
    [TestFixture]
    public class PresetTypeSystemTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  PhaseHandler & PhaseHandlerMap
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PhaseHandler_Builtin_CreatesCorrectKindAndId()
        {
            var h = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            That(h.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(h.HandlerId, Is.EqualTo((int)BuiltinHandlerId.ApplyModifiers));
            That(h.IsValid, Is.True);
        }

        [Test]
        public void PhaseHandler_Graph_CreatesCorrectKindAndId()
        {
            var h = PhaseHandler.Graph(42);
            That(h.Kind, Is.EqualTo(PhaseHandlerKind.Graph));
            That(h.HandlerId, Is.EqualTo(42));
            That(h.IsValid, Is.True);
        }

        [Test]
        public void PhaseHandler_None_IsNotValid()
        {
            var h = PhaseHandler.None;
            That(h.Kind, Is.EqualTo(PhaseHandlerKind.None));
            That(h.IsValid, Is.False);
        }

        [Test]
        public unsafe void PhaseHandlerMap_SetAndGet_RoundTrips()
        {
            var map = new PhaseHandlerMap();
            map[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            map[EffectPhaseId.OnPeriod] = PhaseHandler.Graph(99);
            map[EffectPhaseId.OnResolve] = PhaseHandler.Builtin(BuiltinHandlerId.SpatialQuery);

            var hApply = map[EffectPhaseId.OnApply];
            That(hApply.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hApply.HandlerId, Is.EqualTo((int)BuiltinHandlerId.ApplyModifiers));

            var hPeriod = map[EffectPhaseId.OnPeriod];
            That(hPeriod.Kind, Is.EqualTo(PhaseHandlerKind.Graph));
            That(hPeriod.HandlerId, Is.EqualTo(99));

            var hResolve = map[EffectPhaseId.OnResolve];
            That(hResolve.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hResolve.HandlerId, Is.EqualTo((int)BuiltinHandlerId.SpatialQuery));
        }

        [Test]
        public unsafe void PhaseHandlerMap_UnsetPhase_ReturnsNone()
        {
            var map = new PhaseHandlerMap();
            var h = map[EffectPhaseId.OnExpire];
            That(h.Kind, Is.EqualTo(PhaseHandlerKind.None));
            That(h.IsValid, Is.False);
        }

        [Test]
        public unsafe void PhaseHandlerMap_AllPhases_SetAndGet()
        {
            var map = new PhaseHandlerMap();
            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                map[(EffectPhaseId)i] = PhaseHandler.Builtin((BuiltinHandlerId)(i + 1));
            }

            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                var h = map[(EffectPhaseId)i];
                That(h.Kind, Is.EqualTo(PhaseHandlerKind.Builtin), $"Phase {(EffectPhaseId)i}");
                That(h.HandlerId, Is.EqualTo(i + 1), $"Phase {(EffectPhaseId)i}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  BuiltinHandlerRegistry
        // ════════════════════════════════════════════════════════════════════

        private static int _handlerCallCount;

        private static void TestHandler(World w, Entity e, ref EffectContext ctx, in EffectConfigParams p, in EffectTemplateData t)
        {
            _handlerCallCount++;
        }

        [Test]
        public void BuiltinHandlerRegistry_RegisterAndInvoke_CallsCorrectHandler()
        {
            var reg = new BuiltinHandlerRegistry();
            _handlerCallCount = 0;
            reg.Register(BuiltinHandlerId.ApplyModifiers, TestHandler);

            That(reg.IsRegistered(BuiltinHandlerId.ApplyModifiers), Is.True);

            using var world = World.Create();
            var entity = world.Create();
            var ctx = new EffectContext();
            var param = new EffectConfigParams();
            var tpl = new EffectTemplateData();
            reg.Invoke(BuiltinHandlerId.ApplyModifiers, world, entity, ref ctx, in param, in tpl);

            That(_handlerCallCount, Is.EqualTo(1));
        }

        [Test]
        public void BuiltinHandlerRegistry_UnregisteredId_ThrowsOnInvoke()
        {
            var reg = new BuiltinHandlerRegistry();
            That(reg.IsRegistered(BuiltinHandlerId.SpatialQuery), Is.False);

            using var world = World.Create();
            var entity = world.Create();
            var ctx = new EffectContext();
            var param = new EffectConfigParams();
            var tpl = new EffectTemplateData();

            Assert.Throws<InvalidOperationException>(() =>
                reg.Invoke(BuiltinHandlerId.SpatialQuery, world, entity, ref ctx, in param, in tpl));
        }

        private static void NoOpHandler(World w, Entity e, ref EffectContext ctx, in EffectConfigParams p, in EffectTemplateData t) { }

        [Test]
        public void BuiltinHandlerRegistry_OverflowId_ThrowsOnRegister()
        {
            var reg = new BuiltinHandlerRegistry();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                reg.Register((BuiltinHandlerId)999, NoOpHandler));
        }

        // ════════════════════════════════════════════════════════════════════
        //  ComponentFlags
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ComponentFlags_BitwiseCombine_Works()
        {
            var flags = ComponentFlags.ModifierParams | ComponentFlags.DurationParams;
            That((flags & ComponentFlags.ModifierParams) != 0, Is.True);
            That((flags & ComponentFlags.DurationParams) != 0, Is.True);
            That((flags & ComponentFlags.ForceParams) != 0, Is.False);
        }

        [Test]
        public void ComponentFlags_ParameterAndCapability_NoBitOverlap()
        {
            // Parameter components use bits 0-7, capability components use bits 16-17
            var allParams = ComponentFlags.ModifierParams | ComponentFlags.DurationParams |
                            ComponentFlags.TargetQueryParams | ComponentFlags.TargetFilterParams |
                            ComponentFlags.TargetDispatchParams | ComponentFlags.ForceParams |
                            ComponentFlags.ProjectileParams | ComponentFlags.UnitCreationParams;
            var allCaps = ComponentFlags.PhaseGraphBindings | ComponentFlags.PhaseListenerSetup;
            That((allParams & allCaps), Is.EqualTo(ComponentFlags.None), "No overlap between param and capability flags");
        }

        // ════════════════════════════════════════════════════════════════════
        //  PhaseFlags
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PhaseFlags_Has_CorrectForAllPhases()
        {
            var flags = PhaseFlags.InstantCore; // OnPropose | OnCalculate | OnHit | OnApply
            That(flags.Has(EffectPhaseId.OnPropose), Is.True);
            That(flags.Has(EffectPhaseId.OnCalculate), Is.True);
            That(flags.Has(EffectPhaseId.OnHit), Is.True);
            That(flags.Has(EffectPhaseId.OnApply), Is.True);
            That(flags.Has(EffectPhaseId.OnResolve), Is.False);
            That(flags.Has(EffectPhaseId.OnPeriod), Is.False);
            That(flags.Has(EffectPhaseId.OnExpire), Is.False);
            That(flags.Has(EffectPhaseId.OnRemove), Is.False);
        }

        [Test]
        public void PhaseFlags_ToFlag_RoundTrips()
        {
            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                var phase = (EffectPhaseId)i;
                var flag = phase.ToFlag();
                That(flag.Has(phase), Is.True, $"ToFlag({phase}).Has({phase}) should be true");

                // no other phase should match
                for (int j = 0; j < EffectPhaseConstants.PhaseCount; j++)
                {
                    if (j == i) continue;
                    That(flag.Has((EffectPhaseId)j), Is.False, $"ToFlag({phase}).Has({(EffectPhaseId)j}) should be false");
                }
            }
        }

        [Test]
        public void PhaseFlags_DurationFull_IncludesAllDurationPhases()
        {
            var f = PhaseFlags.DurationFull;
            That(f.Has(EffectPhaseId.OnPropose), Is.True);
            That(f.Has(EffectPhaseId.OnCalculate), Is.True);
            That(f.Has(EffectPhaseId.OnHit), Is.True);
            That(f.Has(EffectPhaseId.OnApply), Is.True);
            That(f.Has(EffectPhaseId.OnPeriod), Is.True);
            That(f.Has(EffectPhaseId.OnExpire), Is.True);
            That(f.Has(EffectPhaseId.OnRemove), Is.True);
            That(f.Has(EffectPhaseId.OnResolve), Is.False);
        }

        // ════════════════════════════════════════════════════════════════════
        //  LifetimeFlags
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void LifetimeFlags_Allows_CorrectForAllKinds()
        {
            var flags = LifetimeFlags.Duration; // After | Infinite
            That(flags.Allows(EffectLifetimeKind.After), Is.True);
            That(flags.Allows(EffectLifetimeKind.Infinite), Is.True);
            That(flags.Allows(EffectLifetimeKind.Instant), Is.False);
        }

        [Test]
        public void LifetimeFlags_All_AllowsEveryKind()
        {
            var flags = LifetimeFlags.All;
            That(flags.Allows(EffectLifetimeKind.Instant), Is.True);
            That(flags.Allows(EffectLifetimeKind.After), Is.True);
            That(flags.Allows(EffectLifetimeKind.Infinite), Is.True);
        }

        [Test]
        public void LifetimeFlags_ToFlag_RoundTrips()
        {
            for (int i = 0; i <= 2; i++)
            {
                var kind = (EffectLifetimeKind)i;
                var flag = kind.ToFlag();
                That(flag.Allows(kind), Is.True, $"ToFlag({kind}).Allows({kind})");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PresetTypeDefinition
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeDefinition_HasComponent_ChecksFlags()
        {
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.DoT,
                Components = ComponentFlags.ModifierParams | ComponentFlags.DurationParams | ComponentFlags.PhaseGraphBindings,
            };

            That(def.HasComponent(ComponentFlags.ModifierParams), Is.True);
            That(def.HasComponent(ComponentFlags.DurationParams), Is.True);
            That(def.HasComponent(ComponentFlags.PhaseGraphBindings), Is.True);
            That(def.HasComponent(ComponentFlags.ForceParams), Is.False);
            That(def.HasComponent(ComponentFlags.ProjectileParams), Is.False);
        }

        [Test]
        public void PresetTypeDefinition_HasPhase_ChecksActivePhases()
        {
            var def = new PresetTypeDefinition
            {
                ActivePhases = PhaseFlags.InstantCore,
            };

            That(def.HasPhase(EffectPhaseId.OnApply), Is.True);
            That(def.HasPhase(EffectPhaseId.OnPeriod), Is.False);
        }

        [Test]
        public void PresetTypeDefinition_AllowsLifetime_ChecksConstraints()
        {
            var def = new PresetTypeDefinition
            {
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };

            That(def.AllowsLifetime(EffectLifetimeKind.Instant), Is.True);
            That(def.AllowsLifetime(EffectLifetimeKind.After), Is.False);
            That(def.AllowsLifetime(EffectLifetimeKind.Infinite), Is.False);
        }

        // ════════════════════════════════════════════════════════════════════
        //  PresetTypeRegistry
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeRegistry_RegisterAndGet_RoundTrips()
        {
            var reg = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            reg.Register(in def);

            That(reg.IsRegistered(EffectPresetType.InstantDamage), Is.True);
            ref readonly var got = ref reg.Get(EffectPresetType.InstantDamage);
            That(got.Type, Is.EqualTo(EffectPresetType.InstantDamage));
            That(got.Components, Is.EqualTo(ComponentFlags.ModifierParams));
        }

        [Test]
        public void PresetTypeRegistry_TryGet_ReturnsFalse_WhenNotRegistered()
        {
            var reg = new PresetTypeRegistry();
            That(reg.IsRegistered(EffectPresetType.DoT), Is.False);
            That(reg.TryGet(EffectPresetType.DoT, out _), Is.False);
        }

        [Test]
        public void PresetTypeRegistry_Clear_RemovesAll()
        {
            var reg = new PresetTypeRegistry();
            var def = new PresetTypeDefinition { Type = EffectPresetType.Buff };
            reg.Register(in def);
            That(reg.IsRegistered(EffectPresetType.Buff), Is.True);

            reg.Clear();
            That(reg.IsRegistered(EffectPresetType.Buff), Is.False);
        }

        // ════════════════════════════════════════════════════════════════════
        //  PresetTypeLoader (JSON → Registry)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeLoader_LoadsSearch_WithCorrectHandlers()
        {
            const string json = @"[
              {
                ""id"": ""Search"",
                ""components"": [""TargetQueryParams"", ""TargetFilterParams"", ""TargetDispatchParams"", ""PhaseGraphBindings""],
                ""activePhases"": [""OnPropose"", ""OnResolve"", ""OnHit"", ""OnApply""],
                ""allowedLifetimes"": [""Instant""],
                ""defaultPhaseHandlers"": {
                  ""OnResolve"": { ""type"": ""builtin"", ""id"": ""SpatialQuery"" },
                  ""OnApply"": { ""type"": ""builtin"", ""id"": ""DispatchPayload"" }
                }
              }
            ]";

            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, json);

            That(reg.IsRegistered(EffectPresetType.Search), Is.True);
            ref readonly var def = ref reg.Get(EffectPresetType.Search);
            That(def.HasComponent(ComponentFlags.TargetQueryParams), Is.True);
            That(def.HasComponent(ComponentFlags.TargetFilterParams), Is.True);
            That(def.HasComponent(ComponentFlags.TargetDispatchParams), Is.True);
            That(def.HasComponent(ComponentFlags.PhaseGraphBindings), Is.True);
            That(def.HasComponent(ComponentFlags.ModifierParams), Is.False);

            That(def.HasPhase(EffectPhaseId.OnPropose), Is.True);
            That(def.HasPhase(EffectPhaseId.OnResolve), Is.True);
            That(def.HasPhase(EffectPhaseId.OnPeriod), Is.False);

            That(def.AllowsLifetime(EffectLifetimeKind.Instant), Is.True);
            That(def.AllowsLifetime(EffectLifetimeKind.After), Is.False);

            var hResolve = def.DefaultPhaseHandlers[EffectPhaseId.OnResolve];
            That(hResolve.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hResolve.HandlerId, Is.EqualTo((int)BuiltinHandlerId.SpatialQuery));

            var hApply = def.DefaultPhaseHandlers[EffectPhaseId.OnApply];
            That(hApply.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hApply.HandlerId, Is.EqualTo((int)BuiltinHandlerId.DispatchPayload));
        }

        [Test]
        public void PresetTypeLoader_LoadsMultipleTypes()
        {
            const string json = @"[
              { ""id"": ""InstantDamage"", ""components"": [""ModifierParams""], ""activePhases"": [""OnApply""], ""allowedLifetimes"": [""Instant""], ""defaultPhaseHandlers"": {} },
              { ""id"": ""Buff"", ""components"": [""ModifierParams"", ""DurationParams""], ""activePhases"": [""OnApply"", ""OnExpire""], ""allowedLifetimes"": [""After"", ""Infinite""], ""defaultPhaseHandlers"": {} }
            ]";

            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, json);

            That(reg.IsRegistered(EffectPresetType.InstantDamage), Is.True);
            That(reg.IsRegistered(EffectPresetType.Buff), Is.True);
            That(reg.IsRegistered(EffectPresetType.DoT), Is.False);
        }

        [Test]
        public void PresetTypeLoader_GraphHandler_ParsesNumericId()
        {
            const string json = @"[
              {
                ""id"": ""Heal"",
                ""components"": [""ModifierParams""],
                ""activePhases"": [""OnCalculate""],
                ""allowedLifetimes"": [""Instant""],
                ""defaultPhaseHandlers"": {
                  ""OnCalculate"": { ""type"": ""graph"", ""id"": ""42"" }
                }
              }
            ]";

            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, json);

            ref readonly var def = ref reg.Get(EffectPresetType.Heal);
            var h = def.DefaultPhaseHandlers[EffectPhaseId.OnCalculate];
            That(h.Kind, Is.EqualTo(PhaseHandlerKind.Graph));
            That(h.HandlerId, Is.EqualTo(42));
        }

        [Test]
        public void PresetTypeLoader_EmptyJson_DoesNotThrow()
        {
            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, "[]");
            That(reg.IsRegistered(EffectPresetType.None), Is.False);
        }

        [Test]
        public void PresetTypeLoader_UnknownPresetId_DefaultsToNone()
        {
            const string json = @"[
              { ""id"": ""FutureMagicType"", ""components"": [], ""activePhases"": [], ""allowedLifetimes"": [], ""defaultPhaseHandlers"": {} }
            ]";

            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, json);
            // Unknown type maps to None
            That(reg.IsRegistered(EffectPresetType.None), Is.True);
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectParamKeys
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectParamKeys_Initialize_AssignsDistinctNonZeroIds()
        {
            EffectParamKeys.Initialize();

            // All IDs should be > 0 (ConfigKeyRegistry.InvalidId == 0)
            That(EffectParamKeys.DurationTicks, Is.GreaterThan(0));
            That(EffectParamKeys.PeriodTicks, Is.GreaterThan(0));
            That(EffectParamKeys.ForceXAttribute, Is.GreaterThan(0));
            That(EffectParamKeys.ForceYAttribute, Is.GreaterThan(0));
            That(EffectParamKeys.QueryRadius, Is.GreaterThan(0));
            That(EffectParamKeys.PayloadEffectId, Is.GreaterThan(0));
            That(EffectParamKeys.ProjectileSpeed, Is.GreaterThan(0));
            That(EffectParamKeys.UnitTypeId, Is.GreaterThan(0));

            // ForceX and ForceY must be different
            That(EffectParamKeys.ForceXAttribute, Is.Not.EqualTo(EffectParamKeys.ForceYAttribute));
        }

        [Test]
        public void EffectParamKeys_Initialize_IsIdempotent()
        {
            EffectParamKeys.Initialize();
            int first = EffectParamKeys.ForceXAttribute;

            EffectParamKeys.Initialize();
            int second = EffectParamKeys.ForceXAttribute;

            That(first, Is.EqualTo(second), "Repeated initialization should return same IDs");
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectConfigParams.MergeFrom
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void ConfigParams_MergeFrom_CallerWinsOnConflict()
        {
            var template = new EffectConfigParams();
            template.TryAddFloat(keyId: 1, 10.0f);
            template.TryAddFloat(keyId: 2, 20.0f);
            template.TryAddInt(keyId: 3, 100);

            var caller = new EffectConfigParams();
            caller.TryAddFloat(keyId: 2, 99.0f);  // override key 2

            template.MergeFrom(in caller);

            // Key 1: unchanged
            That(template.TryGetFloat(1, out float v1), Is.True);
            That(v1, Is.EqualTo(10.0f));

            // Key 2: caller override
            That(template.TryGetFloat(2, out float v2), Is.True);
            That(v2, Is.EqualTo(99.0f));

            // Key 3: unchanged
            That(template.TryGetInt(3, out int v3), Is.True);
            That(v3, Is.EqualTo(100));
        }

        [Test]
        public unsafe void ConfigParams_MergeFrom_AddsNewKeys()
        {
            var template = new EffectConfigParams();
            template.TryAddFloat(keyId: 1, 10.0f);

            var caller = new EffectConfigParams();
            caller.TryAddFloat(keyId: 5, 50.0f); // new key

            template.MergeFrom(in caller);

            That(template.Count, Is.EqualTo(2));
            That(template.TryGetFloat(5, out float v), Is.True);
            That(v, Is.EqualTo(50.0f));
        }

        [Test]
        public unsafe void ConfigParams_MergeFrom_EmptyCaller_NoChange()
        {
            var template = new EffectConfigParams();
            template.TryAddFloat(keyId: 1, 10.0f);

            var empty = new EffectConfigParams();
            template.MergeFrom(in empty);

            That(template.Count, Is.EqualTo(1));
            That(template.TryGetFloat(1, out float v), Is.True);
            That(v, Is.EqualTo(10.0f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  ConfigParamsMerger
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ConfigParamsMerger_EntityBased_ReturnsPreMergedParams()
        {
            using var world = World.Create();

            var templateParams = new EffectConfigParams();
            templateParams.TryAddFloat(keyId: 10, 1.0f);
            templateParams.TryAddFloat(keyId: 20, 2.0f);

            // Simulate pre-merge at creation time (caller overrides key 20)
            var preMerged = templateParams;
            var callerOverrides = new EffectConfigParams();
            callerOverrides.TryAddFloat(keyId: 20, 99.0f);
            preMerged.MergeFrom(in callerOverrides);

            var entity = world.Create(preMerged);

            var merged = ConfigParamsMerger.BuildMergedConfig(world, entity, in templateParams);

            That(merged.TryGetFloat(10, out float v1), Is.True);
            That(v1, Is.EqualTo(1.0f));

            That(merged.TryGetFloat(20, out float v2), Is.True);
            That(v2, Is.EqualTo(99.0f), "Pre-merged params should contain caller override");
        }

        [Test]
        public void ConfigParamsMerger_EntityWithoutConfigParams_ReturnsTemplateOnly()
        {
            using var world = World.Create();

            var templateParams = new EffectConfigParams();
            templateParams.TryAddFloat(keyId: 10, 1.0f);

            var entity = world.Create(); // no EffectConfigParams component

            var merged = ConfigParamsMerger.BuildMergedConfig(world, entity, in templateParams);

            That(merged.TryGetFloat(10, out float v), Is.True);
            That(v, Is.EqualTo(1.0f));
            That(merged.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConfigParamsMerger_RequestBased_MergesCallerParams()
        {
            var templateParams = new EffectConfigParams();
            templateParams.TryAddFloat(keyId: 10, 1.0f);

            var req = new EffectRequest
            {
                HasCallerParams = true,
            };
            req.CallerParams.TryAddFloat(keyId: 10, 77.0f); // override

            var merged = ConfigParamsMerger.BuildMergedConfig(in templateParams, in req);

            That(merged.TryGetFloat(10, out float v), Is.True);
            That(v, Is.EqualTo(77.0f), "Request CallerParams should override template");
        }

        [Test]
        public void ConfigParamsMerger_RequestWithoutCallerParams_ReturnsTemplateOnly()
        {
            var templateParams = new EffectConfigParams();
            templateParams.TryAddFloat(keyId: 10, 1.0f);

            var req = new EffectRequest { HasCallerParams = false };

            var merged = ConfigParamsMerger.BuildMergedConfig(in templateParams, in req);

            That(merged.TryGetFloat(10, out float v), Is.True);
            That(v, Is.EqualTo(1.0f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Pre-merged EffectConfigParams on Entity
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PreMergedConfigParams_AttachAndRead_OnEntity()
        {
            using var world = World.Create();

            var merged = new EffectConfigParams();
            merged.TryAddFloat(keyId: 42, 3.14f);

            var entity = world.Create(merged);

            That(world.Has<EffectConfigParams>(entity), Is.True);
            ref readonly var comp = ref world.Get<EffectConfigParams>(entity);
            That(comp.TryGetFloat(42, out float v), Is.True);
            That(v, Is.EqualTo(3.14f).Within(1e-6f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Full preset_types.json Load Test
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeLoader_FullPresetTypesJson_LoadsAll10Types()
        {
            string json = System.IO.File.ReadAllText(
                System.IO.Path.Combine(FindRepoRoot(), "assets", "Configs", "GAS", "preset_types.json"));

            var reg = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(reg, json);

            // None (0) is not a real preset type — it should NOT be in the JSON
            That(reg.IsRegistered(EffectPresetType.None), Is.False);

            // All 11 real preset types should be registered
            That(reg.IsRegistered(EffectPresetType.ApplyForce2D), Is.True);
            That(reg.IsRegistered(EffectPresetType.InstantDamage), Is.True);
            That(reg.IsRegistered(EffectPresetType.DoT), Is.True);
            That(reg.IsRegistered(EffectPresetType.Heal), Is.True);
            That(reg.IsRegistered(EffectPresetType.HoT), Is.True);
            That(reg.IsRegistered(EffectPresetType.Buff), Is.True);
            That(reg.IsRegistered(EffectPresetType.Search), Is.True);
            That(reg.IsRegistered(EffectPresetType.PeriodicSearch), Is.True);
            That(reg.IsRegistered(EffectPresetType.LaunchProjectile), Is.True);
            That(reg.IsRegistered(EffectPresetType.CreateUnit), Is.True);
            That(reg.IsRegistered(EffectPresetType.Displacement), Is.True);

            // Spot-check ApplyForce2D builtin handler
            ref readonly var af = ref reg.Get(EffectPresetType.ApplyForce2D);
            var hApply = af.DefaultPhaseHandlers[EffectPhaseId.OnApply];
            That(hApply.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hApply.HandlerId, Is.EqualTo((int)BuiltinHandlerId.ApplyForce));

            // Spot-check PeriodicSearch — JSON only defines OnPeriod handler
            ref readonly var ps = ref reg.Get(EffectPresetType.PeriodicSearch);
            That(ps.DefaultPhaseHandlers[EffectPhaseId.OnPeriod].IsValid, Is.True);
            That(ps.DefaultPhaseHandlers[EffectPhaseId.OnPeriod].HandlerId,
                Is.EqualTo((int)BuiltinHandlerId.ReResolveAndDispatch));

            // Spot-check Displacement builtin handler
            ref readonly var disp = ref reg.Get(EffectPresetType.Displacement);
            var hDisp = disp.DefaultPhaseHandlers[EffectPhaseId.OnApply];
            That(hDisp.Kind, Is.EqualTo(PhaseHandlerKind.Builtin));
            That(hDisp.HandlerId, Is.EqualTo((int)BuiltinHandlerId.ApplyDisplacement));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helper
        // ════════════════════════════════════════════════════════════════════

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "assets")))
                    return dir;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
            throw new InvalidOperationException("Cannot find repo root (looking for assets/ directory).");
        }
    }
}
