using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph;

[TestFixture]
[NonParallelizable]
public sealed class GraphDebugToolTests
{
    private const float DeltaTime = 1f / 60f;
    private const string GraphName = "Graph.NightRaid.Flow";
    private const string EntryLabel = "on_raid_start";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "MapTriggerNightRaidMod",
    };

    [Test]
    public void ListAndConfigure_ReportLogicalAndAllocatedCapacityAcrossOffOnOff()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap("night_raid");
        Tick(engine, 2);

        var tool = new GraphDebugTool();
        var context = new AgentToolContext(engine);

        JsonObject listed = Execute(tool, context, "list");
        var slots = engine.GetService(CoreServiceKeys.TriggerGraphExecutionSlots);
        Assert.That((int)listed["executionSlots"]!["capacity"]!, Is.EqualTo(slots.Capacity));
        Assert.That((int)listed["executionSlots"]!["inUseCount"]!, Is.EqualTo(slots.InUseCount));
        Assert.That((int)listed["executionSlots"]!["highWaterMark"]!, Is.EqualTo(slots.HighWaterMark));
        JsonObject disabled = FindMount(listed);
        AssertMount(disabled, GraphDebugTraceMode.Disabled, allocatedCapacity: 0);

        JsonObject enabled = Configure(tool, context, "node");
        AssertMount(enabled, GraphDebugTraceMode.Node, GraphDebugTrace.DefaultCapacity);

        JsonObject disabledAgain = Configure(tool, context, "off");
        AssertMount(disabledAgain, GraphDebugTraceMode.Disabled, allocatedCapacity: 0);
        Assert.That((long)disabledAgain["latestSequence"]!, Is.EqualTo(0));
        Assert.That((long)disabledAgain["droppedCount"]!, Is.EqualTo(0));
    }

    private static JsonObject Configure(GraphDebugTool tool, AgentToolContext context, string mode)
    {
        JsonObject result = (JsonObject)tool.Execute(
            new JsonObject
            {
                ["action"] = "configure",
                ["graphId"] = GraphName,
                ["entryLabel"] = EntryLabel,
                ["mode"] = mode,
            },
            context)!;
        return (JsonObject)result["mount"]!;
    }

    private static JsonObject Execute(GraphDebugTool tool, AgentToolContext context, string action)
    {
        return (JsonObject)tool.Execute(new JsonObject { ["action"] = action }, context)!;
    }

    private static JsonObject FindMount(JsonObject result)
    {
        JsonArray mounts = (JsonArray)result["mounts"]!;
        for (int i = 0; i < mounts.Count; i++)
        {
            var mount = (JsonObject)mounts[i]!;
            if (string.Equals((string?)mount["graphName"], GraphName, StringComparison.Ordinal) &&
                string.Equals((string?)mount["entryLabel"], EntryLabel, StringComparison.Ordinal))
            {
                return mount;
            }
        }

        throw new AssertionException($"Mounted graph entry '{GraphName}/{EntryLabel}' was not listed.");
    }

    private static void AssertMount(JsonObject mount, GraphDebugTraceMode mode, int allocatedCapacity)
    {
        Assert.That((string?)mount["mode"], Is.EqualTo(mode.ToString()));
        Assert.That((int)mount["capacity"]!, Is.EqualTo(GraphDebugTrace.DefaultCapacity));
        Assert.That((int)mount["allocatedCapacity"]!, Is.EqualTo(allocatedCapacity));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, Mods),
            Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
