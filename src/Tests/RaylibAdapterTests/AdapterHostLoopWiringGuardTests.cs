using NUnit.Framework;

namespace Ludots.Adapter.Raylib.Tests;

/// <summary>
/// Adapter host-loop 多座位接线存在性守卫。RaylibHostLoop 的 present binding 接线曾被外部合并
/// 静默删除（headless 测试与 CI 全绿，一天无人发现），因此用源码锚定合同测试锁住接线标记。
/// </summary>
[TestFixture]
public sealed class AdapterHostLoopWiringGuardTests
{
    private const string AlarmMessage =
        "多座位接线被删/回退时此守卫报警；恢复接线或同步守卫需走 PR 说明，禁止静默变更。";

    [Test]
    public void RaylibHostLoop_Source_KeepsMultiSeatPresentBindingWiringAndDropsSolePipeline()
    {
        string hostSource = ReadHostLoopSource(
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs");

        Assert.That(
            hostSource,
            Does.Contain("Ludots.Core.Client.PresentBindingPresentation.TryArmPresentBindingCullingPasses("),
            "RaylibHostLoop 缺 TryArmPresentBindingCullingPasses 接线。" + AlarmMessage);
        Assert.That(
            hostSource,
            Does.Contain("Ludots.Core.Client.PresentBindingPresentation.TryDrivePresentBindings("),
            "RaylibHostLoop 缺 TryDrivePresentBindings 接线。" + AlarmMessage);
        Assert.That(
            hostSource,
            Does.Contain("new PresentBindingScreenRayProvider("),
            "RaylibHostLoop 缺 PresentBindingScreenRayProvider 注册。" + AlarmMessage);
        Assert.That(
            hostSource,
            Does.Contain("CoreServiceKeys.ScreenRayProvider"),
            "RaylibHostLoop 缺 ScreenRayProvider 服务挂载。" + AlarmMessage);

        Assert.That(
            hostSource,
            Does.Not.Contain("TryEnsureSolePresentBindingPipeline"),
            "RaylibHostLoop 回退到旧 sole 管线调用。" + AlarmMessage);
        Assert.That(
            hostSource,
            Does.Not.Contain("TrySyncSolePresentPipeline"),
            "RaylibHostLoop 回退到旧 sole 管线同步。" + AlarmMessage);
    }

    [Test]
    public void WebHostLoop_Source_KeepsMultiSeatPresentBindingWiring()
    {
        string hostSource = ReadHostLoopSource(
            "src/Adapters/Web/Ludots.Adapter.Web/WebHostLoop.cs");

        Assert.That(
            hostSource,
            Does.Contain("Ludots.Core.Client.PresentBindingPresentation.TryArmPresentBindingCullingPasses("),
            "WebHostLoop 缺 TryArmPresentBindingCullingPasses 接线。" + AlarmMessage);
        Assert.That(
            hostSource,
            Does.Contain("Ludots.Core.Client.PresentBindingPresentation.TryDrivePresentBindings("),
            "WebHostLoop 缺 TryDrivePresentBindings 接线。" + AlarmMessage);
    }

    private static string ReadHostLoopSource(string relativePath)
    {
        string fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.That(
            File.Exists(fullPath),
            Is.True,
            $"HostLoop 源文件不存在：{fullPath}。{AlarmMessage}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
