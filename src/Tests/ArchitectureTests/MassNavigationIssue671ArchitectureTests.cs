using System.Reflection;
using Ludots.Core.MassNavigation.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class MassNavigationIssue671ArchitectureTests
{
    [Test]
    public void FlowSolverEntitySync_DoesNotOwnPresentationCulling()
    {
        string repoRoot = FindRepoRoot();
        string entitySync = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "MassNavigation",
            "Runtime",
            "MassNavigationFlowSolverEntitySync.cs"));
        string solverState = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Core",
            "MassNavigation",
            "Runtime",
            "MassNavigationFlowSolverState.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(entitySync, Does.Not.Contain("SyncCullStates"));
            Assert.That(entitySync, Does.Not.Contain("CullState"));
            Assert.That(entitySync, Does.Not.Contain("Ludots.Core.Presentation.Components"));
            Assert.That(solverState, Does.Not.Contain("Ludots.Core.Presentation.Components"));
        });
    }

    [Test]
    public void ActiveHotZoneSelection_IsOwnedBySimulationRuntime()
    {
        MethodInfo? configMutation = typeof(MassNavigationWorldConfig).GetMethod(
            "SetActiveHotZone",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo? runtimeState = typeof(MassNavigationSimulationRuntime).GetField(
            "_activeHotZoneId",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(configMutation, Is.Null, "Validated authoring config must not expose runtime hot-zone mutation.");
            Assert.That(runtimeState, Is.Not.Null, "Simulation runtime must own its resolved active hot-zone state.");
            Assert.That(runtimeState?.FieldType, Is.EqualTo(typeof(string)));
        });
    }

    [Test]
    public void FloatSolverDecision_IsPublishedAndIndexed()
    {
        string repoRoot = FindRepoRoot();
        const string relativePath = "gitbook/architecture/mass-navigation-numeric-domain.md";
        const string summaryPath = "architecture/mass-navigation-numeric-domain.md";
        string decisionPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string decision = File.ReadAllText(decisionPath);
        string architectureIndex = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "README.md"));
        string summary = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "SUMMARY.md"));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Does.Contain("## 1. 概述"));
            Assert.That(decision, Does.Contain("## 2. 结构"));
            Assert.That(decision, Does.Contain("## 3. 详情"));
            Assert.That(decision, Does.Contain("## 4. 场景"));
            Assert.That(decision, Does.Contain("## 5. 边界"));
            Assert.That(decision, Does.Contain("## 6. UAT"));
            Assert.That(decision, Does.Contain("float"));
            Assert.That(decision, Does.Contain("Fix64"));
            Assert.That(decision, Does.Contain("WorldPositionCm"));
            Assert.That(decision, Does.Contain("确定性边界"));
            Assert.That(decision, Does.Contain("Arrived"));
            Assert.That(decision, Does.Contain("Order"));
            Assert.That(decision, Does.Contain("GetAgentWorldPositionCm"));
            Assert.That(decision, Does.Contain("功能:"));
            Assert.That(decision, Does.Contain("场景:"));
            Assert.That(architectureIndex, Does.Contain("mass-navigation-numeric-domain.md"));
            Assert.That(summary, Does.Contain(summaryPath));
        });
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "Core")) &&
                Directory.Exists(Path.Combine(current.FullName, "gitbook", "architecture")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Ludots repository root.");
    }
}
