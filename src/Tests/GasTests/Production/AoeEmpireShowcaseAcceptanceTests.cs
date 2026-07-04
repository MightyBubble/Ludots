using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class AoeEmpireShowcaseAcceptanceTests
{
    private const string MapId = "rts_empire_like";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "AoeEmpireMod",
    };

    [Test]
    public void AoeEmpireMap_LoadsFiveNationsWithHundredTemplates()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        int townCenters = CountEntitiesByNameContains(engine.World, "Town Center");
        int villagers = CountEntitiesByNameContains(engine.World, "Villager");
        int militia = CountEntitiesByNameContains(engine.World, "Militia");

        Assert.That(townCenters, Is.EqualTo(5), "Skirmish map should place one town center per nation.");
        Assert.That(villagers, Is.GreaterThanOrEqualTo(10), "Each nation should start with villagers.");
        Assert.That(militia, Is.GreaterThanOrEqualTo(5), "Each nation should start with militia.");

        var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("AbilityDefinitionRegistry service is missing.");
        Assert.That(abilities.TryGet(AbilityIdRegistry.GetId("Ability.Aoe.Frankia.Build.House"), out _), Is.True);
        Assert.That(abilities.TryGet(AbilityIdRegistry.GetId("Ability.Aoe.Nordheim.Train.Knight"), out _), Is.True);
        Assert.That(abilities.TryGet(AbilityIdRegistry.GetId("Ability.Aoe.Attack.Melee"), out _), Is.True);
    }

    [Test]
    public void AoeEmpireMap_Templates_ContainExactlyOneHundredUnitTypes()
    {
        string repoRoot = FindRepoRoot();
        string templatesPath = Path.Combine(repoRoot, "mods/aoe_empire/AoeEmpireMod/assets/Entities/templates.json");
        Assert.That(File.Exists(templatesPath), Is.True);

        var templates = System.Text.Json.JsonSerializer.Deserialize<List<TemplateEntry>>(
            File.ReadAllText(templatesPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(templates, Is.Not.Null);
        Assert.That(templates!.Count, Is.EqualTo(100));

        var nations = templates!
            .Select(t => t.Id.Split('_')[1])
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        Assert.That(nations.Length, Is.EqualTo(5));
    }

    [Test]
    public void AoeEmpireMap_TechTreeProjection_IsRegistered()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        for (int i = 0; i < 120; i++)
        {
            engine.Tick(1f / 60f);
        }

        Assert.That(
            engine.GlobalContext.TryGetValue("AoeEmpireMod.TechTreeProjection", out object? projection) && projection is not null,
            Is.True,
            "AoE tech tree projection should be written for Web UI DataPlane.");
    }

    private static int CountEntitiesByNameContains(World world, string fragment)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (ref Name name) =>
        {
            if (name.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        });
        return count;
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallDummyInput(engine);
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
    {
        engine.LoadMap(mapId);
        engine.GlobalContext.Remove(CoreServiceKeys.CameraPoseRequest.Name);
        engine.GlobalContext.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
        Tick(engine, 5, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            engine.Tick(1f / 60f);
            sw.Stop();
            frameTimesMs.Add(sw.Elapsed.TotalMilliseconds);
        }
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ludots.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "mods")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class TemplateEntry
    {
        public string Id { get; set; } = string.Empty;
    }
}
