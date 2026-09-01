using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ludots.Content.EngineGallery;
using Ludots.Raylib.Render;
using Ludots.Raylib.SceneKit;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibPlayer
{
    /// <summary>
    /// 引擎播放器：打开任意引擎工程（--project &lt;目录&gt;）并运行其中的关卡。
    /// 播放器是二进制，工程是数据；两者互不包含，打包产物 = 播放器发布输出 + 工程目录副本。
    /// </summary>
    public static class Program
    {
        private const int WindowWidth = 1600;
        private const int WindowHeight = 900;

        public static int Main(string[] args)
        {
            string? projectPath = ParseOption(args, "--project");
            bool headlessArgs = ParseOption(args, "--scene") != null || ParseOption(args, "--screenshot") != null;
            if (projectPath == null)
            {
                // 双击运行的默认路径：自动发现身边工程——唯一工程直进，多工程弹拾取器；
                // 带自动化参数时必须确定性，多候选即报错点名 --project。
                List<(string Name, string Path)> found = DiscoverProjects();
                if (found.Count == 0)
                {
                    Console.Error.WriteLine(
                        "No engine project found near the executable. Pass --project <path> (a directory with project.json).");
                    return 2;
                }

                if (found.Count == 1)
                {
                    projectPath = found[0].Path;
                    Console.WriteLine($"Auto-selected engine project '{found[0].Name}' at {projectPath}");
                }
                else if (headlessArgs)
                {
                    Console.Error.WriteLine(
                        $"Multiple engine projects found; --project is required for unattended runs. Candidates: {string.Join(", ", found.Select(f => f.Path))}");
                    return 2;
                }
                else
                {
                    projectPath = PickProjectInteractive(found);
                    if (projectPath == null)
                    {
                        return 0;
                    }
                }
            }

            EngineProject project;
            try
            {
                project = EngineProject.Open(projectPath);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Failed to open engine project '{projectPath}': {exception.Message}");
                return 2;
            }

            string? sceneId = ParseOption(args, "--scene");
            string? screenshotPath = ParseOption(args, "--screenshot");
            string? jsonPath = ParseOption(args, "--json");
            int frames = ParseFrames(args);
            string? menuAuto = ParseOption(args, "--menu-auto");
            string? interactiveShot = ParseOption(args, "--interactive-shot");
            bool startInEditor = HasFlag(args, "--edit");

            if (sceneId == null && screenshotPath != null)
            {
                Console.Error.WriteLine("--screenshot requires --scene <id>.");
                return 2;
            }

            if (menuAuto != null && !project.TryCreate(menuAuto, out _))
            {
                Console.Error.WriteLine($"Unknown --menu-auto scene '{menuAuto}'.");
                return 2;
            }

            string? selfTest = ParseOption(args, "--edit-selftest");
            if (selfTest != null)
            {
                return RunEditSelfTest(project, selfTest);
            }

            if (sceneId != null && project.TryCreate(sceneId, out IEngineScene? scene))
            {
                if (startInEditor && screenshotPath == null)
                {
                    string editorScenePath = Path.Combine(project.Root, project.Descriptors
                        .First(d => string.Equals(d.Id, sceneId, StringComparison.OrdinalIgnoreCase))
                        .AssetPath.Replace('\\', '/'));
                    return RunSceneInteractive(project, scene!, null, editorScenePath, startInEditor: true);
                }

                return RunScene(project, scene!, screenshotPath, jsonPath, frames);
            }

            if (sceneId != null)
            {
                Console.Error.WriteLine($"Unknown scene '{sceneId}'. Available: {string.Join(", ", project.Ids)}");
                return 2;
            }

            return RunMenu(project, menuAuto, interactiveShot);
        }

        /// <summary>发现身边的工程：当前目录/输出目录自身与其下 projects/ 各层里的 project.json。</summary>
        private static List<(string Name, string Path)> DiscoverProjects()
        {
            var roots = new List<string> { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            string? parent = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
            if (parent != null)
            {
                roots.Add(parent);
            }

            var byPath = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                TryAddProject(byPath, root);
                string nested = Path.Combine(root, "projects");
                if (Directory.Exists(nested))
                {
                    foreach (string dir in Directory.GetDirectories(nested))
                    {
                        TryAddProject(byPath, dir);
                    }
                }
            }

            return byPath.Values.OrderBy(v => v.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void TryAddProject(Dictionary<string, (string, string)> byPath, string directory)
        {
            string marker = Path.Combine(directory, "project.json");
            if (!File.Exists(marker))
            {
                return;
            }

            string name = directory;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(marker));
                if (doc.RootElement.TryGetProperty("name", out JsonElement nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    name = nameElement.GetString()!;
                }
            }
            catch (JsonException)
            {
            }

            byPath[Path.GetFullPath(directory)] = (name, Path.GetFullPath(directory));
        }

        private static string? PickProjectInteractive(List<(string Name, string Path)> candidates)
        {
            GalleryFont.Reset();
            Rl.InitWindow(WindowWidth, WindowHeight, "Ludots Player — 选择工程");
            Rl.SetTargetFPS(60);
            int selected = 0;

            while (!Rl.WindowShouldClose())
            {
                for (int i = 0; i < Math.Min(candidates.Count, 36); i++)
                {
                    if (Rl.IsKeyPressed(HotkeyFor(i)))
                    {
                        selected = i;
                    }
                }
                if (Rl.IsKeyPressed(KeyboardKey.KEY_ENTER) || Rl.IsKeyPressed(KeyboardKey.KEY_SPACE))
                {
                    string chosen = candidates[selected].Path;
                    Rl.CloseWindow();
                    return chosen;
                }

                Rl.BeginDrawing();
                Rl.ClearBackground(new Color(18, 18, 24, 255));
                GalleryFont.Draw($"Ludots Player — 附近发现 {candidates.Count} 个工程", 24, 26, 26, GalleryColors.RayWhite);
                GalleryFont.Draw("数字/字母选择工程，Enter 打开，ESC 退出；也可用 --project <路径> 直达", 24, 60, 17, new Color(160, 160, 175, 255));
                int y = 100;
                for (int i = 0; i < candidates.Count; i++)
                {
                    bool isActive = i == selected;
                    Color color = isActive ? new Color(120, 220, 160, 255) : new Color(200, 200, 210, 255);
                    string prefix = i < 10 ? ((i + 1) % 10).ToString() : char.ToString((char)('A' + i - 10));
                    GalleryFont.Draw(isActive ? "> " : "  ", 24, y, 20, color);
                    GalleryFont.Draw($"[{prefix}] {candidates[i].Name}  —  {candidates[i].Path}", 52, y, 19, color);
                    y += 30;
                }
                GalleryFont.Flush();
                Rl.EndDrawing();
            }

            Rl.CloseWindow();
            return null;
        }

        private static int RunScene(EngineProject project, IEngineScene scene, string? screenshotPath, string? jsonPath, int frames)
        {
            // 多帧截屏与宿主 RaylibHostLoop 同一环境变量合同（录像脚本的取样通道）：
            // LUDOTS_TAKE_SCREENSHOT_PATH 基名 + LUDOTS_TAKE_SCREENSHOT_FRAMES 1 起帧号表，
            // 产物命名 <基名>_<序号:000>_f<帧:0000>.png。
            string? stillBasePath = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");
            int[] stillFrames = ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");
            bool stillSequence = !string.IsNullOrWhiteSpace(stillBasePath) && stillFrames.Length > 0;
            string? stillDirectory = stillSequence
                ? Path.GetDirectoryName(Path.GetFullPath(stillBasePath!))
                : null;
            var stillTargets = new List<string>();

            bool headless = screenshotPath != null || stillSequence;
            if (headless)
            {
                Rl.SetConfigFlags(GalleryWindowFlags.FlagWindowHidden);
            }

            GalleryFont.Reset();
            Rl.InitWindow(WindowWidth, WindowHeight, $"Ludots Player — {project.Name}/{scene.Id}");
            Rl.SetTargetFPS(60);

            var camera = CreateCamera(scene);
            scene.Load();

            Camera3D cam = camera.Camera;
            var frameMs = new List<double>(frames);
            var watch = Stopwatch.StartNew();
            double total = 0.0;
            int drawn = 0;
            int stillIndex = 0;

            while (drawn < frames && !Rl.WindowShouldClose())
            {
                float dt = Rl.GetFrameTime();
                total += dt;

                Rl.BeginDrawing();
                Rl.ClearBackground(GalleryColors.Black);

                long start = Stopwatch.GetTimestamp();
                scene.Draw(dt, total, ref cam);
                frameMs.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

                GalleryFont.Flush();
                if (screenshotPath != null && drawn == frames - 1)
                {
                    // 直读帧缓冲（DPI 无假黑边）前先冲刷 rl 渲染批次，Skia 文字已由 Flush 入批。
                    Rl.rlDrawRenderBatchActive();
                    RaylibFramebufferCapture.WriteFramebufferPng(screenshotPath);
                }

                if (stillSequence && stillIndex < stillFrames.Length && drawn == stillFrames[stillIndex] - 1)
                {
                    Rl.rlDrawRenderBatchActive();
                    string stillTarget = Path.Combine(
                        stillDirectory!, BuildStillFileName(stillBasePath!, stillIndex, stillFrames[stillIndex]));
                    RaylibFramebufferCapture.WriteFramebufferPng(stillTarget);
                    stillTargets.Add(stillTarget);
                    stillIndex++;
                }

                Rl.EndDrawing();
                drawn++;
            }

            int exitCode = 0;
            if (screenshotPath != null && !File.Exists(Path.GetFullPath(screenshotPath)))
            {
                Console.Error.WriteLine($"Failed to write screenshot '{screenshotPath}'.");
                exitCode = 3;
            }

            foreach (string target in stillTargets)
            {
                if (!File.Exists(target))
                {
                    Console.Error.WriteLine($"Failed to write still '{target}'.");
                    exitCode = exitCode == 0 ? 3 : exitCode;
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

        private static int RunMenu(EngineProject project, string? menuAuto, string? interactiveShot)
        {
            GalleryFont.Reset();
            Rl.InitWindow(WindowWidth, WindowHeight, $"Ludots Player — {project.Name}");
            Rl.SetTargetFPS(60);

            var scenes = project.Descriptors;
            int selected = 0;
            int hotkey = 0;
            int menuFrames = 0;

            while (!Rl.WindowShouldClose())
            {
                if (menuAuto != null && menuFrames++ >= 30)
                {
                    var autoScene = project.Create(menuAuto);
                    Rl.CloseWindow();
                    return RunSceneInteractive(project, autoScene, interactiveShot);
                }

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
                    var scene = project.Create(scenes[selected].Id);
                    Rl.CloseWindow();
                    return RunSceneInteractive(project, scene, null);
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

        private static int RunSceneInteractive(EngineProject project, IEngineScene scene, string? interactiveShot, string? editorScenePath = null, bool startInEditor = false)
        {
            GalleryFont.Reset();
            Rl.InitWindow(WindowWidth, WindowHeight, $"Ludots Player — {project.Name}/{scene.Title}");
            Rl.SetTargetFPS(60);
            bool editing = startInEditor && editorScenePath != null;
            EngineOrbitCamera camera = CreateCamera(scene);
            scene.Load();
            SceneEditorSession? editor = editing && editorScenePath != null
                ? new SceneEditorSession(new EngineSceneEditorModel(editorScenePath))
                : null;

            Camera3D cam = camera.Camera;
            double total = 0.0;
            int frameIndex = 0;
            while (!Rl.WindowShouldClose())
            {
                float dt = Rl.GetFrameTime();
                total += dt;

                if (editor != null && Rl.IsKeyPressed(KeyboardKey.KEY_E))
                {
                    editing = !editing;
                    if (!editing)
                    {
                        editor.Dispose();
                        editor = null;
                    }
                }
                else if (editor == null && editorScenePath != null && Rl.IsKeyPressed(KeyboardKey.KEY_E) && File.Exists(editorScenePath))
                {
                    editor = new SceneEditorSession(new EngineSceneEditorModel(editorScenePath));
                    editing = true;
                }

                if (editing && editor != null)
                {
                    editor.HandleInput(camera.Camera, WindowWidth, WindowHeight);
                    camera.UseRotateButton(MouseButton.MOUSE_RIGHT_BUTTON);
                    if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) && Rl.IsKeyPressed(KeyboardKey.KEY_S))
                    {
                        editor.TrySave();
                    }
                }
                else
                {
                    camera.UseRotateButton(MouseButton.MOUSE_LEFT_BUTTON);
                    camera.Update(dt);
                }

                cam = editing ? camera.Camera : camera.Camera;

                Rl.BeginDrawing();
                Rl.ClearBackground(GalleryColors.Black);
                scene.Draw(dt, total, ref cam);

                if (editing && editor != null)
                {
                    Rl.BeginMode3D(cam);
                    editor.DrawWorldOverlay(cam);
                    Rl.EndMode3D();
                }

                GalleryFont.Draw(editing
                    ? "[E] 退出编辑   [Ctrl+S] 保存   左键拾取/拖轴   右键旋转（检视视图 · 游戏 3C 由 Ludots 接管）"
                    : "[ESC] 返回菜单   [R] 复位检视视角   [E] 编辑场景（检视视图 · 游戏 3C 由 Ludots 接管）", 8, WindowHeight - 26, 15, GalleryColors.RayWhite);
                GalleryFont.Draw($"{scene.Title} — {scene.Summary}", 8, WindowHeight - 48, 18, new Color(220, 220, 230, 255));
                GalleryFont.Flush();

                if (editing && editor != null)
                {
                    editor.DrawHud(WindowWidth, WindowHeight);
                }

                if (interactiveShot != null && frameIndex == 120)
                {
                    Rl.rlDrawRenderBatchActive();
                    RaylibFramebufferCapture.WriteFramebufferPng(interactiveShot);

                    Rl.EndDrawing();
                    break;
                }

                Rl.EndDrawing();
                frameIndex++;
            }

            editor?.Dispose();
            scene.Dispose();
            Rl.CloseWindow();

            if (interactiveShot == null)
            {
                GalleryFont.Reset();
                return RunMenu(project, null, null);
            }

            return 0;
        }

        private static void DrawMenu(IReadOnlyList<SceneDescriptor> scenes, int selected)
        {
            GalleryFont.Draw($"Ludots Player — 引擎渲染能力 {scenes.Count} 项", 24, 20, 28, GalleryColors.RayWhite);
            GalleryFont.Draw("数字/字母选择场景，Enter 启动，ESC 退出；场景内 ESC 返回菜单，R 复位检视视角", 24, 56, 18, new Color(160, 160, 175, 255));

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

        private static EngineOrbitCamera CreateCamera(IEngineScene scene)
        {
            EngineSceneCameraDefaults defaults = scene.CameraDefaults;
            return new EngineOrbitCamera(
                defaults.Distance,
                defaults.PitchDegrees,
                defaults.YawDegrees,
                defaults.Target,
                defaults.FovyDegrees);
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

        private static int[] ReadEnvFrameList(string key)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<int>();
            }

            var parsed = new List<int>();
            foreach (string part in raw.Split(','))
            {
                if (int.TryParse(part.Trim(), out int frame) && frame >= 1)
                {
                    parsed.Add(frame);
                }
            }

            return parsed.ToArray();
        }

        private static string BuildStillFileName(string basePath, int sequenceIndex, int frame)
        {
            string fileName = Path.GetFileNameWithoutExtension(basePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "screenshot";
            }

            return $"{fileName}_{sequenceIndex + 1:000}_f{frame:0000}.png";
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

        /// <summary>
        /// 无头编辑自证：打开工程某场景的编辑模型，模拟拾取首个实例、沿 +X 移动 5m、
        /// 保存到临时副本，断言 diff 只含 position 数字行且重装载语义成立。CI 可跑的证据链。
        /// </summary>
        private static int RunEditSelfTest(EngineProject project, string sceneId)
        {
            SceneDescriptor descriptor = project.Descriptors.FirstOrDefault(d => string.Equals(d.Id, sceneId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Unknown scene '{sceneId}' for edit self-test.");
            string sourcePath = Path.Combine(project.Root, descriptor.AssetPath.Replace('\\', '/'));
            string tempPath = Path.Combine(Path.GetTempPath(), "ludots-edit-selftest-" + Guid.NewGuid().ToString("N") + ".scene.json");
            File.Copy(sourcePath, tempPath);

            try
            {
                var editor = new EngineSceneEditorModel(tempPath);
                List<EngineEditableInstance> instances = editor.EnumerateStaticMeshInstances();
                if (instances.Count == 0)
                {
                    Console.Error.WriteLine($"edit-selftest: scene '{sceneId}' has no static_mesh instances; nothing to pick.");
                    return 3;
                }

                EngineEditableInstance target = instances[0];
                float originalX = target.Position.X;
                target.PositionArray[0] = System.Text.Json.Nodes.JsonValue.Create(Math.Round(originalX + 5f, 2));
                editor.Save();

                string before = File.ReadAllText(sourcePath);
                string after = File.ReadAllText(tempPath);
                string[] beforeLines = before.Split('\n');
                string[] afterLines = after.Split('\n');
                if (beforeLines.Length != afterLines.Length)
                {
                    Console.Error.WriteLine("edit-selftest FAIL: line count changed.");
                    return 4;
                }

                var changed = new List<int>();
                for (int i = 0; i < beforeLines.Length; i++)
                {
                    if (beforeLines[i] != afterLines[i])
                    {
                        changed.Add(i);
                    }
                }

                if (changed.Count == 0 || changed.Count > 3)
                {
                    Console.Error.WriteLine($"edit-selftest FAIL: expected 1-3 changed lines, got {changed.Count}.");
                    return 5;
                }

                // 语义校验由重装载完成（ParseSceneDocument 是 internal，走编辑模型装载链）。
                var reloaded = new EngineSceneEditorModel(tempPath);
                float movedX = reloaded.EnumerateStaticMeshInstances()[0].Position.X;
                if (MathF.Abs(movedX - (originalX + 5f)) > 0.01f)
                {
                    Console.Error.WriteLine($"edit-selftest FAIL: reloaded X {movedX} != {originalX + 5f}.");
                    return 6;
                }

                Console.WriteLine($"edit-selftest PASS: scene={sceneId} instances={instances.Count} picked={target.NodeId}/comp{target.ComponentIndex}/inst{target.InstanceIndex} movedX {originalX:0.00}->{movedX:0.00} changedLines={changed.Count}");
                return 0;
            }
            finally
            {
                File.Delete(tempPath);
                File.Delete(tempPath + ".bak");
            }
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int ParseFrames(string[] args)
        {
            string? value = ParseOption(args, "--frames");
            return int.TryParse(value, out int frames) && frames > 0 ? frames : 300;
        }
    }
}
