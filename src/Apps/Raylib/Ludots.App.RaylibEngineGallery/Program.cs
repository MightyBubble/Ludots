using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery
{
    public static class Program
    {
        private const int WindowWidth = 1600;
        private const int WindowHeight = 900;

        public static int Main(string[] args)
        {
            string? sceneId = ParseOption(args, "--scene");
            string? screenshotPath = ParseOption(args, "--screenshot");
            string? jsonPath = ParseOption(args, "--json");
            int frames = ParseFrames(args);

            if (sceneId == null && screenshotPath != null)
            {
                Console.Error.WriteLine("--screenshot requires --scene <id>.");
                return 2;
            }

            if (sceneId != null && SceneCatalog.TryCreate(sceneId, out IEngineScene? scene))
            {
                return RunScene(scene!, screenshotPath, jsonPath, frames);
            }

            if (sceneId != null)
            {
                Console.Error.WriteLine($"Unknown scene '{sceneId}'. Available: {string.Join(", ", SceneCatalog.Ids)}");
                return 2;
            }

            return RunMenu();
        }

        private static int RunScene(IEngineScene scene, string? screenshotPath, string? jsonPath, int frames)
        {
            bool headless = screenshotPath != null;
            if (headless)
            {
                Rl.SetConfigFlags(GalleryWindowFlags.FlagWindowHidden);
            }

            if (headless)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath!))!);
            }

            Rl.InitWindow(WindowWidth, WindowHeight, $"Ludots Engine Gallery — {scene.Id}");
            Rl.SetTargetFPS(60);

            var camera = new EngineOrbitCamera();
            scene.Load();

            Camera3D cam = camera.Camera;
            var frameMs = new List<double>(frames);
            var watch = Stopwatch.StartNew();
            double total = 0.0;
            int drawn = 0;

            while (drawn < frames && !Rl.WindowShouldClose())
            {
                float dt = Rl.GetFrameTime();
                total += dt;

                Rl.BeginDrawing();
                Rl.ClearBackground(GalleryColors.Black);

                long start = Stopwatch.GetTimestamp();
                scene.Draw(dt, total, ref cam);
                frameMs.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

                if (screenshotPath != null && drawn == frames - 1)
                {
                    // raylib 5.5 TakeScreenshot 读取的是当前帧缓冲，2D 文字尚在延迟批处理里，必须先冲刷。
                    Rl.rlDrawRenderBatchActive();
                    Rl.TakeScreenshot(Path.GetFileName(screenshotPath));
                }

                GalleryFont.Flush();
                Rl.EndDrawing();
                drawn++;
            }

            int exitCode = 0;
            if (screenshotPath != null)
            {
                string fullScreenshotPath = Path.GetFullPath(screenshotPath);
                string workingPath = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(screenshotPath));
                if (!string.Equals(workingPath, fullScreenshotPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(workingPath))
                {
                    File.Copy(workingPath, fullScreenshotPath, overwrite: true);
                    File.Delete(workingPath);
                }

                if (!File.Exists(fullScreenshotPath))
                {
                    Console.Error.WriteLine($"Failed to write screenshot '{screenshotPath}'.");
                    exitCode = 3;
                }
            }

            if (jsonPath != null)
            {
                WriteStats(jsonPath, scene.Id, drawn, frameMs, watch.Elapsed.TotalMilliseconds);
            }

            scene.Dispose();
            Rl.CloseWindow();
            return exitCode;
        }

        private static int RunMenu()
        {
            Rl.InitWindow(WindowWidth, WindowHeight, "Ludots Engine Gallery");
            Rl.SetTargetFPS(60);

            var scenes = SceneCatalog.Descriptors;
            int selected = 0;
            int hotkey = 0;

            while (!Rl.WindowShouldClose())
            {
                for (int i = 0; i < Math.Min(scenes.Count, 36); i++)
                {
                    KeyboardKey key = HotkeyFor(i);
                    if (Rl.IsKeyPressed(key))
                    {
                        selected = i;
                        hotkey = i;
                    }
                }
                if (Rl.IsKeyPressed(KeyboardKey.KEY_ENTER) || Rl.IsKeyPressed(KeyboardKey.KEY_SPACE))
                {
                    var scene = SceneCatalog.Create(scenes[selected].Id);
                    Rl.CloseWindow();
                    return RunSceneInteractive(scene);
                }

                Rl.BeginDrawing();
                Rl.ClearBackground(new Color(18, 18, 24, 255));
                DrawMenu(scenes, selected);
                GalleryFont.Flush();
                Rl.EndDrawing();
            }

            Rl.CloseWindow();
            return 0;
        }

        private static int RunSceneInteractive(IEngineScene scene)
        {
            Rl.InitWindow(WindowWidth, WindowHeight, $"Ludots Engine Gallery — {scene.Title}");
            Rl.SetTargetFPS(60);
            var camera = new EngineOrbitCamera();
            scene.Load();

            Camera3D cam = camera.Camera;
            double total = 0.0;
            while (!Rl.WindowShouldClose())
            {
                float dt = Rl.GetFrameTime();
                total += dt;
                camera.Update(dt);
                cam = camera.Camera;

                Rl.BeginDrawing();
                Rl.ClearBackground(GalleryColors.Black);
                scene.Draw(dt, total, ref cam);

                int y = 8;
                GalleryFont.Draw($"[ESC] menu   [R] reset camera", 8, WindowHeight - 26, 18, GalleryColors.RayWhite);
                GalleryFont.Draw($"{scene.Title} — {scene.Summary}", 8, WindowHeight - 48, 18, new Color(220, 220, 230, 255));
                GalleryFont.Flush();
                Rl.EndDrawing();
            }

            scene.Dispose();
            Rl.CloseWindow();

            Rl.InitWindow(WindowWidth, WindowHeight, "Ludots Engine Gallery");
            Rl.SetTargetFPS(60);
            int back = RunMenu();
            return back;
        }

        private static void DrawMenu(List<SceneDescriptor> scenes, int selected)
        {
            GalleryFont.Draw("Ludots Engine Gallery — raylib 引擎渲染能力 18 项", 24, 20, 28, GalleryColors.RayWhite);
            GalleryFont.Draw("数字/字母选择场景，Enter 启动，ESC 退出；场景内 ESC 返回菜单，R 复位相机", 24, 56, 18, new Color(160, 160, 175, 255));

            int y = 96;
            for (int i = 0; i < scenes.Count; i++)
            {
                bool isActive = i == selected;
                Color color = isActive ? new Color(120, 220, 160, 255) : new Color(200, 200, 210, 255);
                string prefix = i < 10 ? ((i + 1) % 10).ToString() : char.ToString((char)('A' + i - 10));
                GalleryFont.Draw(isActive ? "> " : "  ", 24, y, 20, color);
                GalleryFont.Draw($"[{prefix}] {scenes[i].Title} — {scenes[i].Summary}", 52, y, 20, color);
                y += 30;
            }
        }

        private static KeyboardKey HotkeyFor(int index)
        {
            if (index < 9) return (KeyboardKey)(KeyboardKey.KEY_ONE + index);
            if (index == 9) return KeyboardKey.KEY_ZERO;
            return (KeyboardKey)(KeyboardKey.KEY_A + index - 10);
        }

        private static void WriteStats(string path, string sceneId, int frames, List<double> frameMs, double wallMs)
        {
            var ordered = frameMs.OrderBy(v => v).ToList();
            double avg = frameMs.Count > 0 ? frameMs.Average() : 0.0;
            double p95 = ordered.Count > 0 ? ordered[(int)Math.Min(ordered.Count - 1, ordered.Count * 95 / 100)] : 0.0;
            var payload = new
            {
                scene = sceneId,
                frames,
                avgFrameMs = Math.Round(avg, 3),
                p95FrameMs = Math.Round(p95, 3),
                maxFrameMs = Math.Round(frameMs.Count > 0 ? frameMs.Max() : 0.0, 3),
                wallMs = Math.Round(wallMs, 1),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string? ParseOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static int ParseFrames(string[] args)
        {
            string? value = ParseOption(args, "--frames");
            return int.TryParse(value, out int frames) && frames > 0 ? frames : 300;
        }
    }
}
