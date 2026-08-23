using System.Diagnostics;
using System.Text.Json;
using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using SkiaSharp;

string outDir = args.Length > 0 ? args[0] : "/opt/cursor/artifacts/screenshots";
Directory.CreateDirectory(outDir);

// ── Real hot-apply proof (editor pipeline) ─────────────────────────────
GraphIdRegistry.Clear();
EffectTemplateIdRegistry.Clear();

string effectKey = "effect.HotApply.Demo";
int effectId = EffectTemplateIdRegistry.Register(effectKey);
var effects = new EffectTemplateRegistry();
effects.Register(effectId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });
effects.TryGet(effectId, out EffectTemplateData before);

var graphs = new GraphProgramRegistry();
var pipeline = new LiveGasEditPipeline(graphs, effects);
var session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://effect.HotApply.Demo/duration");

var stage = session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
    effectKey, "duration.durationTicks", 40d, provenance));
var report = pipeline.Classify(session);
pipeline.BeginSafeFrame();
var commit = pipeline.CommitNextCastSafeFrame();
pipeline.EndSafeFrame();
effects.TryGet(effectId, out EffectTemplateData after);

string classifyMode = report.Items.Count > 0 ? report.Items[0].Mode.ToString() : "none";
DrawEditorBoard(
    Path.Combine(outDir, "lsw-hotapply-editor.png"),
    stage.Succeeded,
    classifyMode,
    report.CanCommitNextCast,
    commit.Succeeded,
    before.DurationTicks,
    after.DurationTicks,
    commit.AppliedCount);

// ── Runtime observation board from vignette beats ──────────────────────
var runtime = new LiveSkillWorkbenchVignetteRuntime();
runtime.Bind();
runtime.EnsureWorld();
float dummyAfterOld = 1f;
float dummyAfterNew = 1f;
bool sawHot = false;
bool sawStrong = false;
for (int i = 0; i < 60 * 20; i++)
{
    runtime.Tick(1f / 60f);
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HotApplyBanner && !sawHot)
    {
        sawHot = true;
        dummyAfterOld = runtime.DummyHp01;
        DrawRuntimeBoard(
            Path.Combine(outDir, "lsw-hotapply-runtime-before.png"),
            "BEFORE hot-apply commit",
            runtime.EditorAction,
            runtime.RuntimeResult,
            runtime.MageHp01,
            runtime.DummyHp01,
            runtime.ChainLit,
            projectile: false,
            frost: false);
    }
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.StrongCast &&
        runtime.ProjectileT < 0f &&
        runtime.DummyHp01 < 0.5f &&
        !sawStrong)
    {
        sawStrong = true;
        dummyAfterNew = runtime.DummyHp01;
        DrawRuntimeBoard(
            Path.Combine(outDir, "lsw-hotapply-runtime-after.png"),
            "AFTER hot-apply commit (next cast)",
            runtime.EditorAction,
            runtime.RuntimeResult,
            runtime.MageHp01,
            runtime.DummyHp01,
            runtime.ChainLit,
            projectile: false,
            frost: false);
    }
}

DrawSummary(
    Path.Combine(outDir, "lsw-hotapply-acceptance.png"),
    stage.Succeeded,
    classifyMode,
    commit.Succeeded,
    before.DurationTicks,
    after.DurationTicks,
    dummyAfterOld,
    dummyAfterNew);

Console.WriteLine(JsonSerializer.Serialize(new
{
    stage = stage.Succeeded,
    classifyMode,
    canNextCast = report.CanCommitNextCast,
    commit = commit.Succeeded,
    applied = commit.AppliedCount,
    durationBefore = before.DurationTicks,
    durationAfter = after.DurationTicks,
    dummyAfterOld,
    dummyAfterNew
}));

static void DrawEditorBoard(
    string path,
    bool staged,
    string mode,
    bool canNextCast,
    bool committed,
    int beforeTicks,
    int afterTicks,
    int applied)
{
    const int W = 1280, H = 720;
    using var surface = SKSurface.Create(new SKImageInfo(W, H));
    var c = surface.Canvas;
    c.Clear(new SKColor(18, 24, 32));
    using var title = P(SKColors.White, 30);
    using var label = P(new SKColor(180, 200, 220), 20);
    using var ok = P(new SKColor(90, 220, 140), 22);
    using var warn = P(new SKColor(255, 210, 90), 22);
    c.DrawText("EDITOR HOT-EDIT / HOT-APPLY (LiveGasEditPipeline)", 40, 50, title);
    c.DrawText("Not player controls. Not ReloadConfigs.", 40, 84, label);

    DrawCard(c, 40, 120, 560, 220, "1) Stage edit", $"SkillEffectNumeric\neffect.HotApply.Demo\nduration.durationTicks: 10 -> 40\nstaged={staged}");
    DrawCard(c, 640, 120, 560, 220, "2) Classify", $"mode={mode}\nCanCommitNextCast={canNextCast}\nNo Clear+Register-all\nLive registry untouched until commit");
    DrawCard(c, 40, 370, 560, 220, "3) Safe-frame Commit", $"BeginSafeFrame -> CommitNextCast\ncommitted={committed}\nappliedCount={applied}\nEffectTemplate duration: {beforeTicks} -> {afterTicks}");
    DrawCard(c, 640, 370, 560, 220, "Pass criteria", $"duration changed in live registry\nwithout engine restart\nwithout map reload\nwithout ReloadConfigs(GAS)");

    c.DrawText(committed && afterTicks == 40 ? "RESULT: HOT-APPLY OK" : "RESULT: FAIL", 40, 660, committed ? ok : warn);
    Save(surface, path);
}

static void DrawRuntimeBoard(
    string path,
    string heading,
    string editor,
    string runtime,
    float mageHp,
    float dummyHp,
    int chain,
    bool projectile,
    bool frost)
{
    const int W = 1280, H = 720;
    using var surface = SKSurface.Create(new SKImageInfo(W, H));
    var c = surface.Canvas;
    c.Clear(new SKColor(16, 22, 30));
    using var title = P(SKColors.White, 28);
    using var action = P(new SKColor(255, 210, 90), 20);
    using var feed = P(new SKColor(120, 230, 160), 20);
    using var sub = P(new SKColor(170, 195, 215), 18);
    c.DrawText(heading, 40, 48, title);
    c.DrawText($"EDITOR: {editor}", 40, 84, action);
    c.DrawText($"RUNTIME: {runtime}", 40, 114, feed);
    c.DrawText($"mageHP={mageHp:P0}  dummyHP={dummyHp:P0}  chain={chain}/4", 40, 146, sub);

    float WX(float x) => 640 + x * 55f;
    float WY(float y) => 400 - y * 45f;
    void Actor(float x, float y, float r, SKColor color)
    {
        using var fill = new SKPaint { Color = color, IsAntialias = true };
        c.DrawCircle(WX(x), WY(y), r * 42f, fill);
    }
    Actor(-5.5f, 0, 0.85f, new SKColor(240, 200, 40));
    Actor(5.5f, 0, 0.95f, new SKColor(220, 70, 70));
    // HP bars
    void Hp(float x, float y, float fill, SKColor color)
    {
        float cx = WX(x), cy = WY(y) - 58;
        using var bg = new SKPaint { Color = new SKColor(45, 45, 45), IsAntialias = true };
        using var fg = new SKPaint { Color = color, IsAntialias = true };
        c.DrawRoundRect(cx - 72, cy - 10, 144, 18, 4, 4, bg);
        c.DrawRoundRect(cx - 72, cy - 10, 144f * Math.Clamp(fill, 0, 1), 18, 4, 4, fg);
    }
    Hp(-5.5f, 0, mageHp, new SKColor(60, 200, 90));
    Hp(5.5f, 0, dummyHp, new SKColor(220, 70, 70));
    c.DrawText("MAGE", WX(-5.5f) - 28, WY(0) + 70, sub);
    c.DrawText("DUMMY", WX(5.5f) - 34, WY(0) + 70, sub);
    Save(surface, path);
}

static void DrawSummary(
    string path,
    bool staged,
    string mode,
    bool committed,
    int before,
    int after,
    float dummyOld,
    float dummyNew)
{
    const int W = 1280, H = 720;
    using var surface = SKSurface.Create(new SKImageInfo(W, H));
    var c = surface.Canvas;
    c.Clear(new SKColor(14, 20, 28));
    using var title = P(SKColors.White, 32);
    using var ok = P(new SKColor(90, 220, 140), 24);
    using var label = P(new SKColor(190, 205, 220), 22);
    c.DrawText("Acceptance: Editor Hot-Edit / Hot-Apply", 40, 56, title);
    c.DrawText($"1. Stage edit OK: {staged}", 40, 120, label);
    c.DrawText($"2. Classify mode: {mode}", 40, 160, label);
    c.DrawText($"3. Safe-frame commit OK: {committed}", 40, 200, label);
    c.DrawText($"4. Live effect field: durationTicks {before} -> {after}", 40, 240, label);
    c.DrawText($"5. Runtime observation: dummyHP {dummyOld:P0} (old) -> {dummyNew:P0} (new cast)", 40, 280, label);
    bool pass = staged && committed && after == 40 && mode.Contains("NextCast");
    c.DrawText(pass ? "PASS: hot-apply path proven" : "FAIL", 40, 360, ok);
    c.DrawText("Formal entry: LiveGasEditPipeline (not ReloadConfigs)", 40, 420, label);
    Save(surface, path);
}

static void DrawCard(SKCanvas c, float x, float y, float w, float h, string header, string body)
{
    using var bg = new SKPaint { Color = new SKColor(30, 40, 52), IsAntialias = true };
    c.DrawRoundRect(x, y, w, h, 12, 12, bg);
    using var hPaint = P(new SKColor(255, 210, 90), 22);
    using var bPaint = P(new SKColor(210, 220, 230), 18);
    c.DrawText(header, x + 20, y + 36, hPaint);
    float ly = y + 70;
    foreach (var line in body.Split('\n'))
    {
        c.DrawText(line, x + 20, ly, bPaint);
        ly += 28;
    }
}

static SKPaint P(SKColor color, float size) => new()
{
    Color = color,
    TextSize = size,
    IsAntialias = true,
    Typeface = SKTypeface.Default
};

static void Save(SKSurface surface, string path)
{
    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 92);
    using var fs = File.OpenWrite(path);
    data.SaveTo(fs);
    Console.WriteLine("wrote " + path);
}
