using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibAssetAcceptance
{
    /// <summary>
    /// raylib 引擎资产验收台：把外部模型（Sketchfab/Fab/Unity 资产商店导出）拖进窗口，
    /// 在 GGX + split-sum IBL + 深度阴影的引擎光照栈下验收 PBR 材质与动画。
    /// 拖放与 --model 共用同一装载管线；加载失败在画面上给出可读错误，不静默降级。
    /// 输入合同：.glb/.gltf 走 native；.obj/.fbx/.dae 经引擎 Assimp 转换器转 GLB 装载
    /// （native OBJ 分支对无 texcoord/normal 索引面片会 AccessViolation，issue #1050）；
    /// zip/USD/blend 拒绝并给出可读理由。
    /// </summary>
    public static unsafe class Program
    {
        private const int WindowWidth = 1600;
        private const int WindowHeight = 900;
        private const uint FlagWindowHidden = 0x80;
        private const float TargetModelHeight = 3.0f;
        private static readonly Color PanelRed = new(220, 70, 70, 255);
        private static readonly Color PanelDim = new(150, 155, 170, 255);
        private static readonly Color PanelAccent = new(120, 220, 160, 255);

        private sealed class LoadedAsset
        {
            public Model Model;
            public ModelAnimation* Animations;
            public int AnimCount;
            public RaylibFileModelLit.MaterialInspection[] Inspections = Array.Empty<RaylibFileModelLit.MaterialInspection>();
            public string DisplayName = "";
            public string SourcePath = "";
            public string FormatNote = "";
            public float NormalizeScale = 1f;
            public Vector3 Pivot = Vector3.Zero;
            public float NormalizedHeight = TargetModelHeight;

            public static string ClipName(ModelAnimation* animation)
            {
                byte* name = animation->name;
                int len = 0;
                while (len < 32 && name[len] != 0)
                {
                    len++;
                }

                return len == 0 ? "(未命名)" : Encoding.UTF8.GetString(name, len);
            }
        }

        private static RaylibSkyboxRenderer _skybox = null!;
        private static RaylibFrameLighting _lighting = null!;
        private static RaylibDirectionalShadowMap _shadowMap = null!;
        private static RaylibLitModel _propsLit = null!;
        private static RaylibFileModelLit _modelLit = null!;
        private static Mesh _refSphere;
        private static LoadedAsset? _asset;
        private static string _error = "";
        private static OrbitCamera _camera = null!;
        private static bool _quitRequested;
        private static bool _demo;
        private static double _autoSunSeconds;

        // 运行时旋钮
        private static bool _sunAuto = true;
        private static float _sunPhase = 0.58f;
        private static float _turntable = 1f;
        private static float _turntableAngle;
        private static int _envStep = 2;
        private static readonly float[] EnvSteps = { 0f, 0.5f, 1f, 2f };
        private static int _alphaStep;
        private static readonly float[] AlphaSteps = { 0.1f, 0.5f, 0f };
        private static int _clipIndex;
        private static float _clipFrameF;
        private static float _animSpeed = 1f;
        private static bool _animPlaying = true;

        public static int Main(string[] args)
        {
            string? modelPath = ParseOption(args, "--model");
            _demo = HasOption(args, "--demo");
            string? screenshotPath = ParseOption(args, "--screenshot");
            int frames = ParseFrames(args);

            // 静帧序列（录像取样）同样走隐藏窗口；SetConfigFlags 必须先于 InitWindow。
            bool stillSequenceRequested =
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH"))
                && ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES").Length > 0;
            if (screenshotPath != null || stillSequenceRequested)
            {
                Rl.SetConfigFlags(FlagWindowHidden);
            }

            AcceptanceFont.Reset();
            Rl.InitWindow(WindowWidth, WindowHeight, "Raylib 资产验收台");
            Rl.SetTargetFPS(60);

            LoadEnvironment();

            _camera = new OrbitCamera();
            if (modelPath != null)
            {
                TryLoadModel(modelPath);
            }
            else if (screenshotPath != null || stillSequenceRequested)
            {
                _error = "未提供 --model，只有拖放交互可以装载资产；本次截图为空台。";
            }

            int exitCode = RunLoop(frames, screenshotPath);

            UnloadAsset();
            RaylibNativeResources.UnloadMesh(_refSphere);
            _modelLit.Dispose();
            _propsLit.Dispose();
            _shadowMap.Dispose();
            _skybox.Dispose();
            Rl.CloseWindow();
            return exitCode;
        }

        private static void UnloadAsset()
        {
            if (_asset == null)
            {
                return;
            }

            // 先摘掉注入槽（IBL/阴影纹理归渲染器所有），再交还 raylib 释放模型。
            _modelLit.DetachInjectedTextures(_asset.Model);
            if (_asset.Animations != null && _asset.AnimCount > 0)
            {
                Rl.UnloadModelAnimations(_asset.Animations, _asset.AnimCount);
            }

            RaylibNativeResources.UnloadModel(_asset.Model);
            _asset = null;
        }

        private static void LoadEnvironment()
        {
            _skybox = new RaylibSkyboxRenderer();
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: _sunPhase);
            _shadowMap = new RaylibDirectionalShadowMap();
            _propsLit = new RaylibLitModel();
            _modelLit = new RaylibFileModelLit();
            _refSphere = RaylibNativeResources.GenMeshSphere(0.5f, 24, 16);
        }

        private static int RunLoop(int frames, string? screenshotPath)
        {
            // 多帧静屏与宿主 RaylibHostLoop 同一环境变量合同（录像脚本的取样通道）：
            // LUDOTS_TAKE_SCREENSHOT_PATH 基名 + LUDOTS_TAKE_SCREENSHOT_FRAMES 1 起帧号表，
            // 产物命名 <基名>_<序号:000>_f<帧:0000>.png。
            string? stillBasePath = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");
            int[] stillFrames = ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");
            bool stillSequence = !string.IsNullOrWhiteSpace(stillBasePath) && stillFrames.Length > 0;
            string? stillDirectory = stillSequence
                ? Path.GetDirectoryName(Path.GetFullPath(stillBasePath!))
                : null;
            var stillMoves = new List<(string Source, string Target)>();

            bool headless = screenshotPath != null || stillSequence;

            if (screenshotPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
            }

            if (stillDirectory != null)
            {
                Directory.CreateDirectory(stillDirectory);
            }

            int drawn = 0;
            double total = 0.0;
            int stillIndex = 0;

            while (!Rl.WindowShouldClose() && !_quitRequested)
            {
                float dt = Rl.GetFrameTime();
                total += dt;

                if (!headless)
                {
                    PollDroppedFiles();
                    PollKeys();
                    _camera.Update(dt);
                }

                if (_demo)
                {
                    ApplyDemoTimeline(drawn);
                }

                UpdateKnobs(dt);

                Rl.BeginDrawing();
                Rl.ClearBackground(new Color(12, 14, 20, 255));

                DrawScene(dt, total);

                DrawHud();
                AcceptanceFont.Flush();

                if (screenshotPath != null && drawn == frames - 1)
                {
                    Rl.rlDrawRenderBatchActive();
                    Rl.TakeScreenshot(Path.GetFileName(screenshotPath));
                }

                if (stillSequence && stillIndex < stillFrames.Length && drawn == stillFrames[stillIndex] - 1)
                {
                    Rl.rlDrawRenderBatchActive();
                    string stillName = BuildStillFileName(stillBasePath!, stillIndex, stillFrames[stillIndex]);
                    Rl.TakeScreenshot(stillName);
                    stillMoves.Add((stillName, Path.Combine(stillDirectory!, stillName)));
                    stillIndex++;
                }

                drawn++;
                if ((screenshotPath != null || stillSequence) && drawn >= frames)
                {
                    Rl.EndDrawing();
                    break;
                }

                Rl.EndDrawing();
            }

            foreach ((string source, string target) in stillMoves)
            {
                string workingPath = Path.Combine(Environment.CurrentDirectory, source);
                if (!File.Exists(workingPath))
                {
                    Console.Error.WriteLine($"Failed to write still '{source}'.");
                    continue;
                }

                if (!string.Equals(workingPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(workingPath, target, overwrite: true);
                    File.Delete(workingPath);
                }
            }

            if (screenshotPath == null)
            {
                return 0;
            }

            string fullScreenshotPath = Path.GetFullPath(screenshotPath);
            string workingShot = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(screenshotPath));
            if (File.Exists(workingShot))
            {
                if (!string.Equals(workingShot, fullScreenshotPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(workingShot, fullScreenshotPath, overwrite: true);
                    File.Delete(workingShot);
                }
            }
            else
            {
                Console.Error.WriteLine($"Failed to write screenshot '{screenshotPath}'.");
                return 3;
            }

            return 0;
        }

        /// <summary>--demo 的旋钮时间线：60fps 帧号驱动，把验收台的核心可读性（视图拆通道、
        /// 贴图 vs 缺省标量消融）在没有键盘输入的录像里自动演示一遍。</summary>
        private static void ApplyDemoTimeline(int frame)
        {
            const int segment = 120;
            int phase = frame / segment;
            _modelLit.ScalarOverride = phase == 4;
            _modelLit.Mode = phase switch
            {
                1 => RaylibFileModelLit.ViewMode.Albedo,
                2 => RaylibFileModelLit.ViewMode.Normals,
                3 => RaylibFileModelLit.ViewMode.Roughness,
                _ => RaylibFileModelLit.ViewMode.Final,
            };
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

        private static void UpdateKnobs(float dt)
        {
            if (_sunAuto)
            {
                // 太阳自动相位：40 秒扫过 0.30–0.75（清晨→傍晚），循环。
                _autoSunSeconds += dt;
                _sunPhase = 0.30f + (float)(_autoSunSeconds % 40.0 / 40.0) * 0.45f;
            }

            if (_turntable > 0f && _asset != null)
            {
                _turntableAngle += 0.35f * dt;
            }

            if (_asset is { AnimCount: > 0 } asset && _animPlaying)
            {
                int frameCount = asset.Animations[_clipIndex].frameCount;
                if (frameCount > 0)
                {
                    _clipFrameF = (_clipFrameF + 60f * dt * _animSpeed) % frameCount;
                    Rl.UpdateModelAnimation(asset.Model, asset.Animations[_clipIndex], (int)_clipFrameF);
                }
            }
        }

        private static void PollKeys()
        {
            if (Rl.IsKeyPressed(KeyboardKey.KEY_ONE)) _modelLit.Mode = RaylibFileModelLit.ViewMode.Final;
            if (Rl.IsKeyPressed(KeyboardKey.KEY_TWO)) _modelLit.Mode = RaylibFileModelLit.ViewMode.Albedo;
            if (Rl.IsKeyPressed(KeyboardKey.KEY_THREE)) _modelLit.Mode = RaylibFileModelLit.ViewMode.Normals;
            if (Rl.IsKeyPressed(KeyboardKey.KEY_FOUR)) _modelLit.Mode = RaylibFileModelLit.ViewMode.Metallic;
            if (Rl.IsKeyPressed(KeyboardKey.KEY_FIVE)) _modelLit.Mode = RaylibFileModelLit.ViewMode.Roughness;
            if (Rl.IsKeyPressed(KeyboardKey.KEY_O))
            {
                _modelLit.ScalarOverride = !_modelLit.ScalarOverride;
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_K))
            {
                _alphaStep = (_alphaStep + 1) % AlphaSteps.Length;
                _modelLit.AlphaCutoff = AlphaSteps[_alphaStep];
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_T))
            {
                _turntable = _turntable > 0f ? 0f : 1f;
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_E))
            {
                _envStep = (_envStep + 1) % EnvSteps.Length;
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_COMMA))
            {
                _sunAuto = false;
                _sunPhase = Math.Clamp(_sunPhase - 0.02f, 0.05f, 0.95f);
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_PERIOD))
            {
                _sunAuto = false;
                _sunPhase = Math.Clamp(_sunPhase + 0.02f, 0.05f, 0.95f);
            }

            if (_asset is { AnimCount: > 1 } multiClip)
            {
                if (Rl.IsKeyPressed(KeyboardKey.KEY_N))
                {
                    _clipIndex = (_clipIndex + 1) % multiClip.AnimCount;
                    _clipFrameF = 0f;
                }

                if (Rl.IsKeyPressed(KeyboardKey.KEY_P))
                {
                    _clipIndex = (_clipIndex - 1 + multiClip.AnimCount) % multiClip.AnimCount;
                    _clipFrameF = 0f;
                }
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_SPACE) && _asset is { AnimCount: > 0 })
            {
                _animPlaying = !_animPlaying;
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_MINUS))
            {
                _animSpeed = MathF.Max(0.25f, _animSpeed - 0.25f);
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_EQUAL))
            {
                _animSpeed = MathF.Min(4f, _animSpeed + 0.25f);
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_R))
            {
                _camera.ResetToFit(_asset?.NormalizedHeight ?? TargetModelHeight);
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
            {
                _quitRequested = true;
            }
        }

        private static void PollDroppedFiles()
        {
            if (!Rl.IsFileDropped())
            {
                return;
            }

            FilePathList dropped = Rl.LoadDroppedFiles();
            var paths = new List<string>((int)dropped.count);
            for (uint i = 0; i < dropped.count; i++)
            {
                IntPtr raw = Marshal.ReadIntPtr(dropped.paths, (int)(i * IntPtr.Size));
                string? path = Marshal.PtrToStringUTF8(raw);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }

            Rl.UnloadDroppedFiles(dropped);

            string? candidate = ResolveModelCandidate(paths, out string rejectReason);
            if (candidate == null)
            {
                _error = rejectReason;
                return;
            }

            TryLoadModel(candidate);
        }

        /// <summary>从拖入路径集合挑选模型文件：直接文件优先，其次文件夹内递归找可装载格式；不支持的扩展名给出可读拒绝理由。</summary>
        private static string? ResolveModelCandidate(List<string> paths, out string rejectReason)
        {
            // 与引擎 RaylibModelFileLoader 的声明集一致：glTF 原生，OBJ/FBX/COLLADA 经 Assimp 转 GLB。
            string[] supported = { ".glb", ".gltf", ".obj", ".fbx", ".dae" };
            foreach (string path in paths)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (supported.Contains(ext))
                {
                    rejectReason = "";
                    return path;
                }
            }

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (string pattern in new[] { "*.glb", "*.gltf", "*.fbx", "*.obj", "*.dae" })
                    {
                        string? found = Directory
                            .EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                            .OrderBy(p => p.Length)
                            .FirstOrDefault();
                        if (found != null)
                        {
                            rejectReason = "";
                            return found;
                        }
                    }

                    rejectReason = $"文件夹里没有可装载模型（.glb/.gltf/.obj/.fbx/.dae）：{Path.GetFileName(path)}（Sketchfab/Fab 下载请先解压 zip）";
                    return null;
                }
            }

            string first = paths.Count > 0 ? paths[0] : "";
            string firstExt = Path.GetExtension(first).ToLowerInvariant();
            rejectReason = firstExt switch
            {
                ".zip" or ".unitypackage" => "不支持压缩包：请先解压，再拖里面的模型文件或整个文件夹",
                ".usd" or ".usdz" or ".abc" or ".blend" => $"不支持 {firstExt}：请导出 glTF 2.0 (.glb) / FBX 再拖入",
                _ => $"不认识的文件类型 '{Path.GetFileName(first)}'：接受 .glb / .gltf / .obj / .fbx / .dae（或含它们的文件夹）",
            };

            return null;
        }

        private static void TryLoadModel(string path)
        {
            LoadedAsset? attempt = null;
            try
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("文件不存在", path);
                }

                UnloadAsset();
                _error = "";

                // 统一经引擎装载入口：glTF 原生，OBJ/FBX/DAE 先转 GLB（#1050：OBJ 直走
                // native LoadModel 会 AccessViolation，转换失败在此 fail-loud 成错误面板）。
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(path);

                Model model = RaylibNativeResources.LoadModel(loadablePath);
                attempt = new LoadedAsset { Model = model };
                if (model.meshCount <= 0)
                {
                    throw new InvalidOperationException("LoadModel 返回 0 网格——文件损坏或不是可解析的模型");
                }

                int animCount;
                attempt.Animations = Rl.LoadModelAnimations(loadablePath, out animCount);
                attempt.AnimCount = animCount;
                for (int i = 0; i < animCount; i++)
                {
                    if (!Rl.IsModelAnimationValid(model, attempt.Animations[i]))
                    {
                        throw new InvalidOperationException(
                            $"动画[{i}] 骨骼与模型不一致（modelBones={model.boneCount}, animBones={attempt.Animations[i].boneCount}）——拒绝装载");
                    }
                }

                BoundingBox bounds = Rl.GetModelBoundingBox(model);
                Vector3 size = bounds.max - bounds.min;
                float maxDim = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
                if (maxDim <= 0f || float.IsNaN(maxDim) || float.IsInfinity(maxDim))
                {
                    throw new InvalidOperationException($"包围盒无效（size={size}）——网格顶点数据异常");
                }

                attempt.NormalizeScale = TargetModelHeight / maxDim;
                attempt.Pivot = new Vector3(
                    (bounds.min.X + bounds.max.X) * 0.5f,
                    bounds.min.Y,
                    (bounds.min.Z + bounds.max.Z) * 0.5f);
                attempt.NormalizedHeight = size.Y * attempt.NormalizeScale;
                attempt.Inspections = RaylibFileModelLit.Inspect(model);

                string ext = Path.GetExtension(path).ToLowerInvariant();
                attempt.FormatNote = ext switch
                {
                    ".obj" => $"OBJ → GLB 经 Assimp 转换（缓存 {Path.GetDirectoryName(loadablePath)}）；OBJ 无 PBR 贴图/动画",
                    ".fbx" => $"FBX → GLB 经 Assimp 转换（缓存 {Path.GetDirectoryName(loadablePath)}）",
                    ".dae" => $"COLLADA → GLB 经 Assimp 转换（缓存 {Path.GetDirectoryName(loadablePath)}）",
                    _ => animCount > 0 ? "" : "静态模型（无动画轨道）",
                };
                attempt.DisplayName = Path.GetFileName(path);
                attempt.SourcePath = Path.GetFullPath(path);

                _clipIndex = 0;
                _clipFrameF = 0f;
                _animPlaying = true;
                _modelLit.AttachToModel(model);
                _modelLit.Mode = RaylibFileModelLit.ViewMode.Final;
                _modelLit.ScalarOverride = false;
                _camera.ResetToFit(attempt.NormalizedHeight);
                _asset = attempt;
                attempt = null;
            }
            catch (Exception ex)
            {
                if (attempt != null)
                {
                    if (attempt.Animations != null && attempt.AnimCount > 0)
                    {
                        Rl.UnloadModelAnimations(attempt.Animations, attempt.AnimCount);
                    }

                    RaylibNativeResources.UnloadModel(attempt.Model);
                }

                _error = $"装载失败：{ex.Message}";
                _asset = null;
            }
        }

        private static void DrawScene(float dt, double total)
        {
            _lighting.SetDayPhase(_sunPhase);
            Vector3 sun = _lighting.SunDirectionToward;

            Camera3D cam = _camera.Camera;
            Vector3 modelCenter = new(0f, (_asset?.NormalizedHeight ?? TargetModelHeight) * 0.5f, 0f);
            float sceneRadius = MathF.Max(5f, (_asset?.NormalizedHeight ?? TargetModelHeight) * 1.4f);

            // 阴影 pass：模型逐网格（保持与 lit 变换一致）+ 参考球。
            _shadowMap.BeginFrame(sun, modelCenter, sceneRadius);
            if (_asset != null)
            {
                RaylibMatrix modelTransform = ModelTransform();
                for (int i = 0; i < _asset.Model.meshCount; i++)
                {
                    _shadowMap.DrawMeshShadow(_asset.Model.meshes[i], modelTransform);
                }
            }

            foreach ((Vector3 center, float radius) in ReferenceSpheres)
            {
                Matrix4x4 rowMajor = Matrix4x4.CreateScale(radius * 2f) * Matrix4x4.CreateTranslation(center);
                _shadowMap.DrawMeshShadow(_refSphere, RaylibMatrix.FromSystemNumerics(rowMajor));
            }

            _shadowMap.EndFrame();

            var skyConfig = RaylibRenderEnvironmentConfig.CreateDefault() with
            {
                Skybox = RaylibSkyboxConfig.CreateDefault() with
                {
                    SizeMeters = 1200f,
                    ZenithColor = new Vector3(0.10f, 0.30f, 0.62f),
                    HorizonColor = new Vector3(0.84f, 0.72f, 0.58f),
                    GroundHazeColor = new Vector3(0.46f, 0.42f, 0.38f),
                    ClearColor = new Color(120, 150, 180, 255),
                },
                Lighting = RaylibLightingConfig.CreateDefault() with
                {
                    SunDirection = sun,
                    SunColor = new Vector3(1f, 0.93f, 0.78f),
                },
            };

            Rl.BeginMode3D(cam);
            _skybox.Draw(cam, (float)total, skyConfig);
            Rl.DrawGrid(40, 1.5f);

            // 参考球带（PBR 地面真值）：非金属/金属两列 × 粗糙度梯度，与被验资产同光同影。
            _propsLit.BeginFrame(_lighting, cam.position, _shadowMap, shadowTexelWorld: 0.03f);
            for (int i = 0; i < ReferenceSpheres.Length; i++)
            {
                float rough = 0.15f + (i % 3) * 0.35f;
                float metallic = i / 3;
                Vector4 tint = new(0.75f, 0.76f, 0.78f, 1f);
                Matrix4x4 rowMajor = Matrix4x4.CreateScale(ReferenceSpheres[i].Radius * 2f) * Matrix4x4.CreateTranslation(ReferenceSpheres[i].Center);
                _propsLit.DrawMesh(_refSphere, RaylibMatrix.FromSystemNumerics(rowMajor), tint, rough, metallic);
            }

            if (_asset != null)
            {
                _modelLit.EnvSpecular = EnvSteps[_envStep];
                _modelLit.BeginFrame(_lighting, cam.position, _shadowMap, shadowTexelWorld: 0.03f);
                _modelLit.DrawModel(_asset.Model, ModelTransform());
            }

            Rl.EndMode3D();
        }

        private static RaylibMatrix ModelTransform()
        {
            LoadedAsset asset = _asset!;
            Matrix4x4 rowMajor =
                Matrix4x4.CreateRotationY(_turntableAngle) *
                Matrix4x4.CreateScale(asset.NormalizeScale) *
                Matrix4x4.CreateTranslation(-asset.Pivot);
            return RaylibMatrix.FromSystemNumerics(rowMajor);
        }

        // 第一列（远离模型的左侧，默认机位看是画面左）非金属，第二列金属；列内沿 Z 粗糙度递增。
        private static readonly (Vector3 Center, float Radius)[] ReferenceSpheres =
        {
            (new Vector3(-6.6f, 0.45f, -1.2f), 0.45f), (new Vector3(-6.6f, 0.45f, 0f), 0.45f), (new Vector3(-6.6f, 0.45f, 1.2f), 0.45f),
            (new Vector3(-5.2f, 0.45f, -1.2f), 0.45f), (new Vector3(-5.2f, 0.45f, 0f), 0.45f), (new Vector3(-5.2f, 0.45f, 1.2f), 0.45f),
        };

        private static void DrawHud()
        {
            Color white = new(245, 245, 245, 255);
            AcceptanceFont.Draw("Raylib 资产验收台 —— 把 .glb / .gltf / 含它们的文件夹拖进窗口", 12, 12, 22, white);

            int y = 44;
            if (_asset != null)
            {
                LoadedAsset a = _asset;
                AcceptanceFont.Draw($"资产: {a.DisplayName}", 12, y, 20, PanelAccent); y += 26;
                string clip = a.AnimCount > 0
                    ? $" · 动画 {a.AnimCount} 个，播放 {LoadedAsset.ClipName(&a.Animations[_clipIndex])} 帧 {(int)_clipFrameF}/{a.Animations[_clipIndex].frameCount} ×{_animSpeed:0.00}{(_animPlaying ? "" : " [暂停]")}"
                    : "";
                AcceptanceFont.Draw($"网格 {a.Model.meshCount} · 材质 {a.Model.materialCount} · 骨骼 {a.Model.boneCount}{clip}", 12, y, 18, white); y += 24;

                (int alb, int nrm, int orm, int emi) = MapCoverage(a.Inspections);
                AcceptanceFont.Draw(
                    $"贴图覆盖: albedo {alb}/{a.Inspections.Length} · normal {nrm}/{a.Inspections.Length} · ORM {orm}/{a.Inspections.Length} · emissive {emi}/{a.Inspections.Length}",
                    12, y, 18, white); y += 24;

                string factors = FormatFactors(a.Inspections);
                AcceptanceFont.Draw($"PBR 因子: {factors}", 12, y, 18, white); y += 24;
                if (a.FormatNote.Length > 0)
                {
                    AcceptanceFont.Draw(a.FormatNote, 12, y, 18, PanelDim); y += 24;
                }
            }
            else
            {
                AcceptanceFont.Draw("空台：等待拖入资产（或用 --model <路径> 启动装载）", 12, y, 18, PanelDim); y += 26;
                AcceptanceFont.Draw("支持: .glb / .gltf 原生装载 · .obj / .fbx / .dae 自动转 GLB（Assimp）", 12, y, 18, PanelDim); y += 24;
                AcceptanceFont.Draw("Sketchfab/Fab 选 glTF 下载；Unity 商店 FBX 直接拖入；zip 先解压", 12, y, 18, PanelDim); y += 24;
            }

            float elevation = MathF.Asin(Math.Clamp(_lighting.SunDirectionToward.Y, -1f, 1f)) * 180f / MathF.PI;
            string viewName = _modelLit.Mode switch
            {
                RaylibFileModelLit.ViewMode.Albedo => "albedo",
                RaylibFileModelLit.ViewMode.Normals => "法线",
                RaylibFileModelLit.ViewMode.Metallic => "金属度",
                RaylibFileModelLit.ViewMode.Roughness => "粗糙度",
                _ => "最终光照",
            };
            string alphaText = AlphaSteps[_alphaStep] > 0f ? AlphaSteps[_alphaStep].ToString("0.00") : "关";
            AcceptanceFont.Draw(
                $"太阳 {elevation:0}° ({(_sunAuto ? "自动" : "手动")}) · IBL ×{EnvSteps[_envStep]:0.0} · 视图: {viewName} · {(_modelLit.ScalarOverride ? "缺省标量 PBR" : "贴图 PBR")} · alpha剔除 {alphaText} · 转台 {(_turntable > 0f ? "开" : "关")}",
                12, y, 18, white); y += 26;

            AcceptanceFont.Draw("参考球列 = PBR 地面真值（左:非金属 右:金属 × 粗糙度 0.15/0.50/0.85）", 12, y, 18, PanelDim); y += 24;

            AcceptanceFont.Draw(
                "[1-5] 最终/albedo/法线/金属/粗糙  [O] 贴图↔缺省标量  [K] alpha剔除  [E] IBL  [T] 转台",
                12, WindowHeight - 52, 16, PanelDim);
            AcceptanceFont.Draw(
                "[拖放] 换资产  [SPACE] 播放/暂停  [N/P] 动画  [-/+] 速度  [,/.] 太阳  [R] 相机复位  [ESC] 退出",
                12, WindowHeight - 28, 16, PanelDim);

            if (_error.Length > 0)
            {
                string[] lines = WrapText(_error, 42);
                int panelHeight = 40 + lines.Length * 26;
                Rl.DrawRectangle(WindowWidth / 2 - 460, WindowHeight / 2 - panelHeight / 2, 920, panelHeight, new Color(30, 12, 14, 232));
                Rl.DrawRectangleLines(WindowWidth / 2 - 460, WindowHeight / 2 - panelHeight / 2, 920, panelHeight, PanelRed);
                int ey = WindowHeight / 2 - panelHeight / 2 + 18;
                AcceptanceFont.Draw("✗ 装载被拒绝（不静默降级）", WindowWidth / 2 - 440, ey, 20, PanelRed); ey += 32;
                foreach (string line in lines)
                {
                    AcceptanceFont.Draw(line, WindowWidth / 2 - 440, ey, 18, white); ey += 26;
                }

                AcceptanceFont.Draw("拖入新的 .glb / .gltf / 文件夹 重试", WindowWidth / 2 - 440, ey + 6, 16, PanelDim);
            }
        }

        private static (int Albedo, int Normal, int Orm, int Emissive) MapCoverage(RaylibFileModelLit.MaterialInspection[] inspections)
        {
            int alb = 0, nrm = 0, orm = 0, emi = 0;
            foreach (RaylibFileModelLit.MaterialInspection m in inspections)
            {
                if (m.HasAlbedo) alb++;
                if (m.HasNormal) nrm++;
                if (m.HasOrm) orm++;
                if (m.HasEmissive) emi++;
            }

            return (alb, nrm, orm, emi);
        }

        private static string FormatFactors(RaylibFileModelLit.MaterialInspection[] inspections)
        {
            if (inspections.Length == 0)
            {
                return "（模型无材质）";
            }

            var parts = new List<string>();
            int show = Math.Min(inspections.Length, 4);
            for (int i = 0; i < show; i++)
            {
                parts.Add($"#{i} 金属{inspections[i].MetallicFactor:0.00} 粗糙{inspections[i].RoughnessFactor:0.00}");
            }

            if (inspections.Length > show)
            {
                parts.Add($"…共{inspections.Length}材质");
            }

            return string.Join(" · ", parts);
        }

        private static string[] WrapText(string text, int maxChars)
        {
            var lines = new List<string>();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine;
                while (line.Length > maxChars)
                {
                    lines.Add(line[..maxChars]);
                    line = line[maxChars..];
                }

                lines.Add(line);
            }

            return lines.ToArray();
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

        private static bool HasOption(string[] args, string name)
        {
            return args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        }

        private static int ParseFrames(string[] args)
        {
            string? value = ParseOption(args, "--frames");
            return int.TryParse(value, out int frames) && frames > 0 ? frames : 240;
        }
    }
}
