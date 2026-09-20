using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    public sealed partial class TriggerGraphResumeTests
    {
        [Test]
        public void Slots_ExhaustionReportsErrorAndCompletedRunMakesRoom()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = new TriggerGraphExecutionSlotStore(1);
            engine.SetService(CoreServiceKeys.TriggerGraphExecutionSlots, slots);
            var entry = new TriggerGraphEntry("wait", EntryEventName, 0, false);
            int graphId = fixture.RegisterTriggerGraph(engine, YieldThenReturn(), new[] { entry });
            var first = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var second = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var context = engine.CreateContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 41);
            first.ExecuteAsync(context);
            engine.TriggerManager.RegisterTrigger(second);
            engine.TriggerManager.FireEvent(new EventKey(EntryEventName), context);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(1));
            Assert.That(engine.TriggerManager.Errors[0].Exception.Message, Does.Contain("CapacityExceeded"));
            Assert.That(second.IsSuspended, Is.False);
            Assert.That(first.IsSuspended, Is.True);
            new TriggerGraphResumeTrigger(first).ExecuteAsync(context);
            Assert.That(first.LastSliceResult.ReturnInt, Is.EqualTo(41));
            Assert.That(slots.InUseCount, Is.Zero);
            context.Set(MapTriggerEventPayloadKeys.Count, 99);
            second.ExecuteAsync(context);
            new TriggerGraphResumeTrigger(second).ExecuteAsync(context);
            Assert.That(second.LastSliceResult.ReturnInt, Is.EqualTo(99));
            Assert.That(slots.InUseCount, Is.Zero);
        }

        [TestCase("map")]
        [TestCase("global")]
        [TestCase("mod")]
        [TestCase("remove")]
        [TestCase("owned")]
        [TestCase("ability")]
        public void Slots_UnregistrationCancelsWaitAndReleasesTrace(string route)
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = engine.GetService(CoreServiceKeys.TriggerGraphExecutionSlots);
            var entry = new TriggerGraphEntry("wait", EntryEventName, 0, false);
            int symbol = RegisterCallbackSymbol();
            int graphId = fixture.RegisterTriggerGraph(engine, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.AwaitCallback, Imm = symbol, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            }, new[] { entry });
            Entity subject = route == "owned" ? engine.World.Create() : Entity.Null;
            var mount = new TriggerGraphMountTrigger(graphId, GraphName, entry, subject,
                domain: route == "owned" ? TriggerGraphMountDomain.Entity : route == "ability" ? TriggerGraphMountDomain.Ability :
                    route == "mod" ? TriggerGraphMountDomain.Mod : TriggerGraphMountDomain.Map,
                abilityIdFilter: route == "ability" ? 1 : 0,
                modIdFilter: route == "mod" ? "SlotFixture" : null,
                subscriptionScope: route == "global" ? EventScope.Global : EventScope.Map);
            var mapId = new MapId(MapId);
            if (route == "owned")
                mount.Owner = new TriggerMountOwner(TriggerMountOwnerKind.TemplateEntity, subject, 0);
            Trigger[] triggers = { mount, new TriggerGraphResumeTrigger(mount) };
            if (route == "global") engine.TriggerManager.RegisterGlobalTriggers(mapId, triggers);
            else if (route == "mod") engine.TriggerManager.RegisterModTriggers("SlotFixture", triggers);
            else engine.TriggerManager.RegisterMapTriggers(mapId, triggers);
            var context = engine.CreateContext();
            if (route == "mod") context.Set(MapTriggerEventPayloadKeys.ModId, "SlotFixture");
            if (route == "ability") context.Set(MapTriggerEventPayloadKeys.AbilityId, 1);
            mount.DebugTrace.Configure(GraphDebugTraceMode.NodeAndPins);
            mount.ExecuteAsync(context);
            var callbacks = engine.GetService(CoreServiceKeys.GraphCallbackService);
            Assert.That(callbacks.TryGetLiveHandleForTarget(mount, out int handle), Is.True);
            Assert.That(mount.IsAwaitingCallback, Is.True);
            Assert.That(slots.InUseCount, Is.EqualTo(1));
            callbacks.Complete(handle, true);
            if (route == "global") engine.TriggerManager.UnregisterGlobalTriggers(mapId);
            else if (route == "mod") engine.TriggerManager.UnregisterModTriggers("SlotFixture");
            else if (route == "remove") engine.TriggerManager.RemoveMapTriggers(mapId, triggers);
            else if (route == "owned") engine.TriggerManager.RemoveOwnedMounts(mount.Owner);
            else engine.TriggerManager.UnregisterMapTriggers(mapId, context);
            Assert.That(slots.InUseCount, Is.Zero);
            Assert.That(mount.IsCallbackResumeAlive, Is.False);
            Assert.That(mount.DebugTrace.AllocatedCapacity, Is.Zero);
            Assert.That(callbacks.HasLiveWaiterForTarget(mount), Is.False);
            callbacks.Drain();
            Assert.That(mount.IsSuspended, Is.False);
            Assert.That(Assert.Throws<InvalidOperationException>(() => mount.ExecuteAsync(context))!.Message,
                Does.Contain("UnregisteredMount"));
        }

        [Test]
        public void Slots_ReplacingMapRegistrationReleasesPreviousRun()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = new TriggerGraphExecutionSlotStore(1);
            engine.SetService(CoreServiceKeys.TriggerGraphExecutionSlots, slots);
            var entry = new TriggerGraphEntry("wait", EntryEventName, 0, false);
            int graphId = fixture.RegisterTriggerGraph(engine, YieldThenReturn(), new[] { entry });
            var old = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var replacement = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var mapId = new MapId(MapId);
            engine.TriggerManager.RegisterMapTriggers(mapId, new Trigger[] { old });
            var context = engine.CreateContext();
            old.ExecuteAsync(context);
            Assert.That(slots.InUseCount, Is.EqualTo(1));
            engine.TriggerManager.RegisterMapTriggers(mapId, new Trigger[] { replacement });
            Assert.That(slots.InUseCount, Is.Zero);
            Assert.That(old.IsSuspended, Is.False);
            replacement.ExecuteAsync(context);
            new TriggerGraphResumeTrigger(replacement).ExecuteAsync(context);
            Assert.That(slots.InUseCount, Is.Zero);
        }

        [Test]
        public void Slots_RestartInvalidatesOldCallbackAndRetainsNewInput()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = new TriggerGraphExecutionSlotStore(1);
            engine.SetService(CoreServiceKeys.TriggerGraphExecutionSlots, slots);
            var entry = new TriggerGraphEntry("wait", EntryEventName, 0, false);
            int symbol = RegisterCallbackSymbol();
            int graphId = fixture.RegisterTriggerGraph(engine, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.AwaitCallback, Imm = symbol, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            }, new[] { entry });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null, TriggerGraphRefirePolicy.Restart);
            var context = engine.CreateContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 41);
            mount.ExecuteAsync(context);
            var callbacks = engine.GetService(CoreServiceKeys.GraphCallbackService);
            Assert.That(callbacks.TryGetLiveHandleForTarget(mount, out int oldHandle), Is.True);
            callbacks.Complete(oldHandle, true);
            context.Set(MapTriggerEventPayloadKeys.Count, 99);
            mount.ExecuteAsync(context);
            Assert.That(callbacks.TryGetLiveHandleForTarget(mount, out int newHandle), Is.True);
            Assert.That(newHandle, Is.Not.EqualTo(oldHandle));
            callbacks.Drain();
            Assert.That(mount.IsAwaitingCallback, Is.True);
            Assert.That(slots.InUseCount, Is.EqualTo(1));
            callbacks.Complete(newHandle, false);
            callbacks.Drain();
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(99));
            Assert.That(slots.InUseCount, Is.Zero);
        }

        [Test]
        public void Slots_InstructionCapFailureReleasesSuspendedRun()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = engine.GetService(CoreServiceKeys.TriggerGraphExecutionSlots);
            var entry = new TriggerGraphEntry("spin", EntryEventName, 0, false);
            int graphId = fixture.RegisterTriggerGraph(engine, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, new[] { entry });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var context = engine.CreateContext();
            mount.ExecuteAsync(context);
            var resume = new TriggerGraphResumeTrigger(mount);
            Assert.Throws<InvalidOperationException>(() =>
            {
                for (int i = 0; i < GraphVmLimits.MaxInstructionsPerExecution; i++) resume.ExecuteAsync(context);
            });
            Assert.That(slots.InUseCount, Is.Zero);
            Assert.That(mount.IsSuspended, Is.False);
        }

        [Test]
        public void Slots_TenThousandMounts_StartAndResumeWithoutSteadyStateAllocation()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            const int count = 10000;
            var slots = new TriggerGraphExecutionSlotStore(count);
            engine.SetService(CoreServiceKeys.TriggerGraphExecutionSlots, slots);
            var entry = new TriggerGraphEntry("wait", EntryEventName, 0, false);
            int graphId = fixture.RegisterTriggerGraph(engine, YieldThenReturn(), new[] { entry });
            var mounts = new TriggerGraphMountTrigger[count];
            var resumes = new TriggerGraphResumeTrigger[count];
            var context = engine.CreateContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 4242);
            long beforeCreation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                mounts[i] = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
                resumes[i] = new TriggerGraphResumeTrigger(mounts[i]);
            }
            long creationBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCreation;
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < count; i++) mounts[i].ExecuteAsync(context);
                for (int i = 0; i < count; i++) resumes[i].ExecuteAsync(context);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) mounts[i].ExecuteAsync(context);
            long suspended = Stopwatch.GetTimestamp();
            int inUseAtYield = slots.InUseCount;
            for (int i = 0; i < count; i++) resumes[i].ExecuteAsync(context);
            long ended = Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
            Assert.That(inUseAtYield, Is.EqualTo(count));
            Assert.That(slots.InUseCount, Is.Zero);
            Assert.That(slots.HighWaterMark, Is.EqualTo(count));
            foreach (var mount in mounts)
            {
                Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(4242));
                Assert.That(mount.DebugTrace.AllocatedCapacity, Is.Zero);
            }
            TestContext.Out.WriteLine($"mounts={count}; entryMs={Stopwatch.GetElapsedTime(started, suspended).TotalMilliseconds:F3}; resumeMs={Stopwatch.GetElapsedTime(suspended, ended).TotalMilliseconds:F3}; allocatedBytes={bytes}; mountAndCompanionCreationBytes={creationBytes}; slotsAtYield={inUseAtYield}; slotsAfterHalt={slots.InUseCount}");
        }

        [Test]
        public void Slots_CapturedPayloadSurvivesContextChangesAndOtherRuns()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var entry = new TriggerGraphEntry("count", GameEvents.EntityAliveCountChanged.Value, 0, false);
            int symbol = RegisterSymbol(MapTriggerEventPayloadKeys.Count);
            int graphId = fixture.RegisterTriggerGraph(engine, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Yield },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadEntryPayloadInt, Imm = symbol, Dst = 5 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 5 },
            }, new[] { entry });
            var first = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var second = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var context = engine.CreateContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 41);
            first.ExecuteAsync(context);
            context.Set(MapTriggerEventPayloadKeys.Count, 99);
            second.ExecuteAsync(context);
            context.Set(MapTriggerEventPayloadKeys.Count, 0);
            new TriggerGraphResumeTrigger(first).ExecuteAsync(context);
            new TriggerGraphResumeTrigger(second).ExecuteAsync(context);
            Assert.That(first.LastSliceResult.ReturnInt, Is.EqualTo(41));
            Assert.That(second.LastSliceResult.ReturnInt, Is.EqualTo(99));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Slots_DispatchedEventPreservesCallerDuringUnregisterOrNestedRun(bool unregister, bool nestedYields)
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            var slots = new TriggerGraphExecutionSlotStore(unregister ? 1 : 2);
            engine.SetService(CoreServiceKeys.TriggerGraphExecutionSlots, slots);
            const string eventName = "Slots.Dispatch.Probe";
            int symbol = RegisterSymbol(eventName);
            engine.TriggerManager.EventSchemas!.RegisterCustom(new EventSchema(eventName, EventScope.Global, Array.Empty<EventParamSchema>()));
            var entry = new TriggerGraphEntry("fire", EntryEventName, 0, false);
            int graphId = fixture.RegisterTriggerGraph(engine, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.DispatchMapEvent, Imm = symbol, Flags = 2 },
                new GraphInstruction { Op = (ushort)(nestedYields ? GraphNodeOp.Yield : GraphNodeOp.ConstInt), Dst = 5, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            }, new[] { entry });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName, entry, Entity.Null);
            var context = engine.CreateContext();
            context.Set(MapTriggerEventPayloadKeys.Count, 41);
            bool visited = false;
            engine.TriggerManager.RegisterEventHandler(new EventKey(eventName), _ =>
            {
                if (visited) return Task.CompletedTask;
                visited = true;
                if (unregister)
                {
                    engine.TriggerManager.UnregisterTrigger(mount);
                    Assert.That(slots.InUseCount, Is.EqualTo(1));
                    Assert.Throws<InvalidOperationException>(() => slots.Rent());
                }
                else
                {
                    context.Set(MapTriggerEventPayloadKeys.Count, 99);
                    if (nestedYields)
                        Assert.That(Assert.Throws<InvalidOperationException>(() => mount.ExecuteAsync(context))!.Message,
                            Does.Contain("NestedRunSuspended"));
                    else
                        mount.ExecuteAsync(context);
                    Assert.That(slots.InUseCount, Is.EqualTo(1));
                    Assert.That(slots.HighWaterMark, Is.EqualTo(2));
                }
                return Task.CompletedTask;
            });
            mount.ExecuteAsync(context);
            if (nestedYields) new TriggerGraphResumeTrigger(mount).ExecuteAsync(context);
            Assert.That(visited, Is.True);
            Assert.That(engine.TriggerManager.Errors, Is.Empty);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(41));
            Assert.That(slots.InUseCount, Is.Zero);
        }

        private static int RegisterCallbackSymbol() => RegisterSymbol(GraphCallbackTypes.DialogConfirm);

        private static int RegisterSymbol(string name)
        {
            var mappings = ConfigKeyRegistry.SnapshotMappings();
            ConfigKeyRegistry.Clear();
            Array.Sort(mappings, (a, b) => a.Id.CompareTo(b.Id));
            foreach (var mapping in mappings) ConfigKeyRegistry.Register(mapping.Name);
            return ConfigKeyRegistry.Register(name);
        }

        private static GraphInstruction[] YieldThenReturn() => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.Yield },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
        };
    }
}
