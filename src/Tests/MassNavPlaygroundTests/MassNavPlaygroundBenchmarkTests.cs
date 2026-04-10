using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.MassNavPlayground;

[TestFixture]
[NonParallelizable]
public sealed class MassNavPlaygroundBenchmarkTests
{
    private const int AgentCount = 20_000;
    private const int SampleCount = 5;
    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 10;

    [Test]
    public void Benchmark_MassNavFacade_SelectionSync_And_CommandApply_20k()
    {
        var selectionMs = new double[SampleCount];
        var commandMs = new double[SampleCount];

        for (int sample = 0; sample < SampleCount; sample++)
        {
            using var world = World.Create();
            var globals = new Dictionary<string, object>();
            var registry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var selection = new SelectionRuntime(world, new SelectionRuntimeConfig(), registry);
            globals[CoreServiceKeys.SelectionRuntime.Name] = selection;

            Entity owner = world.Create(default(SelectionDragState));
            globals[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);

            Entity[] agents = new Entity[AgentCount];
            for (int i = 0; i < AgentCount; i++)
            {
                agents[i] = world.Create(
                    new NavGoal2D
                    {
                        Kind = NavGoalKind2D.Point,
                        TargetCm = Fix64Vec2.Zero,
                        RadiusCm = Fix64.Zero
                    });
            }

            selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, agents);

            var simulation = new MassNavSimulationRuntime();

            for (int i = 0; i < WarmupIterations; i++)
            {
                MassNavSelectionSync.SyncIfChanged(world, globals, selection, simulation);
                MassNavCommandRuntime.ApplyGridMove(world, simulation.SelectedEntities, new Vector2(8000f, 0f), 120, 120);
                selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, agents);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long selectionTicks = 0;
            long commandTicks = 0;
            for (int iteration = 0; iteration < MeasuredIterations; iteration++)
            {
                selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, agents);

                long t0 = Stopwatch.GetTimestamp();
                MassNavSelectionSync.SyncIfChanged(world, globals, selection, simulation);
                selectionTicks += Stopwatch.GetTimestamp() - t0;

                t0 = Stopwatch.GetTimestamp();
                MassNavCommandRuntime.ApplyGridMove(world, simulation.SelectedEntities, new Vector2(8000f, 0f), 120, 120);
                commandTicks += Stopwatch.GetTimestamp() - t0;
            }

            selectionMs[sample] = selectionTicks * 1000.0 / Stopwatch.Frequency / MeasuredIterations;
            commandMs[sample] = commandTicks * 1000.0 / Stopwatch.Frequency / MeasuredIterations;
        }

        Console.WriteLine("[Benchmark] MassNavFacade / SelectionSync+CommandApply / 20k");
        Console.WriteLine($"  Samples: {SampleCount}");
        Console.WriteLine($"  Median Selection Sync: {Median(selectionMs):F4}ms");
        Console.WriteLine($"  Selection Samples: {Format(selectionMs)}");
        Console.WriteLine($"  Median Command Apply: {Median(commandMs):F4}ms");
        Console.WriteLine($"  Command Samples: {Format(commandMs)}");
    }

    private static string Format(double[] values)
    {
        return string.Join(", ", Array.ConvertAll(values, value => $"{value:F4}ms"));
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        int mid = copy.Length / 2;
        return (copy.Length & 1) != 0
            ? copy[mid]
            : (copy[mid - 1] + copy[mid]) * 0.5;
    }
}
