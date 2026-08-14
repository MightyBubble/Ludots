using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Arch.Core;
using Arch.System;
using GraphAiShowcaseCommon;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[NonParallelizable]
[Category("benchmark")]
public sealed class GraphAiFsmBtBenchmarkTests
{
    private const int EntityCount = 50_000;
    private const int WarmupTicks = 16;
    private const int MeasuredTicks = 45;

    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<GraphAiBrain, GraphAiHotIntent>();

    [Test]
    public void Benchmark_GraphAi_50kEntities_FsmAndBt_SoaZeroAllocAfterWarmup()
    {
        GraphInstruction[] fsmProgram = LoadProgram(
            "mods/showcases/graph_stance_fsm/GraphStanceFsmShowcaseMod",
            "rts_stance_fsm");
        GraphInstruction[] btProgram = LoadProgram(
            "mods/showcases/graph_complex_bt/GraphComplexBtShowcaseMod",
            "complex_bt_selector");

        Assert.That(fsmProgram.Length, Is.GreaterThan(10));
        Assert.That(btProgram.Length, Is.GreaterThan(30));

        using World world = World.Create();
        var system = new GraphAiSoaBenchmarkSystem(world, fsmProgram, btProgram, EntityCount);
        for (int i = 0; i < EntityCount; i++)
        {
            world.Create(new GraphAiBrain { Index = i }, new GraphAiHotIntent());
        }

        for (int i = 0; i < WarmupTicks; i++)
        {
            system.Update(i);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();

        Span<long> frameTicks = stackalloc long[MeasuredTicks];
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        int beforeGen0 = GC.CollectionCount(0);
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < MeasuredTicks; i++)
        {
            long frameStart = Stopwatch.GetTimestamp();
            system.Update(WarmupTicks + i);
            long frameStop = Stopwatch.GetTimestamp();
            frameTicks[i] = frameStop - frameStart;
        }

        long stop = Stopwatch.GetTimestamp();
        int afterGen0 = GC.CollectionCount(0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;

        long totalTicks = stop - start;
        double totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
        double avgMs = totalMs / MeasuredTicks;
        double p95Ms = Percentile95Ms(frameTicks);
        long totalSimulatedTicks = WarmupTicks + MeasuredTicks;

        Console.WriteLine("[Benchmark] Graph AI FSM+BT SoA hot path:");
        Console.WriteLine($"  Entities: {EntityCount}");
        Console.WriteLine($"  WarmupTicks: {WarmupTicks}");
        Console.WriteLine($"  MeasuredTicks: {MeasuredTicks}");
        Console.WriteLine($"  FsmGraphExecutions: {system.FsmGraphExecutions}");
        Console.WriteLine($"  BtGraphExecutions: {system.BtGraphExecutions}");
        Console.WriteLine($"  CompletedTasks: {system.CompletedTasks}");
        Console.WriteLine($"  TotalMs: {totalMs:F2}");
        Console.WriteLine($"  AvgMsPerTick: {avgMs:F4}");
        Console.WriteLine($"  P95MsPerTick: {p95Ms:F4}");
        Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocated}");
        Console.WriteLine($"  Gen0Collections: {afterGen0 - beforeGen0}");
        Console.WriteLine($"  IntentChecksum: {system.IntentChecksum}");

        Assert.That(system.FsmGraphExecutions, Is.EqualTo((long)EntityCount * totalSimulatedTicks));
        Assert.That(system.BtGraphExecutions, Is.GreaterThan(EntityCount));
        Assert.That(system.CompletedTasks, Is.GreaterThan(EntityCount));
        Assert.That(system.FsmBranchMask, Is.EqualTo(0b1111));
        Assert.That((system.BtTaskMask & 0b111110), Is.EqualTo(0b111110));
        Assert.That(system.IntentChecksum, Is.Not.EqualTo(0));
        Assert.That(allocated, Is.LessThanOrEqualTo(64));
        Assert.That(afterGen0, Is.EqualTo(beforeGen0));
        Assert.That(avgMs, Is.LessThan(100.0));
        Assert.That(p95Ms, Is.LessThan(200.0));
    }

    private static GraphInstruction[] LoadProgram(string modPath, string programId)
    {
        string configPath = Path.Combine(
            FindRepoRoot(),
            modPath.Replace('/', Path.DirectorySeparatorChar),
            "assets",
            "GraphAiShowcase",
            "showcase.json");
        using FileStream stream = File.OpenRead(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        GraphAiShowcaseConfig config = JsonSerializer.Deserialize<GraphAiShowcaseConfig>(stream, options)
            ?? throw new InvalidOperationException($"Graph AI config '{configPath}' is empty.");

        for (int i = 0; i < config.Programs.Count; i++)
        {
            if (string.Equals(config.Programs[i].Id, programId, StringComparison.Ordinal))
            {
                return GraphAiProgramCompiler.Compile(config.Programs[i]);
            }
        }

        throw new InvalidOperationException($"Graph AI config '{configPath}' does not contain program '{programId}'.");
    }

    private static double Percentile95Ms(Span<long> frameTicks)
    {
        for (int i = 1; i < frameTicks.Length; i++)
        {
            long value = frameTicks[i];
            int j = i - 1;
            while (j >= 0 && frameTicks[j] > value)
            {
                frameTicks[j + 1] = frameTicks[j];
                j--;
            }

            frameTicks[j + 1] = value;
        }

        int index = Math.Min(frameTicks.Length - 1, (int)Math.Ceiling(frameTicks.Length * 0.95) - 1);
        return frameTicks[index] * 1000.0 / Stopwatch.Frequency;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class GraphAiSoaBenchmarkSystem
    {
        private readonly World _world;
        private readonly GraphInstruction[] _fsmProgram;
        private readonly GraphInstruction[] _btProgram;
        private readonly int[] _fsmIntRegisters;
        private readonly byte[] _fsmBoolRegisters;
        private readonly int[] _btIntRegisters;
        private readonly byte[] _btBoolRegisters;
        private readonly ushort[] _btTaskRemaining;

        public GraphAiSoaBenchmarkSystem(World world, GraphInstruction[] fsmProgram, GraphInstruction[] btProgram, int entityCount)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _fsmProgram = fsmProgram ?? throw new ArgumentNullException(nameof(fsmProgram));
            _btProgram = btProgram ?? throw new ArgumentNullException(nameof(btProgram));
            _fsmIntRegisters = new int[entityCount * GraphAiVmLimits.IntRegisters];
            _fsmBoolRegisters = new byte[entityCount * GraphAiVmLimits.BoolRegisters];
            _btIntRegisters = new int[entityCount * GraphAiVmLimits.IntRegisters];
            _btBoolRegisters = new byte[entityCount * GraphAiVmLimits.BoolRegisters];
            _btTaskRemaining = new ushort[entityCount];
            Seed(entityCount);
        }

        public long FsmGraphExecutions { get; private set; }
        public long BtGraphExecutions { get; private set; }
        public long CompletedTasks { get; private set; }
        public int FsmBranchMask { get; private set; }
        public int BtTaskMask { get; private set; }
        public int IntentChecksum { get; private set; }

        public void Update(int tick)
        {
            var job = new TickJob(
                tick,
                _fsmProgram,
                _btProgram,
                _fsmIntRegisters,
                _fsmBoolRegisters,
                _btIntRegisters,
                _btBoolRegisters,
                _btTaskRemaining);
            _world.InlineEntityQuery<TickJob, GraphAiBrain, GraphAiHotIntent>(in Query, ref job);
            FsmGraphExecutions += job.FsmGraphExecutions;
            BtGraphExecutions += job.BtGraphExecutions;
            CompletedTasks += job.CompletedTasks;
            FsmBranchMask |= job.FsmBranchMask;
            BtTaskMask |= job.BtTaskMask;
            IntentChecksum = unchecked(IntentChecksum + job.IntentChecksum);
        }

        private void Seed(int entityCount)
        {
            for (int index = 0; index < entityCount; index++)
            {
                int fsmBase = index * GraphAiVmLimits.IntRegisters;
                int btBase = index * GraphAiVmLimits.IntRegisters;
                SeedSenses(_fsmIntRegisters, fsmBase, index);
                SeedSenses(_btIntRegisters, btBase, index);
                _fsmIntRegisters[fsmBase + 1] = index & 3;
                _btIntRegisters[btBase + 6] = index & 1;
            }
        }
    }

    private struct TickJob : IForEachWithEntity<GraphAiBrain, GraphAiHotIntent>
    {
        private readonly int _tick;
        private readonly GraphInstruction[] _fsmProgram;
        private readonly GraphInstruction[] _btProgram;
        private readonly int[] _fsmIntRegisters;
        private readonly byte[] _fsmBoolRegisters;
        private readonly int[] _btIntRegisters;
        private readonly byte[] _btBoolRegisters;
        private readonly ushort[] _btTaskRemaining;

        public TickJob(
            int tick,
            GraphInstruction[] fsmProgram,
            GraphInstruction[] btProgram,
            int[] fsmIntRegisters,
            byte[] fsmBoolRegisters,
            int[] btIntRegisters,
            byte[] btBoolRegisters,
            ushort[] btTaskRemaining)
        {
            _tick = tick;
            _fsmProgram = fsmProgram;
            _btProgram = btProgram;
            _fsmIntRegisters = fsmIntRegisters;
            _fsmBoolRegisters = fsmBoolRegisters;
            _btIntRegisters = btIntRegisters;
            _btBoolRegisters = btBoolRegisters;
            _btTaskRemaining = btTaskRemaining;
            FsmGraphExecutions = 0;
            BtGraphExecutions = 0;
            CompletedTasks = 0;
            FsmBranchMask = 0;
            BtTaskMask = 0;
            IntentChecksum = 0;
        }

        public int FsmGraphExecutions { get; private set; }
        public int BtGraphExecutions { get; private set; }
        public int CompletedTasks { get; private set; }
        public int FsmBranchMask { get; private set; }
        public int BtTaskMask { get; private set; }
        public int IntentChecksum { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(Entity entity, ref GraphAiBrain brain, ref GraphAiHotIntent intent)
        {
            int index = brain.Index;
            int fsmBase = index * GraphAiVmLimits.IntRegisters;
            int boolBase = index * GraphAiVmLimits.BoolRegisters;
            SeedSenses(_fsmIntRegisters, fsmBase, index);
            _fsmIntRegisters[fsmBase] = _tick;

            var fsmState = new GraphAiSoaVmState(_fsmIntRegisters, _fsmBoolRegisters, fsmBase, boolBase);
            GraphExecutor.Execute(ref fsmState, _fsmProgram, GraphAiSoaOpHandlerTable.Instance);
            FsmGraphExecutions++;

            int fsmStateValue = _fsmIntRegisters[fsmBase + 10];
            int fsmIntent = _fsmIntRegisters[fsmBase + 11];
            _fsmIntRegisters[fsmBase + 1] = fsmStateValue;
            FsmBranchMask |= 1 << fsmStateValue;

            int btBase = index * GraphAiVmLimits.IntRegisters;
            SeedSenses(_btIntRegisters, btBase, index);
            _btIntRegisters[btBase] = _tick;
            _btIntRegisters[btBase + 1] = fsmStateValue;

            ushort remaining = _btTaskRemaining[index];
            if (remaining > 0)
            {
                remaining--;
                _btTaskRemaining[index] = remaining;
                _btIntRegisters[btBase + 7] = remaining;
                if (remaining == 0)
                {
                    CompletedTasks++;
                }
            }
            else
            {
                var btState = new GraphAiSoaVmState(_btIntRegisters, _btBoolRegisters, btBase, boolBase);
                GraphExecutor.Execute(ref btState, _btProgram, GraphAiSoaOpHandlerTable.Instance);
                BtGraphExecutions++;

                int taskId = _btIntRegisters[btBase + 13];
                int duration = _btIntRegisters[btBase + 14];
                if (taskId <= 0 || duration <= 0 || duration > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Graph BT benchmark produced an invalid task.");
                }

                _btTaskRemaining[index] = (ushort)duration;
                _btIntRegisters[btBase + 6] = _btIntRegisters[btBase + 12];
                _btIntRegisters[btBase + 7] = duration;
                BtTaskMask |= 1 << taskId;
            }

            intent.State = (byte)fsmStateValue;
            intent.Code = (byte)((fsmIntent + _btIntRegisters[btBase + 11]) & 0xFF);
            intent.Task = (byte)_btIntRegisters[btBase + 13];
            intent.Revision++;
            IntentChecksum = unchecked(IntentChecksum + intent.State + intent.Code + intent.Task + intent.Revision);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SeedSenses(int[] registers, int baseIndex, int index)
    {
        int profile = index & 3;
        if (profile == 0)
        {
            registers[baseIndex + 2] = 240;
            registers[baseIndex + 3] = 86;
            registers[baseIndex + 4] = 60;
            return;
        }

        if (profile == 1)
        {
            registers[baseIndex + 2] = 760;
            registers[baseIndex + 3] = 92;
            registers[baseIndex + 4] = 94;
            return;
        }

        if (profile == 2)
        {
            registers[baseIndex + 2] = 180;
            registers[baseIndex + 3] = 24;
            registers[baseIndex + 4] = 48;
            return;
        }

        registers[baseIndex + 2] = 900;
        registers[baseIndex + 3] = 88;
        registers[baseIndex + 4] = 52;
    }

    private readonly struct GraphAiSoaVmState
    {
        public GraphAiSoaVmState(int[] intRegisters, byte[] boolRegisters, int intBase, int boolBase)
        {
            I = intRegisters;
            B = boolRegisters;
            IntBase = intBase;
            BoolBase = boolBase;
        }

        public readonly int[] I;
        public readonly byte[] B;
        public readonly int IntBase;
        public readonly int BoolBase;
    }

    private sealed class GraphAiSoaOpHandlerTable : IOpHandlerTable<GraphAiSoaVmState>
    {
        public static readonly GraphAiSoaOpHandlerTable Instance = new();

        public GraphOpHandler<GraphAiSoaVmState>[] Handlers { get; }

        private GraphAiSoaOpHandlerTable()
        {
            var handlers = new GraphOpHandler<GraphAiSoaVmState>[GraphAiVmLimits.HandlerTableSize];
            handlers[(ushort)GraphAiOp.ConstInt] = HandleConstInt;
            handlers[(ushort)GraphAiOp.CopyInt] = HandleCopyInt;
            handlers[(ushort)GraphAiOp.AddInt] = HandleAddInt;
            handlers[(ushort)GraphAiOp.DecrementPositive] = HandleDecrementPositive;
            handlers[(ushort)GraphAiOp.CompareLtIntImm] = HandleCompareLtIntImm;
            handlers[(ushort)GraphAiOp.CompareGtIntImm] = HandleCompareGtIntImm;
            handlers[(ushort)GraphAiOp.CompareEqIntImm] = HandleCompareEqIntImm;
            handlers[(ushort)GraphAiOp.Jump] = HandleJump;
            handlers[(ushort)GraphAiOp.JumpIfFalse] = HandleJumpIfFalse;
            Handlers = handlers;
        }

        private static void HandleConstInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.I[state.IntBase + ins.Dst] = ins.Imm;
        }

        private static void HandleCopyInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.I[state.IntBase + ins.Dst] = state.I[state.IntBase + ins.A];
        }

        private static void HandleAddInt(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.I[state.IntBase + ins.Dst] = state.I[state.IntBase + ins.A] + state.I[state.IntBase + ins.B];
        }

        private static void HandleDecrementPositive(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            int value = state.I[state.IntBase + ins.A];
            state.I[state.IntBase + ins.Dst] = value > 0 ? value - 1 : 0;
        }

        private static void HandleCompareLtIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] < ins.Imm ? 1 : 0);
        }

        private static void HandleCompareGtIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] > ins.Imm ? 1 : 0);
        }

        private static void HandleCompareEqIntImm(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            state.B[state.BoolBase + ins.Dst] = (byte)(state.I[state.IntBase + ins.A] == ins.Imm ? 1 : 0);
        }

        private static void HandleJump(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            pc += ins.Imm;
        }

        private static void HandleJumpIfFalse(ref GraphAiSoaVmState state, in GraphInstruction ins, ref int pc)
        {
            if (state.B[state.BoolBase + ins.A] == 0)
            {
                pc += ins.Imm;
            }
        }
    }

    private struct GraphAiBrain
    {
        public int Index;
    }

    private struct GraphAiHotIntent
    {
        public byte State;
        public byte Code;
        public byte Task;
        public int Revision;
    }
}
