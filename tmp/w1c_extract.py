import re

path = "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs"
with open(path, encoding="utf-8", newline="") as f:
    src = f.read()

def member_block(sig):
    idx = src.find(sig)
    assert idx >= 0, sig[:60]
    line_start = src.rfind("\n", 0, idx) + 1
    open_brace = src.find("{", idx)
    depth = 0
    end = None
    for i in range(open_brace, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                break
    while src[end:end+2] == "\n":
        end += 2
    seg = src[:line_start]
    if seg.endswith("\n\n"):
        line_start -= 1
    return src[line_start:end], line_start, end

def remove(sig):
    global src
    block, ls, e = member_block(sig)
    src = src[:ls] + src[e:]
    return block

read_env_frame = remove("        private static int[] ReadEnvFrameList(string key)")
build_seq = remove("        private static string BuildSequencedScreenshotPath(string targetPath, int sequenceIndex, int frame)")
validate = remove("        internal static void ValidateRuntimeScreenshotEvidence(string screenshotPath")
flat = remove("        private static bool IsVisuallyFlat(SKBitmap bitmap)")
color_dist = remove("        private static int ColorDistance(SKColor a, SKColor b)")

hud_start_sig = "        private static void DrawLightweightDiagnosticHud(GameEngine engine"
idx = src.find(hud_start_sig)
assert idx >= 0
line_s = src.rfind("\n", 0, idx) + 1
glyph_idx = src.find("        private static ulong GetDiagnosticGlyph(char c)", idx)
assert glyph_idx > idx
m = re.search(r"\n        (?:private|internal|public) static ", src[glyph_idx + 100:])
assert m
line_e = glyph_idx + 100 + m.start() + 1
hud_block = src[line_s:line_e]
src = src[:line_s] + src[line_e:]

old_env = (
    '                string? screenshotPath = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");\n'
    '                string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");\n'
    '                string? screenshotTargetPath = string.IsNullOrWhiteSpace(screenshotPath)\n'
    '                    ? null\n'
    '                    : Path.GetFullPath(screenshotPath);\n'
    '                string? screenshotFileName = string.IsNullOrWhiteSpace(screenshotTargetPath)\n'
    '                    ? null\n'
    '                    : Path.GetFileName(screenshotTargetPath);\n'
    '                int[] screenshotFrames = ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");\n'
    '                int screenshotSequenceIndex = 0;\n'
    '                bool screenshotSequenceEnabled = screenshotFrames.Length > 0;\n'
    '                bool screenshotPending = !string.IsNullOrWhiteSpace(screenshotFileName) &&\n'
    '                                         (!screenshotSequenceEnabled || screenshotFrames.Length > 0);\n'
    '                int screenshotFrame = screenshotSequenceEnabled\n'
    '                    ? screenshotFrames[0]\n'
    '                    : int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAME"), out int parsedScreenshotFrame)\n'
    '                    ? Math.Max(1, parsedScreenshotFrame)\n'
    '                    : 60;\n'
    '                int autoExitFrame = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_AUTO_EXIT_FRAME"), out int parsedAutoExitFrame)\n'
    '                    ? Math.Max(0, parsedAutoExitFrame)\n'
    '                    : 0;\n'
    '                int minRuntimeMsBeforeScreenshot = ReadEnvIntOrDefault("LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT", 0);'
)
assert old_env in src, "env block not found"
new_env = (
    '                string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");\n'
    '                RaylibScreenshotEvidenceRecorder? screenshotRecorder = RaylibScreenshotEvidenceRecorder.TryCreateFromEnvironment();\n'
    '                int autoExitFrame = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_AUTO_EXIT_FRAME"), out int parsedAutoExitFrame)\n'
    '                    ? Math.Max(0, parsedAutoExitFrame)\n'
    '                    : 0;'
)
src = src.replace(old_env, new_env, 1)

assert "DrawLightweightDiagnosticHud(engine, presentationTiming);" in src
src = src.replace("DrawLightweightDiagnosticHud(engine, presentationTiming);",
                  "RaylibDiagnosticHud.Draw(engine, presentationTiming);", 1)

cap_start = src.find("                        if (screenshotPending && frameIndex >= screenshotFrame")
assert cap_start >= 0
cap_end_marker = 'Log.Info(in LogChannels.Engine, $"Captured runtime screenshot: {fullScreenshotPath}");'
cap_end = src.find(cap_end_marker, cap_start)
cap_end = src.find("\n", cap_end) + 1
cap_end = src.find("                        }", cap_end)
cap_end = src.find("\n", cap_end) + 1
new_cap = '''                        if (screenshotRecorder != null &&
                            screenshotRecorder.ShouldCapture(frameIndex, runtimeStopwatch.ElapsedMilliseconds))
                        {
                            long screenshotElapsedMs = screenshotRecorder.CaptureFrame(
                                frameIndex,
                                lastW,
                                lastH,
                                writeDiagnostics: () =>
                                {
                                    AppendRaylibDiagnostic(
                                        diagnosticPath,
                                        $"screenshot frame={frameIndex} cameraPos=({activeCamera.position.X:F2},{activeCamera.position.Y:F2},{activeCamera.position.Z:F2}) cameraTarget=({activeCamera.target.X:F2},{activeCamera.target.Y:F2},{activeCamera.target.Z:F2})");
                                    AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                                    AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildVisualKindDiagnosticSummary());
                                    if (engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshesForDiagnostics))
                                    {
                                        AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildPrimitiveLaneDiagnosticSummary(meshesForDiagnostics));
                                    }

                                    AppendRaylibDiagnostic(diagnosticPath, BuildInputSelectionDiagnostic(engine));
                                });
                            presentationTiming?.ObserveScreenshot(screenshotElapsedMs);
                        }
'''
src = src[:cap_start] + new_cap + src[cap_end:]

src = src.replace("if (autoExitFrame > 0 && frameIndex >= autoExitFrame && !screenshotPending)",
                  "if (autoExitFrame > 0 && frameIndex >= autoExitFrame && screenshotRecorder?.Pending != true)", 1)

with open(path, "w", encoding="utf-8", newline="") as f:
    f.write(src)

recorder = (
    'using System;\n'
    'using System.IO;\n'
    'using SkiaSharp;\n'
    '\n'
    'namespace Ludots.Adapter.Raylib\n'
    '{\n'
    '    /// <summary>\n'
    '    /// 运行时截图取证器：LUDOTS_TAKE_SCREENSHOT_PATH/FRAMES/FRAME 与 LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT 合同的持有者，\n'
    '    /// 含序列帧命名、落盘搬运与尺寸/平坦度校验。时序基准（frameIndex、runtime 毫秒）由宿主逐帧显式传入（#1325）。\n'
    '    /// </summary>\n'
    '    internal sealed class RaylibScreenshotEvidenceRecorder\n'
    '    {\n'
    '        private readonly string _targetPath;\n'
    '        private readonly int[] _sequenceFrames;\n'
    '        private readonly int _minRuntimeMs;\n'
    '        private int _sequenceIndex;\n'
    '        private int _currentFrame;\n'
    '        private bool _pending;\n'
    '\n'
    '        private RaylibScreenshotEvidenceRecorder(string targetPath, int[] sequenceFrames, int currentFrame, int minRuntimeMs)\n'
    '        {\n'
    '            _targetPath = targetPath;\n'
    '            _sequenceFrames = sequenceFrames;\n'
    '            _currentFrame = currentFrame;\n'
    '            _minRuntimeMs = minRuntimeMs;\n'
    '            _pending = true;\n'
    '        }\n'
    '\n'
    '        public bool Pending => _pending;\n'
    '\n'
    '        public static RaylibScreenshotEvidenceRecorder? TryCreateFromEnvironment()\n'
    '        {\n'
    '            string? raw = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");\n'
    '            if (string.IsNullOrWhiteSpace(raw))\n'
    '            {\n'
    '                return null;\n'
    '            }\n'
    '\n'
    '            string targetPath = Path.GetFullPath(raw);\n'
    '            int[] sequenceFrames = ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");\n'
    '            bool sequenceEnabled = sequenceFrames.Length > 0;\n'
    '            int currentFrame = sequenceEnabled\n'
    '                ? sequenceFrames[0]\n'
    '                : int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAME"), out int parsed)\n'
    '                    ? Math.Max(1, parsed)\n'
    '                    : 60;\n'
    '            int minRuntimeMs = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT", 0);\n'
    '            return new RaylibScreenshotEvidenceRecorder(targetPath, sequenceFrames, currentFrame, minRuntimeMs);\n'
    '        }\n'
    '\n'
    '        public bool ShouldCapture(int frameIndex, long runtimeElapsedMs)\n'
    '        {\n'
    '            return _pending && frameIndex >= _currentFrame && runtimeElapsedMs >= _minRuntimeMs;\n'
    '        }\n'
    '\n'
    '        /// <summary>落盘一张取证截图并推进序列状态；返回 TakeScreenshot..校验 的耗时毫秒数。writeDiagnostics 在截图前回调（宿主追加时序敏感诊断）。</summary>\n'
    '        public long CaptureFrame(int frameIndex, int expectedWidth, int expectedHeight, Action writeDiagnostics)\n'
    '        {\n'
    '            string fullScreenshotPath = _sequenceFrames.Length > 0\n'
    '                ? BuildSequencedScreenshotPath(_targetPath, _sequenceIndex, _currentFrame)\n'
    '                : _targetPath;\n'
    '            string screenshotFile = Path.GetFileName(fullScreenshotPath);\n'
    '            string screenshotWorkingFilePath = Path.Combine(Environment.CurrentDirectory, screenshotFile);\n'
    '            string? screenshotDirectory = Path.GetDirectoryName(fullScreenshotPath);\n'
    '            if (!string.IsNullOrWhiteSpace(screenshotDirectory))\n'
    '            {\n'
    '                Directory.CreateDirectory(screenshotDirectory);\n'
    '            }\n'
    '\n'
    '            writeDiagnostics();\n'
    '\n'
    '            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();\n'
    '            Raylib_cs.Raylib.TakeScreenshot(screenshotFile);\n'
    '            if (!string.Equals(screenshotWorkingFilePath, fullScreenshotPath, StringComparison.OrdinalIgnoreCase) &&\n'
    '                File.Exists(screenshotWorkingFilePath))\n'
    '            {\n'
    '                File.Copy(screenshotWorkingFilePath, fullScreenshotPath, overwrite: true);\n'
    '                File.Delete(screenshotWorkingFilePath);\n'
    '            }\n'
    '\n'
    '            ValidateRuntimeScreenshotEvidence(fullScreenshotPath, expectedWidth, expectedHeight);\n'
    '            long elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000L / System.Diagnostics.Stopwatch.Frequency;\n'
    '\n'
    '            if (_sequenceFrames.Length > 0)\n'
    '            {\n'
    '                _sequenceIndex++;\n'
    '                _pending = _sequenceIndex < _sequenceFrames.Length;\n'
    '                if (_pending)\n'
    '                {\n'
    '                    _currentFrame = _sequenceFrames[_sequenceIndex];\n'
    '                }\n'
    '            }\n'
    '            else\n'
    '            {\n'
    '                _pending = false;\n'
    '            }\n'
    '\n'
    '            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine, $"Captured runtime screenshot: {fullScreenshotPath}");\n'
    '            return elapsedMs;\n'
    '        }\n'
    '\n'
) + read_env_frame + "\n" + build_seq + "\n" + validate + "\n" + flat + "\n" + color_dist + "    }\n}\n"

with open("src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibScreenshotEvidenceRecorder.cs", "w", encoding="utf-8", newline="") as f:
    f.write(recorder.replace("\n", "\r\n"))

hud = (
    'using System;\n'
    'using Ludots.Core.Diagnostics;\n'
    'using Ludots.Core.Engine;\n'
    'using Ludots.Core.Presentation.Hud;\n'
    'using Ludots.Core.Scripting;\n'
    'using Ludots.Platform.Abstractions;\n'
    'using Raylib_cs;\n'
    'using Rl = Raylib_cs.Raylib;\n'
    '\n'
    'namespace Ludots.Adapter.Raylib\n'
    '{\n'
    '    /// <summary>\n'
    '    /// 轻量原生诊断 HUD（点阵字形、零资产依赖）：LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD 开关驱动的\n'
    '    /// FPS/帧耗时/车道计数只读面板。绘制发生在覆盖层合成之后、EndDrawing 之前（#1325 自宿主拆出）。\n'
    '    /// </summary>\n'
    '    internal static class RaylibDiagnosticHud\n'
    '    {\n'
    '        public static void Draw(GameEngine engine, PresentationTimingDiagnostics? timing)\n'
    '        {\n'
    '            DrawLightweightDiagnosticHud(engine, timing);\n'
    '        }\n'
    '\n'
) + hud_block + "    }\n}\n"

with open("src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibDiagnosticHud.cs", "w", encoding="utf-8", newline="") as f:
    f.write(hud.replace("\n", "\r\n"))

tests = "src/Tests/RaylibAdapterTests/RaylibScreenshotEvidenceTests.cs"
with open(tests, encoding="utf-8", newline="") as f:
    t = f.read()
t = t.replace("RaylibHostLoop.ValidateRuntimeScreenshotEvidence", "RaylibScreenshotEvidenceRecorder.ValidateRuntimeScreenshotEvidence")
with open(tests, "w", encoding="utf-8", newline="") as f:
    f.write(t)

print("clean redo complete")
