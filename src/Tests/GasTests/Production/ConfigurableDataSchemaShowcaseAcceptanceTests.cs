using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class ConfigurableDataSchemaShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "configurable_data_schema_workbench";

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "ConfigurableDataSchemaSharedMod",
    };

    [Test]
    public void LauncherBindingAndPreset_AreRegistered()
    {
        string repoRoot = FindRepoRoot();
        using JsonDocument bindingsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
        using JsonDocument presetsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));

        Assert.That(
            bindingsDoc.RootElement.GetProperty("bindings").EnumerateArray()
                .Any(b => b.GetProperty("name").GetString() == "configurable_data_schema"),
            Is.True);
        Assert.That(
            presetsDoc.RootElement.GetProperty("presets").EnumerateArray()
                .Any(p => p.GetProperty("id").GetString() == "configurable_data_schema_raylib"),
            Is.True);
    }

    [Test]
    public void Workbench_LoadsNonEmptyScoutAndProjectsDataPins()
    {
        using GameEngine engine = CreateEngine(skinMod: null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), string.Join("\n", engine.TriggerManager.Errors));
        DataSchemaRegistry registry = engine.DataSchemaRegistry
            ?? throw new InvalidOperationException("DataSchemaRegistry missing.");
        Assert.That(registry.TryGet("unit.scout", out _), Is.True);
        Assert.That(registry.TryGet("unit.workbench", out _), Is.True);
        Assert.That(registry.TryGetNode("unit.workbench", "position.x", out JsonNode? x), Is.True);
        Assert.That(x!.GetValue<double>(), Is.EqualTo(12.5));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.GreaterThanOrEqualTo(1));

        PanelActivationApi activation = engine.GetService(CoreServiceKeys.PanelActivationApi)
            ?? throw new InvalidOperationException("PanelActivationApi missing.");
        Assert.That(activation.Store.IsVisible("panel.mixed.schema.workbench"), Is.True);

        PanelInstanceHandle mixed = FindPanel(panelHost, "panel.mixed.schema.workbench");
        Assert.That(panelHost.TryGetValues(mixed, out PanelVariableSet values), Is.True);
        Assert.That(values.GetDisplayText("name"), Is.EqualTo("Scout"));
        Assert.That(values.Get("x"), Is.EqualTo(12.5f).Within(0.001f));
        Assert.That(values.Get("score"), Is.EqualTo(42f).Within(0.001f));
    }

    [TestCase("ConfigurableDataSchemaNativeMod")]
    [TestCase(null)]
    public void Workbench_DraftEditUpdatesProjectionSession(string? skinMod)
    {
        using GameEngine engine = CreateEngine(skinMod);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        runtime.NudgePositionX(engine, 1f);
        Tick(engine, 4);

        DataSchemaProjectionSession session = engine.DataSchemaProjectionSession
            ?? throw new InvalidOperationException("DataSchemaProjectionSession missing.");
        Assert.That(session.IsPreview, Is.True);
        Assert.That(session.TryGetNode("unit.workbench", "position.x", out JsonNode? x), Is.True);
        Assert.That(x!.GetValue<double>(), Is.EqualTo(13.5));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)!;
        PanelInstanceHandle mixed = FindPanel(panelHost, "panel.mixed.schema.workbench");
        panelHost.Refresh(mixed);
        Assert.That(panelHost.TryGetValues(mixed, out PanelVariableSet values), Is.True);
        Assert.That(values.Get("x"), Is.EqualTo(13.5f).Within(0.001f));
    }

    [Test]
    public void Workbench_InvalidEnumDisablesExportAndKeepsLastGoodProjection()
    {
        using GameEngine engine = CreateEngine(null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        runtime.NudgePositionX(engine, 2f);
        Tick(engine, 2);
        runtime.InjectInvalidUnknownEnum(engine);
        Tick(engine, 2);

        Assert.That(runtime.IsValid, Is.False);
        Assert.That(runtime.CanExport, Is.False);
        Assert.That(
            runtime.FirstErrorPath,
            Does.Contain("rarity").IgnoreCase
                .Or.Contain("Legendary")
                .Or.Contain("enum")
                .Or.Contain("unknown"));

        DataSchemaProjectionSession session = engine.DataSchemaProjectionSession!;
        Assert.That(session.TryGetNode("unit.workbench", "position.x", out JsonNode? x), Is.True);
        Assert.That(x!.GetValue<double>(), Is.EqualTo(14.5));
    }

    [Test]
    public void Workbench_SourceModeSwitchesVisiblePanel()
    {
        using GameEngine engine = CreateEngine(null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        PanelActivationApi activation = engine.GetService(CoreServiceKeys.PanelActivationApi)!;

        runtime.CycleSourceMode(engine); // Mixed -> Data
        Tick(engine, 2);
        Assert.That(activation.Store.IsVisible("panel.data.schema.workbench"), Is.True);
        Assert.That(activation.Store.IsVisible("panel.mixed.schema.workbench"), Is.False);

        runtime.CycleSourceMode(engine); // Data -> Graph
        Tick(engine, 2);
        Assert.That(activation.Store.IsVisible("panel.graph.schema.workbench"), Is.True);
        Assert.That(activation.Store.IsVisible("panel.data.schema.workbench"), Is.False);
    }

    [Test]
    public void Workbench_WebSkin_HeadlessStillProjectsData()
    {
        using GameEngine engine = CreateEngine("ConfigurableDataSchemaWebMod");
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)!;
        PanelInstanceHandle mixed = FindPanel(panelHost, "panel.mixed.schema.workbench");
        Assert.That(panelHost.TryGetValues(mixed, out PanelVariableSet values), Is.True);
        Assert.That(values.GetDisplayText("name"), Is.EqualTo("Scout"));
        Assert.That(values.Get("score"), Is.EqualTo(42f).Within(0.001f));
    }

    [Test]
    public void Authoring_AddFieldBindPathAndSaveToMod()
    {
        using GameEngine engine = CreateEngine(null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        string modRoot = runtime.SaveTargetRoot;
        Assert.That(Directory.Exists(modRoot), Is.True);

        string tempRoot = Path.Combine(Path.GetTempPath(), "LudotsDataSchemaAuthoring", Guid.NewGuid().ToString("N"));
        CopyAuthoringAssets(modRoot, tempRoot);
        runtime.RedirectAuthoringSaveRoot(engine, tempRoot);

        runtime.SetAuthoringLayer(engine, "Schema");
        runtime.AuthoringAddField(engine);
        runtime.SetAuthoringLayer(engine, "Binding");
        runtime.AuthoringSelectPin(engine, "name");
        runtime.AuthoringSelectBindingPath(engine, "name");
        runtime.SaveAuthoringToMod(engine);
        Tick(engine, 2);

        Assert.That(runtime.CanSaveToMod, Is.True);
        string savedSchemas = File.ReadAllText(Path.Combine(tempRoot, "assets", "Data", "data_schemas.json"));
        Assert.That(savedSchemas, Does.Contain("notes").Or.Contain("speed").Or.Contain("faction"));
        Assert.That(File.Exists(Path.Combine(tempRoot, "assets", "Panels", "panel_templates.json")), Is.True);

        try { Directory.Delete(tempRoot, recursive: true); } catch { }
    }

    private static void CopyAuthoringAssets(string sourceRoot, string targetRoot)
    {
        string[] relatives =
        {
            Path.Combine("assets", "Data", "data_schemas.json"),
            Path.Combine("assets", "Data", "data_records.json"),
            Path.Combine("assets", "Panels", "panel_templates.json"),
        };
        foreach (string relative in relatives)
        {
            string source = Path.Combine(sourceRoot, relative);
            string target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    [Test]
    public void Authoring_InvalidBindingKeepsSaveDisabled()
    {
        using GameEngine engine = CreateEngine(null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        runtime.InjectInvalidBindingPath(engine, "does.not.exist");
        Tick(engine, 2);
        Assert.That(runtime.CanSaveToMod, Is.False);
        Assert.That(runtime.AuthoringError, Does.Contain("does.not.exist").IgnoreCase
            .Or.Contain("path").IgnoreCase
            .Or.Contain("unknown").IgnoreCase
            .Or.Not.Empty);
    }

    [Test]
    public void Authoring_BindingPathHotAppliesToLivePanel()
    {
        using GameEngine engine = CreateEngine(null);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        ConfigurableDataSchemaRuntimeProxy runtime = RequireRuntime(engine);
        runtime.SetAuthoringLayer(engine, "Binding");
        runtime.AuthoringSelectPin(engine, "name");
        runtime.AuthoringSelectBindingPath(engine, "rarity");
        Tick(engine, 2);

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)!;
        PanelInstanceHandle mixed = FindPanel(panelHost, "panel.mixed.schema.workbench");
        Assert.That(panelHost.TryGetValues(mixed, out PanelVariableSet values), Is.True);
        Assert.That(values.GetDisplayText("name"), Is.EqualTo("Common").Or.EqualTo("Rare").Or.EqualTo("Epic"));
    }

    private static ConfigurableDataSchemaRuntimeProxy RequireRuntime(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue("ConfigurableDataSchema.Runtime", out object? runtimeObj) ||
            runtimeObj == null)
        {
            throw new InvalidOperationException("ConfigurableDataSchema runtime was not installed.");
        }

        return new ConfigurableDataSchemaRuntimeProxy(runtimeObj);
    }

    private static GameEngine CreateEngine(string? skinMod)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        IEnumerable<string> mods = skinMod == null ? BaseMods : BaseMods.Append(skinMod);

        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, mods),
            Path.Combine(repoRoot, "assets"));
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(DeltaTime);
        }
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, string templateId)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, templateId, StringComparison.Ordinal))
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"Panel '{templateId}' was not instantiated.");
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// Reflection bridge so GasTests does not take a project reference on the showcase assembly.
    /// </summary>
    private sealed class ConfigurableDataSchemaRuntimeProxy
    {
        private readonly object _runtime;
        private readonly Type _type;

        public ConfigurableDataSchemaRuntimeProxy(object runtime)
        {
            _runtime = runtime;
            _type = runtime.GetType();
        }

        public bool IsValid
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (bool)snapshot.GetType().GetProperty("IsValid")!.GetValue(snapshot)!;
            }
        }

        public bool CanExport
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (bool)snapshot.GetType().GetProperty("CanExport")!.GetValue(snapshot)!;
            }
        }

        public string FirstErrorPath
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (string)snapshot.GetType().GetProperty("FirstErrorPath")!.GetValue(snapshot)!;
            }
        }

        public void NudgePositionX(GameEngine engine, float delta) =>
            _type.GetMethod("NudgePositionX")!.Invoke(_runtime, new object[] { engine, delta });

        public void InjectInvalidUnknownEnum(GameEngine engine)
        {
            Type invalidCase = _type.Assembly.GetType("ConfigurableDataSchemaSharedMod.Runtime.DataSchemaInvalidCase")
                ?? throw new InvalidOperationException("DataSchemaInvalidCase missing.");
            object unknownEnum = Enum.Parse(invalidCase, "UnknownEnum");
            _type.GetMethod("InjectInvalid")!.Invoke(_runtime, new object[] { engine, unknownEnum });
        }

        public void CycleSourceMode(GameEngine engine) =>
            _type.GetMethod("CycleSourceMode")!.Invoke(_runtime, new object[] { engine });

        public string SaveTargetRoot
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (string)snapshot.GetType().GetProperty("SaveTargetRoot")!.GetValue(snapshot)!;
            }
        }

        public bool CanSaveToMod
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (bool)snapshot.GetType().GetProperty("CanSaveToMod")!.GetValue(snapshot)!;
            }
        }

        public string AuthoringError
        {
            get
            {
                object snapshot = _type.GetProperty("Snapshot")!.GetValue(_runtime)!;
                return (string)snapshot.GetType().GetProperty("AuthoringError")!.GetValue(snapshot)!;
            }
        }

        public void SetAuthoringLayer(GameEngine engine, string layerName)
        {
            Type layerType = _type.Assembly.GetType("ConfigurableDataSchemaSharedMod.Runtime.DataSchemaAuthoringLayer")
                ?? throw new InvalidOperationException("DataSchemaAuthoringLayer missing.");
            object layer = Enum.Parse(layerType, layerName);
            _type.GetMethod("SetAuthoringLayer")!.Invoke(_runtime, new object[] { engine, layer });
        }

        public void AuthoringAddField(GameEngine engine) =>
            _type.GetMethod("AuthoringAddField")!.Invoke(_runtime, new object[] { engine });

        public void AuthoringSelectPin(GameEngine engine, string pinName) =>
            _type.GetMethod("AuthoringSelectPin")!.Invoke(_runtime, new object[] { engine, pinName });

        public void AuthoringSelectBindingPath(GameEngine engine, string path) =>
            _type.GetMethod("AuthoringSelectBindingPath")!.Invoke(_runtime, new object[] { engine, path });

        public void InjectInvalidBindingPath(GameEngine engine, string path) =>
            _type.GetMethod("InjectInvalidBindingPath")!.Invoke(_runtime, new object[] { engine, path });

        public void SaveAuthoringToMod(GameEngine engine) =>
            _type.GetMethod("SaveAuthoringToMod")!.Invoke(_runtime, new object[] { engine });

        public void RedirectAuthoringSaveRoot(GameEngine engine, string root) =>
            _type.GetMethod("RedirectAuthoringSaveRoot")!.Invoke(_runtime, new object[] { engine, root });
    }
}
