using System.Diagnostics;
using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.GraphRuntime;
using SkiaSharp;

string outDir = args.Length > 0 ? args[0] : "/opt/cursor/artifacts/screenshots";
Directory.CreateDirectory(outDir);

var runtime = new LiveSkillWorkbenchVignetteRuntime();
runtime.Bind(new GraphProgramRegistry(), new LiveGasEditPipeline(new GraphProgramRegistry()), new LiveEffectChainTracer(64));
runtime.EnsureWorld();

var captured = new HashSet<string>();
void TryCapture(string key)
{
    if (!captured.Add(key)) return;
    string path = Path.Combine(outDir, $"lsw-scene-{key}.png");
    DrawFrame(runtime, path, key);
    Console.WriteLine($"wrote {path} beat={runtime.CurrentBeat} dummyHp={runtime.DummyHp01:0.00} mageHp={runtime.MageHp01:0.00}");
}

for (int i = 0; i < 60 * 30; i++)
{
    runtime.Tick(1f / 60f);
    if (runtime.ProjectileT is >= 0.35f and <= 0.7f && !runtime.ProjectileFrost)
    {
        if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.WeakCast)
            TryCapture("01-weak-fireball");
        if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.StrongCast)
            TryCapture("03-strong-fireball");
    }
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HotApplyBanner)
        TryCapture("02-hot-apply");
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage && runtime.MageHp01 > 0.95f)
        TryCapture("04-mage-healed");
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.EffectChain && runtime.ChainLit >= 3)
        TryCapture("05-effect-chain");
    if (runtime.ProjectileFrost && runtime.ProjectileT is >= 0.3f and <= 0.7f)
        TryCapture("06-frost-draft");
    if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.LoopHold)
        TryCapture("07-done");
}

var pngs = Directory.GetFiles(outDir, "lsw-scene-*.png").OrderBy(p => p).ToArray();
string listFile = Path.Combine(outDir, "lsw-scene-frames.txt");
using (var sw = new StreamWriter(listFile))
{
    foreach (var p in pngs)
    {
        sw.WriteLine($"file '{p}'");
        sw.WriteLine("duration 2.4");
    }
    if (pngs.Length > 0) sw.WriteLine($"file '{pngs[^1]}'");
}

string mp4 = Path.Combine(outDir, "lsw-scene-vignette.mp4");
var psi = new ProcessStartInfo
{
    FileName = "ffmpeg",
    Arguments = $"-y -f concat -safe 0 -i \"{listFile}\" -vf scale=1280:720 -pix_fmt yuv420p \"{mp4}\"",
    RedirectStandardError = true,
    RedirectStandardOutput = true
};
using var proc = Process.Start(psi)!;
proc.WaitForExit();
Console.WriteLine($"ffmpeg={proc.ExitCode} frames={pngs.Length} mp4={mp4}");

static void DrawFrame(LiveSkillWorkbenchVignetteRuntime rt, string path, string caption)
{
    const int W = 1280, H = 720;
    using var surface = SKSurface.Create(new SKImageInfo(W, H));
    var c = surface.Canvas;
    c.Clear(new SKColor(16, 22, 30));

    using var title = new SKPaint { Color = SKColors.White, TextSize = 30, IsAntialias = true, Typeface = SKTypeface.Default };
    using var sub = new SKPaint { Color = new SKColor(170, 195, 215), TextSize = 20, IsAntialias = true, Typeface = SKTypeface.Default };
    c.DrawText("Live Skill Workbench - Playable Vignette (in-world)", 36, 46, title);
    c.DrawText(ToAsciiBanner(rt.Banner), 36, 78, sub);
    c.DrawText($"beat={rt.CurrentBeat}  mageHP={rt.MageHp01:P0}  dummyHP={rt.DummyHp01:P0}  chain={rt.ChainLit}/4", 36, 108, sub);

    float WX(float x) => 640 + x * 55f;
    float WY(float y) => 390 - y * 45f;

    using var lane = new SKPaint { Color = new SKColor(80, 90, 105), StrokeWidth = 4, IsAntialias = true, Style = SKPaintStyle.Stroke };
    c.DrawLine(WX(-8), WY(-2.2f), WX(8), WY(-2.2f), lane);

    void Actor(float x, float y, float r, SKColor color)
    {
        using var fill = new SKPaint { Color = color, IsAntialias = true };
        using var ring = new SKPaint { Color = SKColors.White, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
        c.DrawCircle(WX(x), WY(y), r * 42f, fill);
        c.DrawCircle(WX(x), WY(y), r * 42f, ring);
    }

    void Hp(float x, float y, float fill, SKColor color)
    {
        float cx = WX(x), cy = WY(y) - 58;
        using var bg = new SKPaint { Color = new SKColor(45, 45, 45), IsAntialias = true };
        using var fg = new SKPaint { Color = color, IsAntialias = true };
        c.DrawRoundRect(cx - 72, cy - 10, 144, 18, 4, 4, bg);
        c.DrawRoundRect(cx - 72, cy - 10, 144 * Math.Clamp(fill, 0, 1), 18, 4, 4, fg);
    }

    Actor(rt.MageX, rt.MageY, 0.85f, new SKColor(240, 200, 40));
    Actor(rt.DummyX, rt.DummyY, 0.95f, new SKColor(220, 70, 70));
    Hp(rt.MageX, rt.MageY, rt.MageHp01, new SKColor(60, 200, 90));
    Hp(rt.DummyX, rt.DummyY, rt.DummyHp01, new SKColor(220, 70, 70));
    c.DrawText("MAGE", WX(rt.MageX) - 28, WY(rt.MageY) + 70, sub);
    c.DrawText("DUMMY", WX(rt.DummyX) - 34, WY(rt.DummyY) + 70, sub);

    if (rt.ProjectileT >= 0f)
    {
        rt.GetProjectilePos(out float px, out float py);
        var col = rt.ProjectileFrost ? new SKColor(80, 220, 255) : new SKColor(255, 170, 40);
        Actor(px, py, rt.ProjectileFrost ? 0.35f : 0.45f, col);
        using var beam = new SKPaint { Color = col.WithAlpha(170), StrokeWidth = 3, IsAntialias = true };
        c.DrawLine(WX(rt.MageX), WY(rt.MageY), WX(rt.DummyX), WY(rt.DummyY), beam);
        c.DrawText(rt.ProjectileFrost ? "FROST DRAFT" : "FIREBALL", WX(px) - 40, WY(py) - 40, sub);
    }

    string[] pips = { "CAST", "EFFECT", "ATTR", "RESP" };
    for (int i = 0; i < 4; i++)
    {
        bool on = i < rt.ChainLit;
        float x = 300 + i * 170, y = 630;
        using var pip = new SKPaint { Color = on ? new SKColor(240, 200, 60) : new SKColor(70, 70, 70), IsAntialias = true };
        c.DrawRoundRect(x, y, 140, 40, 8, 8, pip);
        using var t = new SKPaint { Color = on ? SKColors.Black : SKColors.LightGray, TextSize = 18, IsAntialias = true, Typeface = SKTypeface.Default };
        c.DrawText(pips[i], x + 36, y + 26, t);
    }

    using var badge = new SKPaint { Color = new SKColor(28, 120, 80), IsAntialias = true };
    c.DrawRoundRect(36, H - 64, 520, 36, 8, 8, badge);
    c.DrawText(caption, 50, H - 38, sub);

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 92);
    using var fs = File.OpenWrite(path);
    data.SaveTo(fs);
}

static string ToAsciiBanner(string banner)
{
    // Keep player-readable English phase titles even if source banner is Chinese.
    if (banner.Contains('①') || banner.Contains("弱火球")) return "1) Weak fireball - dummy loses HP";
    if (banner.Contains('②') || banner.Contains("热应用")) return "2) Hot-apply - damage upgraded for next cast";
    if (banner.Contains('③') || banner.Contains("强火球")) return "3) Strong fireball - big HP drop";
    if (banner.Contains('④') || banner.Contains("属性调试")) return "4) Attribute debug - mage HP refilled now";
    if (banner.Contains('⑤') || banner.Contains("效果链")) return "5) Effect-chain lights: cast/effect/attr/response";
    if (banner.Contains('⑥') || banner.Contains("冰冻")) return "6) AI frost draft playtest - cyan shot";
    if (banner.Contains("循环")) return "Loop complete - hot-apply demo finished";
    return banner;
}
