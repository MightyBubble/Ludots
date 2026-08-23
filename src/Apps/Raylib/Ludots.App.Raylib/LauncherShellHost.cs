using System.Diagnostics;
using Ludots.Adapter.Raylib;
using Ludots.Launcher.Backend;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.Raylib;

/// <summary>
/// Adapter 生命周期的 ShellMode 前厅：无 GameEngine、无 mod 装载的 preset 选择界面。
/// 首个游戏会话经 PrepareLaunchAsync 在本进程内引导；游戏结束后中继重启回 Shell。
/// </summary>
internal static class LauncherShellHost
{
    private const int WindowWidth = 1024;
    private const int WindowHeight = 640;
    private const int RowHeight = 26;
    private const int ListTop = 110;
    private const int VisibleRows = 18;
    private const int FontSize = 20;

    public static void Run(string baseDir)
    {
        var repoRoot = LauncherService.FindRepoRoot(baseDir);
        var launcher = new LauncherService(repoRoot);
        var presets = launcher.GetState()
            .Presets
            .Where(preset => string.Equals(preset.AdapterId, LauncherPlatformIds.Raylib, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var selected = 0;
        var scroll = 0;
        var status = presets.Count > 0
            ? "Up/Down select - Enter launch - Esc exit"
            : "No raylib presets found in launcher.presets.json";
        Task<LauncherPrepareResult>? prepare = null;
        var preparing = string.Empty;

        Rl.InitWindow(WindowWidth, WindowHeight, "Ludots Launcher");
        Rl.SetTargetFPS(30);
        while (!Rl.WindowShouldClose())
        {
            if (prepare is null)
            {
                if (presets.Count > 0)
                {
                    if (Rl.IsKeyPressed(KeyboardKey.KEY_DOWN))
                    {
                        selected = Math.Min(selected + 1, presets.Count - 1);
                        scroll = ClampScroll(selected, scroll);
                    }
                    if (Rl.IsKeyPressed(KeyboardKey.KEY_UP))
                    {
                        selected = Math.Max(selected - 1, 0);
                        scroll = ClampScroll(selected, scroll);
                    }
                    if (Rl.IsKeyPressed(KeyboardKey.KEY_ENTER))
                    {
                        var preset = presets[selected];
                        preparing = preset.Name;
                        status = $"Preparing '{preset.Name}' (resolve + build)...";
                        prepare = launcher.PrepareLaunchAsync(preset.Selectors, preset.AdapterId, ParseBuildMode(preset.BuildMode));
                    }
                }
                if (Rl.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                {
                    break;
                }
            }
            else if (prepare.IsCompleted)
            {
                var result = prepare.Result;
                prepare = null;
                if (result.Ok && result.Plan is not null)
                {
                    LaunchGameSession(baseDir, result.BootstrapPath);
                    return;
                }
                status = $"Launch failed: {FirstLine(result.Error)}";
            }

            Rl.BeginDrawing();
            Rl.ClearBackground(new Color(24, 26, 32, 255));
            Rl.DrawText("LUDOTS", 24, 28, 32, new Color(120, 220, 160, 255));
            Rl.DrawText($"presets (raylib): {presets.Count}   selected {selected + 1}/{presets.Count}", 24, 70, FontSize, Color.LIGHTGRAY);
            for (var row = 0; row < Math.Min(VisibleRows, presets.Count - scroll); row++)
            {
                var index = scroll + row;
                var preset = presets[index];
                var y = ListTop + row * RowHeight;
                if (index == selected)
                {
                    Rl.DrawRectangle(20, y - 3, WindowWidth - 40, RowHeight, new Color(60, 70, 60, 255));
                }
                var label = $"{preset.Name}  [{string.Join(", ", preset.Selectors)}]";
                Rl.DrawText(label, 28, y, FontSize, index == selected ? Color.WHITE : Color.LIGHTGRAY);
            }
            Rl.DrawText(status, 24, WindowHeight - 40, FontSize, prepare is null ? Color.LIGHTGRAY : Color.YELLOW);
            Rl.EndDrawing();
        }

        Rl.CloseWindow();
    }

    private static void LaunchGameSession(string baseDir, string bootstrapPath)
    {
        Rl.CloseWindow();
        using var host = new RaylibGameHost(baseDir, bootstrapPath);
        host.Run();
        Process.Start(LauncherShellLifecycle.BuildRelayRestartStartInfo());
    }

    private static int ClampScroll(int selected, int scroll)
    {
        if (selected < scroll)
        {
            return selected;
        }
        if (selected >= scroll + VisibleRows)
        {
            return selected - VisibleRows + 1;
        }
        return scroll;
    }

    private static LauncherBuildMode ParseBuildMode(string value)
    {
        return Enum.TryParse<LauncherBuildMode>(value, ignoreCase: true, out var mode)
            ? mode
            : LauncherBuildMode.Auto;
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        return newline > 0 ? text[..newline] : text;
    }
}
