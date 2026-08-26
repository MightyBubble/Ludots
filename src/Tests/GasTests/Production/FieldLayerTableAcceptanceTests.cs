using System;
using System.IO;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Field layer table acceptance: ownership.table materializes three regions;
/// MapLoaded writes count map vars and creates the progress panel.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldLayerTableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "field_layer_table";
    private const string LayerKey = "ownership.table";
    private const string ProgressPanelId = "panel.field_layer_table.progress";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FieldLayerTableMod",
    };

    [Test]
    public void LayerTable_Materialize_Fields_Panel()
    {
        using GameEngine engine = CreateEngine(Mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        MapVariableStore variables = session.Variables
            ?? throw new InvalidOperationException("map variables missing");
        Assert.That(session.Fields, Is.Not.Null);
        Assert.That(session.RegionIndex, Is.Not.Null);
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        Assert.That(layerData, Is.InstanceOf<DiscreteIdFieldLayerData>());
        var layer = (DiscreteIdFieldLayerData)layerData;

        Assert.That(session.RegionIndex!.Count, Is.EqualTo(3), "r1/r2/r3 must materialize");
        Assert.That(layer.Regions.GetName(1), Is.EqualTo("r1"));
        Assert.That(layer.Regions.GetName(2), Is.EqualTo("r2"));
        Assert.That(layer.Regions.GetName(3), Is.EqualTo("r3"));
        Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(12));

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(1), "MapLoaded writes stage=1");
        Assert.That(variables.ReadInt("layer_count"), Is.EqualTo(1));
        Assert.That(variables.ReadInt("region_count"), Is.EqualTo(3));
        Assert.That(variables.ReadInt("non_default_count"), Is.EqualTo(12));

        var panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        int progressPanels = 0;
        foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, ProgressPanelId, StringComparison.Ordinal))
            {
                progressPanels++;
            }
        }

        Assert.That(progressPanels, Is.EqualTo(1), "MapLoaded must create the progress panel.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join("; ", engine.TriggerManager.Errors));
    }

    [Test]
    public void LayerTableMod_IsDataOnly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "field_layer_table", "FieldLayerTableMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldLayerTableMod.csproj")), Is.False,
            "Layer table showcase must stay code-free: Fields + map + graphs + panels only.");
    }

    private static GameEngine CreateEngine(string[] mods)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, mods),
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
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
