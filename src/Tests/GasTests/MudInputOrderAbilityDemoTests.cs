using System;
using System.Diagnostics;
using System.Text;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// MUD-like demo tests + stress tests for the Input/Order/Ability audit features:
    ///   - OrderBuffer PendingBuffer E2E flow
    ///   - Toggle Ability lifecycle
    ///   - GrantedSlot override merging
    ///   - Graph-based order validation
    ///   - OrderBuffer stress test
    /// 
    /// Follows MUD-style log output: section markers, narrative lines, compact status.
    /// </summary>
    [TestFixture]
    public class MudInputOrderAbilityDemoTests
    {
        private readonly TagOps _tagOps = new TagOps();

        // ════════════════════════════════════════════════════════════════════
        // E2E: PendingBuffer — Input Buffering Scenario
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MudPendingBuffer_InputBuffering_DemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][ORDER] Input Buffering (PendingBuffer) Demo");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            var buffer = OrderBuffer.CreateEmpty();

            // Scenario: Player spams Q during another ability's execution
            var moveOrder = new Order { OrderTagId = 1, OrderId = 1 };
            var qSkillOrder = new Order { OrderTagId = 10, OrderId = 2 };
            var wSkillOrder = new Order { OrderTagId = 11, OrderId = 3 };

            // Step 1: Player issues Move order — becomes active
            buffer.SetActiveDirect(in moveOrder, priority: 0);
            sb.AppendLine("[MUD][ORDER] 玩家下达【移动】指令。");
            sb.AppendLine($"  HasActive={buffer.HasActive} ActiveTag={buffer.ActiveOrder.Order.OrderTagId} HasPending={buffer.HasPending}");

            // Step 2: During move, player presses Q — blocked, goes to pending
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][ORDER] 移动中，玩家按下【Q技能】—— 指令被缓冲。");
            buffer.SetPending(in qSkillOrder, priority: 5, expireStep: 50, insertStep: 10);
            sb.AppendLine($"  HasPending={buffer.HasPending} PendingTag={buffer.PendingOrder.Order.OrderTagId} ExpireStep={buffer.PendingOrder.ExpireStep}");

            // Step 3: Player presses W — overwrites Q in pending (last-write-wins)
            sb.AppendLine("[MUD][ORDER] 玩家又按下【W技能】—— 覆盖之前的Q缓冲。");
            buffer.SetPending(in wSkillOrder, priority: 5, expireStep: 60, insertStep: 15);
            sb.AppendLine($"  HasPending={buffer.HasPending} PendingTag={buffer.PendingOrder.Order.OrderTagId} (last-write-wins)");

            That(buffer.PendingOrder.Order.OrderTagId, Is.EqualTo(11), "W should overwrite Q");

            // Step 4: Move completes — pending should be ready to submit
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][ORDER] 移动完成，系统尝试提交缓冲的W技能。");
            buffer.ClearActive();
            That(buffer.HasActive, Is.False);
            That(buffer.HasPending, Is.True);
            var pendingOrd = buffer.PendingOrder.Order;
            buffer.ClearPending();
            buffer.SetActiveDirect(in pendingOrd, priority: 5);
            sb.AppendLine($"  ActiveTag={buffer.ActiveOrder.Order.OrderTagId} HasPending={buffer.HasPending}");

            That(buffer.ActiveOrder.Order.OrderTagId, Is.EqualTo(11), "W should now be active");
            That(buffer.HasPending, Is.False);

            // Step 5: Test pending expiration
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            buffer.ClearActive();
            buffer.SetPending(in qSkillOrder, priority: 5, expireStep: 20, insertStep: 0);
            sb.AppendLine("[MUD][ORDER] 新的Q缓冲（expireStep=20），模拟超时。");
            bool expired = buffer.ExpirePending(currentStep: 25);
            sb.AppendLine($"  Step=25 → Expired={expired} HasPending={buffer.HasPending}");
            That(expired, Is.True);

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][ORDER] Input Buffering 演示完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());
            Pass("PendingBuffer E2E demo complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // E2E: Toggle Ability — Activate/Deactivate Lifecycle
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MudToggleAbility_ActivateDeactivate_DemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][TOGGLE] Toggle Ability Lifecycle Demo");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            int toggleTagId = 100;
            int effectTemplateId1 = 201;
            int effectTemplateId2 = 202;

            var registry = new AbilityDefinitionRegistry();

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = toggleTagId,
                ActiveEffectCount = 2
            };
            unsafe
            {
                toggleSpec.ActiveEffectTemplateIds[0] = effectTemplateId1;
                toggleSpec.ActiveEffectTemplateIds[1] = effectTemplateId2;
            }

            var def = new AbilityDefinition
            {
                HasToggleSpec = true,
                ToggleSpec = toggleSpec
            };

            registry.Register(abilityId: 1, in def);

            sb.AppendLine("[MUD][TOGGLE] 注册开关技能：ID=1, ToggleTag=100, Effects=[201,202]");

            // Simulate toggle activation
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][TOGGLE] 玩家按下 E —— 激活开关技能。");

            That(registry.TryGet(1, out var retrieved), Is.True);
            That(retrieved.HasToggleSpec, Is.True);

            // Simulate checking if toggle tag is active on actor
            using var world = World.Create();
            var actor = world.Create(new GameplayTagContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(actor);

            sb.AppendLine($"  激活前: HasToggleTag={tags.HasTag(toggleTagId)}");
            That(tags.HasTag(toggleTagId), Is.False);

            // Activate: grant toggle tag
            tags.AddTag(toggleTagId);
            sb.AppendLine($"  激活后: HasToggleTag={tags.HasTag(toggleTagId)}");
            sb.AppendLine($"  → 附加持续效果: TemplateId={effectTemplateId1}, {effectTemplateId2}");
            That(tags.HasTag(toggleTagId), Is.True);

            // Deactivate: remove toggle tag
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][TOGGLE] 玩家再次按下 E —— 关闭开关技能。");
            tags.RemoveTag(toggleTagId);
            sb.AppendLine($"  关闭后: HasToggleTag={tags.HasTag(toggleTagId)}");
            sb.AppendLine("  → 移除持续效果，执行 DeactivateExecSpec（如有）。");
            That(tags.HasTag(toggleTagId), Is.False);

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][TOGGLE] Toggle Ability 演示完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());
            Pass("Toggle ability E2E demo complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // E2E: GrantedSlot — Item/Buff Override Scenario
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MudGrantedSlot_ItemOverride_DemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][SLOT] GrantedSlot Item Override Demo");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            var baseSlots = new AbilityStateBuffer();
            baseSlots.AddAbility(10); // slot 0: Fireball
            baseSlots.AddAbility(20); // slot 1: IceBlast
            baseSlots.AddAbility(30); // slot 2: HealingWave
            baseSlots.AddAbility(40); // slot 3: Teleport

            sb.AppendLine("[MUD][SLOT] 基础技能栏: [Fireball(10), IceBlast(20), HealingWave(30), Teleport(40)]");

            var granted = new GrantedSlotBuffer();

            // Item grants an override to slot 1
            int itemBuffTag = 500;
            granted.Grant(1, abilityId: 99, sourceTagId: itemBuffTag);
            sb.AppendLine("[MUD][SLOT] 装备【地狱火杖】—— 槽位1替换为 InfernoBlast(99)。");

            for (int i = 0; i < 4; i++)
            {
                var resolved = AbilitySlotResolver.Resolve(in baseSlots, in granted, hasGranted: true, i);
                sb.AppendLine($"  Slot[{i}] = AbilityId={resolved.AbilityId}{(granted.HasOverride(i) ? " (OVERRIDE)" : "")}");
            }

            That(AbilitySlotResolver.Resolve(in baseSlots, in granted, true, 1).AbilityId, Is.EqualTo(99));
            That(AbilitySlotResolver.Resolve(in baseSlots, in granted, true, 0).AbilityId, Is.EqualTo(10));

            // Buff expires — revoke by source
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][SLOT] 【地狱火杖】buff过期 —— 恢复原始技能。");
            int revoked = granted.RevokeBySource(itemBuffTag);
            sb.AppendLine($"  Revoked={revoked} slots");

            var resolvedAfter = AbilitySlotResolver.Resolve(in baseSlots, in granted, true, 1);
            sb.AppendLine($"  Slot[1] = AbilityId={resolvedAfter.AbilityId} (restored)");
            That(resolvedAfter.AbilityId, Is.EqualTo(20), "Should be restored to IceBlast");

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][SLOT] GrantedSlot 演示完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());
            Pass("GrantedSlot E2E demo complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // E2E: Graph Validation — Order Rejected by Validation Graph
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MudGraphValidation_RejectAndPass_DemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][VALID] Graph-based Order Validation Demo");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Case 1: Empty program — default pass
            sb.AppendLine("[MUD][VALID] Case 1: 空校验图 → 默认通过 (B[0]=1)。");
            bool result1 = GasGraphExecutor.ExecuteValidation(
                world, caster, target, default,
                ReadOnlySpan<GraphInstruction>.Empty, null!);
            sb.AppendLine($"  Result={result1}");
            That(result1, Is.True);

            // Case 2: Reject program — SetBool B[0] = 0
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][VALID] Case 2: 校验图设置 B[0]=0 → 拒绝。");
            var reject = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 };
            bool result2 = GasGraphExecutor.ExecuteValidation(
                world, caster, target, default,
                new[] { reject }, null!);
            sb.AppendLine($"  Result={result2}");
            That(result2, Is.False);

            // Case 3: Pass program — SetBool B[0] = 1 (explicit pass)
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("[MUD][VALID] Case 3: 校验图显式设置 B[0]=1 → 通过。");
            var passInstr = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 };
            bool result3 = GasGraphExecutor.ExecuteValidation(
                world, caster, target, default,
                new[] { passInstr }, null!);
            sb.AppendLine($"  Result={result3}");
            That(result3, Is.True);

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][VALID] Graph Validation 演示完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());
            Pass("Graph validation E2E demo complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // STRESS: OrderBuffer — High-throughput Enqueue/Dequeue/Pending
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void StressTest_OrderBuffer_HighThroughput()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] OrderBuffer High-Throughput Stress Test");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            const int iterations = 100_000;
            const int entitiesPerBatch = 100;
            int totalEnqueues = 0;
            int totalPromotes = 0;
            int totalPendingSets = 0;
            int totalPendingExpires = 0;

            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            int gen0_0 = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();

            var buffers = new OrderBuffer[entitiesPerBatch];
            for (int i = 0; i < entitiesPerBatch; i++)
            {
                buffers[i] = OrderBuffer.CreateEmpty();
            }

            for (int frame = 0; frame < iterations; frame++)
            {
                int entityIdx = frame % entitiesPerBatch;
                ref var buffer = ref buffers[entityIdx];

                // Enqueue
                var order = new Order { OrderTagId = frame % 20, OrderId = frame };
                if (buffer.Enqueue(in order, priority: frame % 5, expireStep: frame + 100, insertStep: frame))
                {
                    totalEnqueues++;
                }

                // Promote if no active
                if (!buffer.HasActive && buffer.HasQueued)
                {
                    buffer.PromoteNext();
                    totalPromotes++;
                }

                // Every 3rd frame: complete active and check pending
                if (frame % 3 == 0 && buffer.HasActive)
                {
                    buffer.ClearActive();

                    if (buffer.HasPending)
                    {
                        var pending = buffer.PendingOrder.Order;
                        buffer.ClearPending();
                        buffer.SetActiveDirect(in pending, 5);
                        totalPendingSets++;
                    }
                }

                // Every 7th frame: set pending
                if (frame % 7 == 0)
                {
                    buffer.SetPending(in order, 5, expireStep: frame + 10, insertStep: frame);
                    totalPendingSets++;
                }

                // Every 5th frame: expire pending
                if (frame % 5 == 0 && buffer.ExpirePending(frame))
                {
                    totalPendingExpires++;
                }

                // Every 10th frame: remove expired
                if (frame % 10 == 0)
                {
                    buffer.RemoveExpired(frame);
                }
            }

            sw.Stop();
            long alloc1 = GC.GetAllocatedBytesForCurrentThread();
            int gen0_1 = GC.CollectionCount(0);

            sb.AppendLine($"[MUD][STRESS] Iterations={iterations} Entities={entitiesPerBatch}");
            sb.AppendLine($"[MUD][STRESS] Enqueues={totalEnqueues} Promotes={totalPromotes} PendingSets={totalPendingSets} PendingExpires={totalPendingExpires}");
            sb.AppendLine($"[MUD][STRESS] ElapsedMs={sw.Elapsed.TotalMilliseconds:F2} PerIterationNs={(sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations):F1}");
            sb.AppendLine($"[MUD][STRESS] AllocBytes={alloc1 - alloc0} GC.Gen0Δ={gen0_1 - gen0_0}");

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] OrderBuffer 压力测试完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());

            // Sanity checks
            That(totalEnqueues, Is.GreaterThan(0));
            That(totalPromotes, Is.GreaterThan(0));
            That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5000), "Should complete within 5 seconds");

            Pass("OrderBuffer stress test complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // STRESS: GrantedSlotBuffer — Rapid Grant/Revoke Cycles
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void StressTest_GrantedSlotBuffer_RapidCycles()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] GrantedSlotBuffer Rapid Grant/Revoke Stress Test");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            const int cycles = 500_000;
            int totalGrants = 0;
            int totalRevokes = 0;
            int totalResolves = 0;

            var baseSlots = new AbilityStateBuffer();
            for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
            {
                baseSlots.AddAbility((i + 1) * 10);
            }

            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            var granted = new GrantedSlotBuffer();

            for (int c = 0; c < cycles; c++)
            {
                int slot = c % GrantedSlotBuffer.CAPACITY;
                int sourceTag = (c / GrantedSlotBuffer.CAPACITY) % 100 + 1;

                // Grant
                granted.Grant(slot, abilityId: 1000 + c % 50, sourceTagId: sourceTag);
                totalGrants++;

                // Resolve
                var resolved = AbilitySlotResolver.Resolve(in baseSlots, in granted, true, slot);
                totalResolves++;

                // Every 3rd cycle: revoke
                if (c % 3 == 0)
                {
                    granted.Revoke(slot);
                    totalRevokes++;
                }

                // Every 100th cycle: revoke by source
                if (c % 100 == 0)
                {
                    totalRevokes += granted.RevokeBySource(sourceTag);
                }
            }

            sw.Stop();
            long alloc1 = GC.GetAllocatedBytesForCurrentThread();

            sb.AppendLine($"[MUD][STRESS] Cycles={cycles} Grants={totalGrants} Revokes={totalRevokes} Resolves={totalResolves}");
            sb.AppendLine($"[MUD][STRESS] ElapsedMs={sw.Elapsed.TotalMilliseconds:F2} PerCycleNs={(sw.Elapsed.TotalMilliseconds * 1_000_000.0 / cycles):F1}");
            sb.AppendLine($"[MUD][STRESS] AllocBytes={alloc1 - alloc0}");

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] GrantedSlotBuffer 压力测试完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());

            That(totalGrants, Is.EqualTo(cycles));
            That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5000));

            Pass("GrantedSlotBuffer stress test complete");
        }

        // ════════════════════════════════════════════════════════════════════
        // STRESS: GraphExecutor.ExecuteValidation — Throughput
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void StressTest_GraphValidation_Throughput()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] GraphExecutor.ExecuteValidation Throughput Test");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Small validation program: SetBool B[0] = 1 (pass)
            var passInstruction = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 };
            var program = new[] { passInstruction };

            const int iterations = 100_000;
            int passed = 0;

            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                if (GasGraphExecutor.ExecuteValidation(world, caster, target, default, program, null!))
                {
                    passed++;
                }
            }

            sw.Stop();
            long alloc1 = GC.GetAllocatedBytesForCurrentThread();

            sb.AppendLine($"[MUD][STRESS] Iterations={iterations} Passed={passed}");
            sb.AppendLine($"[MUD][STRESS] ElapsedMs={sw.Elapsed.TotalMilliseconds:F2} PerValidationNs={(sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations):F1}");
            sb.AppendLine($"[MUD][STRESS] AllocBytes={alloc1 - alloc0}");

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("[MUD][STRESS] GraphValidation 压力测试完成。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            Console.WriteLine(sb.ToString());

            That(passed, Is.EqualTo(iterations));
            That(sw.Elapsed.TotalMilliseconds, Is.LessThan(10000));

            Pass("GraphValidation stress test complete");
        }
    }
}
