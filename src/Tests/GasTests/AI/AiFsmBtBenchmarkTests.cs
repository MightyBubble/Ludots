using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class AiFsmBtBenchmarkTests
    {
        private const int EntityCount = 50_000;
        private const int WarmupTicks = 32;
        private const int MeasuredTicks = 120;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<FsmBrain, BtCursor, AiSense, AiIntent>();

        [Test]
        public void Benchmark_AI_50kEntities_FsmBtSoa_ZeroAllocAfterWarmup()
        {
            using World world = World.Create();
            for (int i = 0; i < EntityCount; i++)
            {
                world.Create(
                    new FsmBrain
                    {
                        State = (byte)(i & 3),
                        CooldownTicks = (ushort)(i % 17),
                        Phase = (ushort)(i % 251)
                    },
                    new BtCursor
                    {
                        Node = (byte)(i % 5),
                        RunningChild = (byte)(i & 1),
                        WaitTicks = (ushort)(i % 11)
                    },
                    new AiSense
                    {
                        EnemyDistance = (short)(96 + (i % 640)),
                        Health = (short)(260 + (i % 700)),
                        Flags = (byte)(i & 1)
                    },
                    new AiIntent());
            }

            var system = new FsmBtSoaTickSystem(world);
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

            Console.WriteLine("[Benchmark] AI FSM+BT SoA hot path:");
            Console.WriteLine($"  Entities: {EntityCount}");
            Console.WriteLine($"  WarmupTicks: {WarmupTicks}");
            Console.WriteLine($"  MeasuredTicks: {MeasuredTicks}");
            Console.WriteLine($"  TotalMs: {totalMs:F2}");
            Console.WriteLine($"  AvgMsPerTick: {avgMs:F4}");
            Console.WriteLine($"  P95MsPerTick: {p95Ms:F4}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocated}");
            Console.WriteLine($"  Gen0Collections: {afterGen0 - beforeGen0}");
            Console.WriteLine($"  IntentChecksum: {system.IntentChecksum}");

            Assert.That(system.Visited, Is.EqualTo(EntityCount * (WarmupTicks + MeasuredTicks)));
            Assert.That(system.IntentChecksum, Is.Not.EqualTo(0));
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
            Assert.That(afterGen0, Is.EqualTo(beforeGen0));
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

        private sealed class FsmBtSoaTickSystem
        {
            private readonly World _world;

            public FsmBtSoaTickSystem(World world)
            {
                _world = world ?? throw new ArgumentNullException(nameof(world));
            }

            public int Visited { get; private set; }
            public int IntentChecksum { get; private set; }

            public void Update(int tick)
            {
                var job = new TickJob(tick);
                _world.InlineEntityQuery<TickJob, FsmBrain, BtCursor, AiSense, AiIntent>(in Query, ref job);
                Visited += job.Visited;
                IntentChecksum = unchecked(IntentChecksum + job.IntentChecksum);
            }
        }

        private struct TickJob : IForEachWithEntity<FsmBrain, BtCursor, AiSense, AiIntent>
        {
            private readonly int _tick;

            public TickJob(int tick)
            {
                _tick = tick;
                Visited = 0;
                IntentChecksum = 0;
            }

            public int Visited { get; private set; }
            public int IntentChecksum { get; private set; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref FsmBrain fsm, ref BtCursor bt, ref AiSense sense, ref AiIntent intent)
            {
                int noise = unchecked((entity.Id * 1103515245) + (_tick * 12345) + fsm.Phase);
                sense.EnemyDistance = ClampToShort(64 + (noise & 1023));
                sense.Health = ClampToShort(sense.Health + (((noise >> 12) & 31) - 14));
                byte hasEnemy = sense.EnemyDistance < 420 ? (byte)1 : (byte)0;
                byte lowHealth = sense.Health < 220 ? (byte)1 : (byte)0;
                sense.Flags = (byte)((hasEnemy << 0) | (lowHealth << 1));

                fsm.State = NextState(fsm.State, hasEnemy, lowHealth, ref fsm.CooldownTicks);
                TickBehaviorTree(ref fsm, ref bt, ref sense, ref intent);

                fsm.Phase = (ushort)((fsm.Phase + 1 + intent.Code) & 1023);
                intent.Revision++;
                Visited++;
                IntentChecksum = unchecked(IntentChecksum + intent.Code + intent.TargetLane + intent.Revision);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static byte NextState(byte state, byte hasEnemy, byte lowHealth, ref ushort cooldownTicks)
            {
                if (cooldownTicks > 0)
                {
                    cooldownTicks--;
                }

                if (lowHealth != 0)
                {
                    cooldownTicks = 12;
                    return 3;
                }

                if (hasEnemy != 0)
                {
                    return 2;
                }

                if (state == 3 && cooldownTicks == 0)
                {
                    return 0;
                }

                return state == 0 ? (byte)1 : state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void TickBehaviorTree(ref FsmBrain fsm, ref BtCursor bt, ref AiSense sense, ref AiIntent intent)
            {
                if (fsm.State == 3)
                {
                    bt.Node = 4;
                    bt.RunningChild = 0;
                    bt.WaitTicks = 2;
                    sense.Health = ClampToShort(sense.Health + 9);
                    intent.Code = 4;
                    intent.TargetLane = -1;
                    return;
                }

                if (fsm.State == 2)
                {
                    if (bt.WaitTicks > 0)
                    {
                        bt.WaitTicks--;
                    }
                    else
                    {
                        bt.WaitTicks = 3;
                        bt.RunningChild ^= 1;
                    }

                    bt.Node = bt.RunningChild == 0 ? (byte)2 : (byte)3;
                    intent.Code = bt.RunningChild == 0 ? (byte)2 : (byte)3;
                    intent.TargetLane = (short)((sense.EnemyDistance >> 5) & 31);
                    return;
                }

                bt.Node = 1;
                bt.RunningChild = (byte)((bt.RunningChild + 1) & 3);
                intent.Code = 1;
                intent.TargetLane = (short)((fsm.Phase + bt.RunningChild) & 63);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static short ClampToShort(int value)
            {
                if (value < 0)
                {
                    return 0;
                }

                return value > short.MaxValue ? short.MaxValue : (short)value;
            }
        }

        private struct FsmBrain
        {
            public byte State;
            public ushort CooldownTicks;
            public ushort Phase;
        }

        private struct BtCursor
        {
            public byte Node;
            public byte RunningChild;
            public ushort WaitTicks;
        }

        private struct AiSense
        {
            public short EnemyDistance;
            public short Health;
            public byte Flags;
        }

        private struct AiIntent
        {
            public byte Code;
            public short TargetLane;
            public int Revision;
        }
    }
}

