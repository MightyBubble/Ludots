using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace GasTests.Effect;

[TestFixture]
public sealed class EffectCompositionSsotTests
{
    [Test]
    public void TemplateMainGraph_ReplacesPresetDefaultMain()
    {
        using World world = World.Create();
        Entity target = world.Create(new BlackboardFloatBuffer());
        Entity caster = world.Create();

        const int presetMainGraphId = 701;
        const int templateMainGraphId = 702;
        var programs = new GraphProgramRegistry();
        programs.Register(presetMainGraphId, CreateBlackboardWriteProgram(999f), GraphKind.Effect);
        programs.Register(templateMainGraphId, CreateBlackboardWriteProgram(42f), GraphKind.Effect);

        var presets = new PresetTypeRegistry();
        var preset = new PresetTypeDefinition { Type = EffectPresetType.Buff };
        preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(presetMainGraphId);
        presets.Register(in preset);

        var behavior = new EffectPhaseGraphBindings();
        Assert.That(
            behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, templateMainGraphId),
            Is.True);

        var executor = new EffectPhaseExecutor(
            programs,
            presets,
            new BuiltinHandlerRegistry(),
            GasGraphOpHandlerTable.Instance,
            new EffectTemplateRegistry());
        var api = new GasGraphRuntimeApi(world);

        executor.ExecutePhase(
            world,
            api,
            caster,
            target,
            default,
            default,
            EffectPhaseId.OnApply,
            in behavior,
            EffectPresetType.Buff);

        ref BlackboardFloatBuffer blackboard = ref world.Get<BlackboardFloatBuffer>(target);
        Assert.That(blackboard.TryGet(1, out float value), Is.True);
        Assert.That(value, Is.EqualTo(42f));
    }

    [Test]
    public void UncertifiedRevealArea_IsNotPublishedAsPresetOrFormalGraph()
    {
        Assert.That(Enum.GetNames<EffectPresetType>(), Does.Not.Contain("RevealArea"));

        string repoRoot = FindRepoRoot();
        JsonArray presets = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "assets", "Configs", "GAS", "preset_types.json")))!.AsArray();
        Assert.That(
            presets.Select(node => node!["id"]!.GetValue<string>()),
            Does.Not.Contain("RevealArea"));

        JsonArray graphs = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "assets", "Configs", "GAS", "graphs.json")))!.AsArray();
        string[] graphIds = graphs.Select(node => node!["id"]!.GetValue<string>()).ToArray();
        Assert.That(graphIds, Does.Not.Contain("Graph.Vision.RevealArea"));
        Assert.That(graphIds, Does.Not.Contain("Graph.Vision.DecayRevealArea"));

        var builtinHandlers = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(builtinHandlers);
        Assert.That(
            builtinHandlers.TryGetOperationMetadata(
                BuiltinHandlerId.RevealArea,
                out EffectOperationMetadata revealMetadata),
            Is.True);
        Assert.That(revealMetadata.Kind, Is.EqualTo(EffectOperationKind.Unsupported));
        Assert.That(revealMetadata.Domain, Is.EqualTo(EffectAtomicDomain.Vision));
    }

    private static GraphInstruction[] CreateBlackboardWriteProgram(float value)
    {
        return
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = value },
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 }
        ];
    }

    private static string FindRepoRoot()
    {
        string? directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "assets", "Configs", "GAS", "preset_types.json")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Ludots repository root.");
    }
}
