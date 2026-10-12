using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// 群体统计走类型化遥测,不在 tick 上写会话黑板,也不在 tick 上做呈现摘要。
/// </summary>
[TestFixture]
public sealed class CrowdSimulationTelemetryChannelContractTests
{
    [Test]
    public void Tick_DoesNotWriteSessionBlackboardOrFormatPresenterSummary()
    {
        string path = Path.Combine(FindRepoRoot(), "src", "Core", "CrowdSimulation", "Runtime", "CrowdSimulationRuntime.cs");
        string source = File.ReadAllText(path);
        string tick = SliceMethod(source, "public void Tick()");
        Assert.That(tick, Does.Not.Contain("BlackboardFloatBuffer"));
        Assert.That(tick, Does.Not.Contain("ConfigKeyRegistry"));
        Assert.That(tick, Does.Not.Contain("BuildActiveDefinitionSummary"));
        Assert.That(tick, Does.Not.Contain("CrowdSimChecksum.Format"));
        Assert.That(source, Does.Not.Contain("crowd_simulation.session."));
    }

    private static string SliceMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
        int next = source.IndexOf("public void ", start + signature.Length, StringComparison.Ordinal);
        Assert.That(next, Is.GreaterThan(start));
        return source[start..next];
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
