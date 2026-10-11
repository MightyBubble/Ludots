using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class TcgPromptShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "tcg_prompt";
    private const string Seat = "seat.0";
    private const string Scheme = "scheme.tcg_prompt";
    private const string HeroId = "tcg-prompt-hero";
    private const string MageId = "tcg-prompt-mage";
    private const string DuelProfile = "interaction.context.tcg_prompt.duel";
    private const string PromptProfile = "interaction.context.tcg_prompt.prompt";
    private const string HintPanel = "panel.tcg_prompt.turn_hint";
    private const string PromptPanel = "panel.tcg_prompt.prompt";
    private const string HeroStatusPanel = "panel.tcg_prompt.hero_status";
    private const string MageStatusPanel = "panel.tcg_prompt.mage_status";
    private const string EndTurn = "TcgPrompt.EndTurn";
    private const string Pass = "TcgPrompt.Pass";
    private const string Negate = "TcgPrompt.Negate";
    private const string Activate = "TcgPrompt.Activate";
    private static readonly string ArtifactDir = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "tcg_prompt");

    [Test]
    public void EnterTable_SeesOwnStatusEnemyStatusAndTurnHint_NoPrompt()
    {
        using var scene = Scene.Load();

        Assert.That(scene.Activation.IsVisible(HeroStatusPanel), Is.True, "左上角是你的英雄");
        Assert.That(scene.Activation.IsVisible(MageStatusPanel), Is.True, "右上角是敌方法师");
        Assert.That(scene.Activation.IsVisible(HintPanel), Is.True, "底部提示：按 E 结束回合");
        Assert.That(scene.Activation.IsVisible(PromptPanel), Is.False, "没人出手时不问你");
        Assert.That(scene.Prompt.IsOpen, Is.False);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(100f));
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(100f));
        Assert.That(scene.HasContext(scene.Hero, DuelProfile), Is.True, "英雄一进场就在对局情境里");
        Assert.That(scene.UiText(), Has.Some.Contains("按 E 结束回合"));
        scene.Capture(1, "enter-table", "进场", "左上你的英雄 100 血、右上敌方法师 100 血、底部提示按 E 结束回合");
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void EndTurn_MageCastsFireball_TableStopsAndAsksYou()
    {
        using var scene = Scene.Load();

        scene.Press(EndTurn);
        scene.TickUntil(30, () => scene.Activation.IsVisible(PromptPanel));

        Assert.That(scene.Prompt.IsOpen, Is.True, "法师的火球触发了你的陷阱监听，牌桌停下来");
        Assert.That(scene.Prompt.PlayerId, Is.EqualTo(1), "问的是你（陷阱的主人），不是法师");
        Assert.That(scene.Prompt.Responder, Is.EqualTo(scene.Hero));
        Assert.That(scene.Prompt.WindowSource, Is.EqualTo(scene.Mage), "火球是法师放的");
        Assert.That(scene.Prompt.WindowTarget, Is.EqualTo(scene.Hero), "火球冲你来");
        Assert.That(scene.HasContext(scene.Hero, PromptProfile), Is.True, "英雄进入“要不要接招”情境");
        Assert.That(scene.Activation.IsVisible(PromptPanel), Is.True, "底部换成三个选项");
        Assert.That(scene.Activation.IsVisible(HintPanel), Is.False, "回合提示先收起");
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(100f), "你还没回答，火球悬在半空");
        IReadOnlyList<string> text = scene.UiText();
        Assert.That(text, Has.Some.Contains("要不要接招"));
        Assert.That(text, Has.Some.Contains("空格"));
        Assert.That(text, Has.Some.Contains("N："));
        Assert.That(text, Has.Some.Contains("1：翻开陷阱【反击】"));
        scene.Capture(2, "asked", "按 E 之后", "法师朝你放火球，牌桌停下，底部列出空格 / N / 1 三个选项");

        scene.Press(EndTurn);
        Assert.That(scene.Prompt.IsOpen, Is.True, "问你的时候再按 E 不会插队");
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(100f));
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void AnswerPass_FireballLands_Hero70()
    {
        using var scene = Scene.Load();
        scene.OpenPrompt();

        scene.Press(Pass);
        scene.TickUntil(30, () => !scene.Prompt.IsOpen && scene.Activation.IsVisible(HintPanel));

        AssertBackToTurn(scene);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(70f), "让过：火球照打，吃下 30");
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(100f));
        scene.Capture(3, "pass", "按空格", "你让过，火球落地：你的英雄 100 → 70，法师不变");
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void AnswerNegate_FireballVoided_Hero100()
    {
        using var scene = Scene.Load();
        scene.OpenPrompt();

        scene.Press(Negate);
        scene.TickUntil(30, () => !scene.Prompt.IsOpen && scene.Activation.IsVisible(HintPanel));
        scene.Tick(10);

        AssertBackToTurn(scene);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(100f), "无效：火球作废，你不掉血");
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(100f));
        scene.Capture(4, "negate", "按 N", "你打出无效，火球作废：双方都还是 100");
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void AnswerActivate_TrapFires_Hero70Mage85()
    {
        using var scene = Scene.Load();
        scene.OpenPrompt();

        scene.Press(Activate);
        scene.TickUntil(30, () => !scene.Prompt.IsOpen && scene.Activation.IsVisible(HintPanel));
        scene.Tick(10);

        AssertBackToTurn(scene);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(70f), "反击不挡火球：你照样吃 30");
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(85f), "陷阱反打出手的法师 15");
        scene.Capture(5, "activate", "按 1", "你翻开反击：你 100 → 70，法师 100 → 85");
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void AnswerKeys_WithoutPrompt_DoNothing()
    {
        using var scene = Scene.Load();

        scene.Press(Pass);
        scene.Press(Negate);
        scene.Press(Activate);
        scene.Tick(10);

        Assert.That(scene.Prompt.IsOpen, Is.False);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(100f), "没人问你时，空格 / N / 1 什么都不发生");
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(100f));
        Assert.That(scene.Activation.IsVisible(HintPanel), Is.True);
        scene.AssertNoTriggerErrors();
    }

    [Test]
    public void SecondRound_AskedAgain_AnswerOnlyCountsOnce()
    {
        using var scene = Scene.Load();
        scene.OpenPrompt();
        scene.Press(Activate);
        scene.TickUntil(30, () => !scene.Prompt.IsOpen && scene.Activation.IsVisible(HintPanel));
        scene.Tick(10);

        scene.OpenPrompt();
        scene.Press(Pass);
        scene.Press(Activate);
        scene.TickUntil(30, () => !scene.Prompt.IsOpen && scene.Activation.IsVisible(HintPanel));
        scene.Tick(10);

        AssertBackToTurn(scene);
        Assert.That(scene.Health(scene.Hero), Is.EqualTo(40f), "两轮各吃一发火球");
        Assert.That(scene.Health(scene.Mage), Is.EqualTo(85f), "第二轮先让过，窗口已关，后按的 1 不算数");
        scene.AssertNoTriggerErrors();
    }

    private static void AssertBackToTurn(Scene scene)
    {
        Assert.That(scene.Prompt.IsOpen, Is.False, "回答后窗口关闭");
        Assert.That(scene.HasContext(scene.Hero, PromptProfile), Is.False, "英雄退出“要不要接招”情境");
        Assert.That(scene.HasContext(scene.Hero, DuelProfile), Is.True, "回到对局情境，可以再按 E");
        Assert.That(scene.Activation.IsVisible(PromptPanel), Is.False, "三个选项收起");
        Assert.That(scene.Activation.IsVisible(HintPanel), Is.True, "回合提示回来");
    }

    private sealed class Scene : IDisposable
    {
        private readonly GameEngine _engine;
        private readonly PlayerInputHandler _input;
        private readonly UIRoot _ui;
        private readonly InteractionContextProfileRegistry _profiles;

        private Scene(GameEngine engine, PlayerInputHandler input, UIRoot ui)
        {
            _engine = engine;
            _input = input;
            _ui = ui;
            _profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("InteractionContextProfileRegistry service is missing.");
            Prompt = engine.GetService(CoreServiceKeys.ResponseChainPromptState)
                ?? throw new InvalidOperationException("ResponseChainPromptState service is missing.");
            Activation = engine.GetService(CoreServiceKeys.PanelActivationStore)
                ?? throw new InvalidOperationException("PanelActivationStore service is missing.");
            Hero = Resolve(HeroId);
            Mage = Resolve(MageId);
        }

        public ResponseChainPromptState Prompt { get; }
        public UiPanelActivationStore Activation { get; }
        public Entity Hero { get; }
        public Entity Mage { get; }

        public static Scene Load()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "TcgDemoMod", "TcgPromptShowcaseMod" }),
                Path.Combine(repoRoot, "assets"));
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)new NullInputBackend());
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            UIRoot ui = AcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
            engine.Start();
            engine.LoadMap(new MapLoadRequest(new MapId(MapId),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding(Seat, 1, Scheme) })));
            for (int i = 0; i < 8; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }

            return new Scene(engine, inputHandler, ui);
        }

        public void OpenPrompt()
        {
            Press(EndTurn);
            TickUntil(30, () => Activation.IsVisible(PromptPanel));
            Assert.That(Prompt.IsOpen && HasContext(Hero, PromptProfile) && Activation.IsVisible(PromptPanel), Is.True,
                "按 E 后法师出手，牌桌停下来问你");
        }

        public void Press(string actionId)
        {
            _input.InjectButtonPress(actionId);
            Tick(2);
            _input.InjectButtonRelease(actionId);
            Tick(1);
        }

        public void Tick(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _engine.SetService(CoreServiceKeys.UiCaptured, false);
                _engine.Tick(DeltaTime);
                AssertDrawnItemsHaveOwners();
            }
        }

        private void AssertDrawnItemsHaveOwners()
        {
            PrimitiveDrawBuffer draws = _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer service is missing.");
            foreach (ref readonly PrimitiveDrawItem item in draws.GetSpan())
            {
                Assert.That(item.StableId <= 0 || item.OwnerStableId > 0, Is.True,
                    $"画面上的东西 stableId={item.StableId} mesh={item.MeshAssetId} 没有主人，Raylib 提交回执会直接崩");
            }
        }

        public void TickUntil(int maxFrames, Func<bool> condition)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
            {
                Tick(1);
            }
        }

        public float Health(Entity entity) =>
            _engine.World.Get<AttributeBuffer>(entity).GetCurrent(AttributeRegistry.GetId("Health"));

        public bool HasContext(Entity rep, string profileId)
        {
            int id = _profiles.ProfileIdRegistry.GetId(profileId);
            if (_engine.World.TryGet(rep, out InteractionContextInstance root) && root.ContextId == id)
            {
                return true;
            }

            if (!_engine.World.TryGet(rep, out InteractionContextInstances derived))
            {
                return false;
            }

            for (int i = 0; i < derived.Count; i++)
            {
                if (derived[i].ContextId == id)
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<string> UiText()
        {
            _ui.Scene!.Layout(_ui.Width, _ui.Height);
            return AcceptanceUiEvidenceWriter.ExtractUiText(_ui);
        }

        public void Capture(int order, string step, string when, string what)
        {
            string screens = Path.Combine(ArtifactDir, "screens");
            Directory.CreateDirectory(screens);
            _ui.Scene!.Layout(_ui.Width, _ui.Height);
            AcceptanceUiEvidenceWriter.CaptureFrame(_ui, screens, order, step, when, "玩家（seat.0，你的英雄）", what,
                "tcg_prompt 牌桌", "新手看懂响应窗：别人出手时牌桌停下来问你", "E 结束回合；空格 / N / 1 回答");
        }

        public void AssertNoTriggerErrors()
        {
            Assert.That(_engine.TriggerManager.Errors.Count, Is.EqualTo(0), string.Join(" | ", _engine.TriggerManager.Errors));
        }

        public void Dispose() => _engine.Dispose();

        private Entity Resolve(string instanceId)
        {
            MapSession session = _engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
            return session.EntityIndex.GetRequired(session.MapId.Value, instanceId, nameof(TcgPromptShowcaseAcceptanceTests));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "mods")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
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
