using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Performance and stress tests for the new Preset Type System.
    /// Verifies 0GC constraints and throughput of hot-path operations.
    /// </summary>
    [TestFixture]
    public class PresetTypePerformanceTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  ConfigParams MergeFrom — 0GC Verification
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ConfigParamsMerger_BuildMergedConfig_NoAllocation()
        {
            using var world = World.Create();

            var templateParams = new EffectConfigParams();
            for (int i = 0; i < 8; i++)
                templateParams.TryAddFloat(i + 1, i * 1.5f);

            // Pre-merge caller overrides into template params (mimics creation-time merge)
            var preMerged = templateParams;
            var callerOverrides = new EffectConfigParams();
            callerOverrides.TryAddFloat(3, 999.0f);
            callerOverrides.TryAddFloat(5, 888.0f);
            preMerged.MergeFrom(in callerOverrides);

            var entity = world.Create(preMerged);

            // Warm up
            for (int i = 0; i < 100; i++)
                ConfigParamsMerger.BuildMergedConfig(world, entity, in templateParams);

            long before = GC.GetAllocatedBytesForCurrentThread();
            const int iterations = 10000;
            for (int i = 0; i < iterations; i++)
            {
                var merged = ConfigParamsMerger.BuildMergedConfig(world, entity, in templateParams);
                // Prevent dead code elimination
                if (merged.Count == -1) throw new Exception("unreachable");
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            long allocated = after - before;

            TestContext.Out.WriteLine($"[ConfigParamsMerger] {iterations} merges: {allocated} bytes allocated");
            That(allocated, Is.LessThanOrEqualTo(1024),
                $"ConfigParamsMerger should be 0GC on hot path, allocated {allocated} bytes in {iterations} iterations");
        }

        // ════════════════════════════════════════════════════════════════════
        //  PresetTypeRegistry Lookup — Throughput
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeRegistry_Lookup_HighThroughput()
        {
            var reg = new PresetTypeRegistry();
            // Register all 10 types
            for (int i = 0; i <= 10; i++)
            {
                var def = new PresetTypeDefinition
                {
                    Type = (EffectPresetType)i,
                    Components = (ComponentFlags)(1 << (i % 8)),
                    ActivePhases = PhaseFlags.InstantCore,
                    AllowedLifetimes = LifetimeFlags.All,
                };
                reg.Register(in def);
            }

            // Warm up
            for (int i = 0; i < 1000; i++)
                reg.Get((EffectPresetType)(i % 11));

            var sw = Stopwatch.StartNew();
            const int iterations = 1_000_000;
            int checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                ref readonly var def = ref reg.Get((EffectPresetType)(i % 11));
                checksum += (int)def.Components;
            }
            sw.Stop();

            double nsPerLookup = sw.Elapsed.TotalMilliseconds * 1_000_000 / iterations;
            TestContext.Out.WriteLine($"[PresetTypeRegistry] {iterations:N0} lookups in {sw.ElapsedMilliseconds}ms ({nsPerLookup:F1} ns/lookup, checksum={checksum})");
            That(sw.ElapsedMilliseconds, Is.LessThan(500),
                "1M lookups should complete in < 500ms");
        }

        // ════════════════════════════════════════════════════════════════════
        //  BuiltinHandlerRegistry Dispatch — Throughput
        // ════════════════════════════════════════════════════════════════════

        private static int _perfCounter;

        private static void CountingHandler(World w, Entity e, ref EffectContext ctx, in EffectConfigParams p, in EffectTemplateData t)
        {
            _perfCounter++;
        }

        [Test]
        public void BuiltinHandlerRegistry_Dispatch_HighThroughput()
        {
            var reg = new BuiltinHandlerRegistry();
            _perfCounter = 0;
            reg.Register(BuiltinHandlerId.ApplyModifiers, CountingHandler);

            using var world = World.Create();
            var entity = world.Create();
            var ctx = new EffectContext();
            var param = new EffectConfigParams();
            var tpl = new EffectTemplateData();

            // Warm up
            for (int i = 0; i < 1000; i++)
                reg.Invoke(BuiltinHandlerId.ApplyModifiers, world, entity, ref ctx, in param, in tpl);
            _perfCounter = 0;

            var sw = Stopwatch.StartNew();
            const int iterations = 1_000_000;
            for (int i = 0; i < iterations; i++)
            {
                reg.Invoke(BuiltinHandlerId.ApplyModifiers, world, entity, ref ctx, in param, in tpl);
            }
            sw.Stop();

            double nsPerCall = sw.Elapsed.TotalMilliseconds * 1_000_000 / iterations;
            TestContext.Out.WriteLine($"[BuiltinHandlerRegistry] {iterations:N0} dispatches in {sw.ElapsedMilliseconds}ms ({nsPerCall:F1} ns/dispatch)");
            That(_perfCounter, Is.EqualTo(iterations));
            That(sw.ElapsedMilliseconds, Is.LessThan(1000),
                "1M dispatches should complete in < 1s");
        }

        // ════════════════════════════════════════════════════════════════════
        //  PhaseHandlerMap Access — 0GC Verification
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void PhaseHandlerMap_Access_NoAllocation()
        {
            var map = new PhaseHandlerMap();
            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                map[(EffectPhaseId)i] = PhaseHandler.Builtin((BuiltinHandlerId)(i + 1));
            }

            // Warm up
            for (int i = 0; i < 100; i++)
            {
                var _ = map[(EffectPhaseId)(i % EffectPhaseConstants.PhaseCount)];
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            const int iterations = 100_000;
            int checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                var h = map[(EffectPhaseId)(i % EffectPhaseConstants.PhaseCount)];
                checksum += h.HandlerId;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            long allocated = after - before;

            TestContext.Out.WriteLine($"[PhaseHandlerMap] {iterations:N0} accesses: {allocated} bytes allocated, checksum={checksum}");
            That(allocated, Is.LessThanOrEqualTo(512),
                $"PhaseHandlerMap should be 0GC, allocated {allocated} bytes");
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectConfigParams MergeFrom — Stress with Full Capacity
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void ConfigParams_MergeFrom_StressFullCapacity()
        {
            var template = new EffectConfigParams();
            for (int i = 0; i < EffectConfigParams.MAX_PARAMS; i++)
            {
                template.TryAddFloat(i + 1, i * 0.1f);
            }
            That(template.Count, Is.EqualTo(EffectConfigParams.MAX_PARAMS));

            // Caller overrides every other key
            var caller = new EffectConfigParams();
            for (int i = 0; i < EffectConfigParams.MAX_PARAMS; i += 2)
            {
                caller.TryAddFloat(i + 1, 999.0f);
            }

            template.MergeFrom(in caller);

            // Verify overrides
            for (int i = 0; i < EffectConfigParams.MAX_PARAMS; i++)
            {
                template.TryGetFloat(i + 1, out float v);
                if (i % 2 == 0)
                    That(v, Is.EqualTo(999.0f), $"Key {i + 1} should be overridden");
                else
                    That(v, Is.EqualTo(i * 0.1f).Within(1e-5f), $"Key {i + 1} should keep original");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectRequest CallerParams — Batch Stress
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectRequestQueue_CallerParams_BatchStress()
        {
            EffectParamKeys.Initialize();
            var queue = new EffectRequestQueue();

            const int batchSize = 500;
            for (int i = 0; i < batchSize; i++)
            {
                var req = new EffectRequest
                {
                    TemplateId = i,
                    HasCallerParams = true,
                };
                req.CallerParams.TryAddFloat(EffectParamKeys.ForceXAttribute, i * 1.0f);
                req.CallerParams.TryAddFloat(EffectParamKeys.ForceYAttribute, i * -1.0f);
                queue.Publish(req);
            }

            That(queue.Count, Is.EqualTo(batchSize));

            // Verify all preserved
            for (int i = 0; i < batchSize; i++)
            {
                var req = queue[i];
                That(req.TemplateId, Is.EqualTo(i));
                That(req.HasCallerParams, Is.True);

                req.CallerParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx);
                req.CallerParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy);
                That(fx, Is.EqualTo(i * 1.0f).Within(1e-5f));
                That(fy, Is.EqualTo(i * -1.0f).Within(1e-5f));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PresetTypeLoader — JSON Parse Performance
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PresetTypeLoader_ParsePerformance()
        {
            string json = System.IO.File.ReadAllText(
                System.IO.Path.Combine(FindRepoRoot(), "assets", "Configs", "GAS", "preset_types.json"));

            // Warm up
            for (int i = 0; i < 10; i++)
            {
                var reg = new PresetTypeRegistry();
                PresetTypeLoader.Load(reg, json);
            }

            var sw = Stopwatch.StartNew();
            const int iterations = 1000;
            for (int i = 0; i < iterations; i++)
            {
                var reg = new PresetTypeRegistry();
                PresetTypeLoader.Load(reg, json);
            }
            sw.Stop();

            double msPerLoad = sw.Elapsed.TotalMilliseconds / iterations;
            TestContext.Out.WriteLine($"[PresetTypeLoader] {iterations} loads in {sw.ElapsedMilliseconds}ms ({msPerLoad:F3} ms/load)");
            That(sw.ElapsedMilliseconds, Is.LessThan(5000),
                "1000 loads of preset_types.json should complete in < 5s");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
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
            throw new InvalidOperationException("Cannot find repo root.");
        }
    }
}
