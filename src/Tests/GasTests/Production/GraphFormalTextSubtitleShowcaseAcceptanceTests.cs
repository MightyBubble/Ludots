using System;
using System.IO;
using System.Numerics;
using CapabilityStandardGraphFormalTextShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class GraphFormalTextSubtitleShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    [Test]
    public void GraphFormalTextShowcase_ComposedSubtitlesAppearOnOverlay()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(GraphFormalTextShowcaseContract.MapId);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(
            engine.CurrentMapSession?.MapConfig?.Tags,
            Does.Contain(GraphFormalTextShowcaseContract.MapId));

        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("拼句字幕短剧需要屏幕字幕缓冲。");

        TickUntilOverlayHas(engine, overlay, GraphFormalTextShowcaseContract.FixedSentence, maxFrames: 30);
        Assert.Multiple(() =>
        {
            Assert.That(OverlayContainsText(overlay, GraphFormalTextShowcaseContract.PlayerTitle), Is.True);
            Assert.That(OverlayContainsText(overlay, GraphFormalTextShowcaseContract.FixedSentence), Is.True);
            Assert.That(OverlayContainsText(overlay, GraphFormalTextShowcaseContract.CountSentence), Is.True);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        });

        WriteOverlayEvidence(overlay);
    }

    private static void WriteOverlayEvidence(ScreenOverlayBuffer overlay)
    {
        string repoRoot = FindRepoRoot();
        string dir = Path.Combine(repoRoot, "artifacts", "evidence", "capability_standard_graph_formal_text");
        Directory.CreateDirectory(dir);
        var lines = new List<string>
        {
            "showcase=capability_standard_graph_formal_text",
            $"title={GraphFormalTextShowcaseContract.PlayerTitle}",
            "source=ScreenOverlayBuffer",
        };
        foreach (ref readonly var item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text))
            {
                lines.Add($"overlay={text}");
            }
        }

        File.WriteAllLines(Path.Combine(dir, "overlay-captions.txt"), lines);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "CoreInputMod",
                "CapabilityStandardGraphFormalTextShowcaseMod"
            }),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);
        return engine;
    }

    private static void TickUntilOverlayHas(
        GameEngine engine,
        ScreenOverlayBuffer overlay,
        string expected,
        int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (OverlayContainsText(overlay, expected) &&
                OverlayContainsText(overlay, GraphFormalTextShowcaseContract.CountSentence))
            {
                return;
            }
        }

        Assert.Fail(
            $"拼句字幕短剧在 {maxFrames} 帧内没有同时出现「{GraphFormalTextShowcaseContract.FixedSentence}」与「{GraphFormalTextShowcaseContract.CountSentence}」。");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        GasClockStepPolicy stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            engine.Tick(DeltaTime);
        }
    }

    private static bool OverlayContainsText(ScreenOverlayBuffer overlay, string expected)
    {
        foreach (ref readonly var item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text) &&
                text.Contains(expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
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
}
