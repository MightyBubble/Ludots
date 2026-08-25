using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Arch.Core;
using CapabilityStandardEffectPresetTypeCodeShowcaseMod;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using CapabilityStandardPresenterBehaviorExtensionShowcaseMod;
using CapabilityStandardPresenterCommandExtensionShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardExtensibleRuntimeShowcaseAcceptanceTests
{
    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod"
    };

    [Test]
    public void ConfigShards_PlayerCastsAbilityLoadedFromIndependentShardFiles()
    {
        const string modId = "CapabilityStandardConfigShardsShowcaseMod";
        const string mapId = "capability_standard_config_shards_showcase";
        const string bindingName = "capability_standard_config_shards_showcase";
        const string presetId = "capability_standard_config_shards_showcase_raylib";
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();

        AssertRootShowcaseAssets(repoRoot, modId, mapId, bindingName, presetId,
            "assets/GAS/abilities/capability_standard.config_shards.ember_bolt.json",
            "assets/GAS/effects/capability_standard.config_shards.ember_bolt_damage.json");

        using var engine = CreateEngine(repoRoot, modId);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        UIRoot uiRoot = RequireUiRoot(engine);
        ExtensibleRuntimeShowcaseRuntime runtime = RequireRuntime(engine);
        TickFrames(engine, 1);

        AssertPanelAndButton(uiRoot, "capability-standard-config-shards-panel", "capability-standard-config-shards-cast");
        Assert.That(runtime.CapturePanelState(engine).MetricAValue, Is.EqualTo("loaded"));
        Assert.That(runtime.CapturePanelState(engine).MetricBLabel, Is.EqualTo("Target HP"));
        Assert.That(runtime.CapturePanelState(engine).MetricBValue, Is.EqualTo("100"));

        int abilityId = AbilityIdRegistry.GetId("Ability.CapabilityStandard.ConfigShards.EmberBolt");
        int effectId = EffectTemplateIdRegistry.GetId("Effect.CapabilityStandard.ConfigShards.EmberBoltDamage");
        Assert.That(engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)!.TryGet(abilityId, out AbilityDefinition ability), Is.True);
        Assert.That(engine.GetService(CoreServiceKeys.EffectTemplateRegistry)!.TryGet(effectId, out _), Is.True);
        Assert.That(AbilityExecContainsEffectSignal(in ability, effectId), Is.True);

        ClickElement(uiRoot, "capability-standard-config-shards-cast");
        TickUntil(engine, () => runtime.CapturePanelState(engine).MetricBValue == "90", 30);
        ExtensibleRuntimeShowcasePanelState state = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.PrimaryActionCount, Is.EqualTo(1));
            Assert.That(state.LastEvent, Does.Contain("reduced its health"));
            Assert.That(state.MetricBLabel, Is.EqualTo("Target HP"));
            Assert.That(state.MetricBValue, Is.EqualTo("90"));
        });
    }

    [Test]
    public void EffectPresetTypeCode_PlayerAppliesEffectAndModHandlerRunsThroughGas()
    {
        const string modId = "CapabilityStandardEffectPresetTypeCodeShowcaseMod";
        const string mapId = "capability_standard_effect_preset_type_code_showcase";
        const string bindingName = "capability_standard_effect_preset_type_code_showcase";
        const string presetId = "capability_standard_effect_preset_type_code_showcase_raylib";
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();

        AssertRootShowcaseAssets(repoRoot, modId, mapId, bindingName, presetId,
            "assets/GAS/preset_types/capability_standard.effect_preset_type_code.heat_mark.json",
            "assets/GAS/effects/capability_standard.effect_preset_type_code.heat_mark.json");

        using var engine = CreateEngine(repoRoot, modId);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        UIRoot uiRoot = RequireUiRoot(engine);
        ExtensibleRuntimeShowcaseRuntime runtime = RequireRuntime(engine);
        TickFrames(engine, 1);

        AssertPanelAndButton(uiRoot, "capability-standard-effect-preset-type-code-panel", "capability-standard-effect-preset-type-code-apply");
        int effectId = EffectTemplateIdRegistry.GetId("Effect.CapabilityStandard.EffectPresetTypeCode.HeatMark");
        Assert.That(engine.GetService(CoreServiceKeys.EffectTemplateRegistry)!.TryGet(effectId, out EffectTemplateData template), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(template.PresetType, Is.EqualTo(EffectPresetType.None));
            Assert.That(template.PresetTypeId, Is.GreaterThan(byte.MaxValue));
        });

        ClickElement(uiRoot, "capability-standard-effect-preset-type-code-apply");
        TickUntil(
            engine,
            () => ReadPlainInt(runtime.CapturePanelState(engine).MetricAValue) > 0,
            20);
        ExtensibleRuntimeShowcasePanelState state = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(state.LastEvent, Does.Contain("Heat Mark applied"));
            Assert.That(state.MetricALabel, Is.EqualTo("Calls"));
            Assert.That(ReadPlainInt(state.MetricAValue), Is.GreaterThan(0));
        });
    }

    [Test]
    public void PresenterBehaviorExtension_PlayerSeesCloudDriftTickFromModBehavior()
    {
        const string modId = "CapabilityStandardPresenterBehaviorExtensionShowcaseMod";
        const string mapId = "capability_standard_presenter_behavior_extension_showcase";
        const string bindingName = "capability_standard_presenter_behavior_extension_showcase";
        const string presetId = "capability_standard_presenter_behavior_extension_showcase_raylib";
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();

        AssertRootShowcaseAssets(repoRoot, modId, mapId, bindingName, presetId,
            "assets/Presentation/presenters/capability_standard.presenter_behavior_extension.cloud_banner.json");

        using var engine = CreateEngine(repoRoot, modId);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        UIRoot uiRoot = RequireUiRoot(engine);
        ExtensibleRuntimeShowcaseRuntime runtime = RequireRuntime(engine);
        TickUntil(engine, () => ReadPlainInt(runtime.CapturePanelState(engine).MetricBValue) > 0, 30);

        AssertPanelAndButton(uiRoot, "capability-standard-presenter-behavior-extension-panel", "capability-standard-presenter-behavior-extension-focus");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
        int definitionId = definitions.GetId(CapabilityStandardPresenterBehaviorExtensionShowcaseModEntry.PresenterDefinitionKey);
        Assert.That(definitions.TryGet(definitionId, out PresenterDefinition definition), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(definition.Behaviors[0].KindId,
                Is.GreaterThanOrEqualTo(PresenterBehaviorKindRegistry.FirstModBehaviorKindId));
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.Extension));
        });

        ClickElement(uiRoot, "capability-standard-presenter-behavior-extension-focus");
        TickFrames(engine, 1);
        ExtensibleRuntimeShowcasePanelState state = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(state.LastEvent, Does.Contain("CloudDrift"));
            Assert.That(ReadPlainInt(state.MetricBValue), Is.GreaterThan(0));
            Assert.That(runtime.PrimaryActionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void PresenterCommandExtension_PlayerSignalRoutesToModCommandOnExistingPresenter()
    {
        const string modId = "CapabilityStandardPresenterCommandExtensionShowcaseMod";
        const string mapId = "capability_standard_presenter_command_extension_showcase";
        const string bindingName = "capability_standard_presenter_command_extension_showcase";
        const string presetId = "capability_standard_presenter_command_extension_showcase_raylib";
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();

        AssertRootShowcaseAssets(repoRoot, modId, mapId, bindingName, presetId,
            "assets/Presentation/presenters/capability_standard.presenter_command_extension.signal_rules.json");

        using var engine = CreateEngine(repoRoot, modId);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        UIRoot uiRoot = RequireUiRoot(engine);
        ExtensibleRuntimeShowcaseRuntime runtime = RequireRuntime(engine);
        TickFrames(engine, 2);

        AssertPanelAndButton(uiRoot, "capability-standard-presenter-command-extension-panel", "capability-standard-presenter-command-extension-signal");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
        int definitionId = definitions.GetId(CapabilityStandardPresenterCommandExtensionShowcaseModEntry.PresenterDefinitionKey);
        Assert.That(definitions.TryGet(definitionId, out PresenterDefinition definition), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(definition.Rules[0].Command.CommandKindId,
                Is.GreaterThanOrEqualTo(PresenterCommandKindRegistry.FirstModCommandKindId));
            Assert.That(definition.Rules[0].Command.CommandKind, Is.EqualTo(PresenterCommandKind.Extension));
            Assert.That(definition.Rules[0].Command.RouteStrategy, Is.EqualTo(PresenterCommandRouteStrategy.ExistingInstances));
        });

        ClickElement(uiRoot, "capability-standard-presenter-command-extension-signal");
        TickUntil(engine, () => ReadPlainInt(runtime.CapturePanelState(engine).MetricBValue) > 0, 30);
        ExtensibleRuntimeShowcasePanelState state = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(state.LastEvent, Does.Contain("was handled"));
            Assert.That(state.MetricBValue, Is.EqualTo("1"));
        });
    }

    private static GameEngine CreateEngine(string repoRoot, params string[] showcaseMods)
    {
        var mods = new List<string>(BaseMods);
        mods.AddRange(showcaseMods);
        GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, mods);
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static ExtensibleRuntimeShowcaseRuntime RequireRuntime(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.BenchmarkSceneController) as ExtensibleRuntimeShowcaseRuntime
            ?? throw new InvalidOperationException("Extensible runtime showcase controller missing.");
    }

    private static UIRoot RequireUiRoot(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Showcase UI root missing.");
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        var frameTimes = new List<double>(frames);
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, frames, frameTimes);
    }

    private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames)
    {
        var frameTimes = new List<double>(maxFrames);
        CapabilityStandardShowcaseTestHarness.TickUntil(engine, frameTimes, predicate, maxFrames);
    }

    private static int ReadPlainInt(string value)
    {
        return int.TryParse(value, out int parsed) ? parsed : 0;
    }

    private static bool AbilityExecContainsEffectSignal(in AbilityDefinition ability, int effectId)
    {
        for (int i = 0; i < ability.ExecSpec.ItemCount; i++)
        {
            if (ability.ExecSpec.GetKind(i) == ExecItemKind.EffectSignal &&
                ability.ExecSpec.GetTemplateId(i) == effectId)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertPanelAndButton(UIRoot root, string panelElementId, string buttonElementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        Assert.That(scene.FindByElementId(panelElementId), Is.Not.Null);
        Assert.That(scene.FindByElementId(buttonElementId), Is.Not.Null);
    }

    private static void ClickElement(UIRoot root, string elementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must be clickable.");

        float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
        float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
        UiNode? hitNode = scene.HitTest(x, y);
        Assert.That(
            hitNode?.ElementId,
            Is.EqualTo(elementId),
            $"Pointer click for '{elementId}' hit '{hitNode?.ElementId ?? hitNode?.TagName ?? "<none>"}' instead.");

        bool downHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });
        bool upHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });

        Assert.That(downHandled || upHandled, Is.True, $"UI element '{elementId}' did not handle pointer click.");
    }

    private static void AssertRootShowcaseAssets(
        string repoRoot,
        string modId,
        string mapId,
        string bindingName,
        string presetId,
        params string[] relativeAssetPaths)
    {
        string modDir = Path.Combine(repoRoot, "mods", "showcases", "capability_standard", modId);
        AssertLauncherBinding(repoRoot, bindingName, modId);
        AssertLauncherPreset(repoRoot, presetId, bindingName);
        Assert.That(File.Exists(Path.Combine(modDir, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, $"{modId}.csproj")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "game.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "Maps", $"{mapId}.json")), Is.True);
        for (int i = 0; i < relativeAssetPaths.Length; i++)
        {
            Assert.That(File.Exists(Path.Combine(modDir, relativeAssetPaths[i])), Is.True, relativeAssetPaths[i]);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "assets", "game.json")));
        Assert.That(document.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo(mapId));
    }

    private static void AssertLauncherBinding(string repoRoot, string bindingName, string modId)
    {
        string launcherConfig = Path.Combine(repoRoot, "launcher.config.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfig));
        foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
        {
            if (!string.Equals(binding.GetProperty("name").GetString(), bindingName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement target = binding.GetProperty("target");
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
            Assert.That(
                target.GetProperty("value").GetString(),
                Is.EqualTo($"mods/showcases/capability_standard/{modId}"));
            Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo($"{modId}.csproj"));
            return;
        }

        Assert.Fail($"Launcher binding '{bindingName}' is missing.");
    }

    private static void AssertLauncherPreset(string repoRoot, string presetId, string bindingName)
    {
        string launcherPresets = Path.Combine(repoRoot, "launcher.presets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherPresets));
        foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
        {
            if (!string.Equals(preset.GetProperty("id").GetString(), presetId, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
            JsonElement selectors = preset.GetProperty("selectors");
            Assert.That(selectors.GetArrayLength(), Is.EqualTo(1));
            Assert.That(selectors[0].GetString(), Is.EqualTo($"${bindingName}"));
            return;
        }

        Assert.Fail($"Launcher preset '{presetId}' is missing.");
    }
}
