using System.Xml.Linq;
using Ludots.Contracts;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
public sealed class S14LayeringReferenceGraphTests
{
    [Test]
    public void Contracts_DoNotReferenceCoreOrGameplayAssemblies()
    {
        AssertNoForbiddenProjectReferences(
            Path.Combine("src", "Contracts", "Ludots.Contracts.csproj"),
            "Ludots.Core.csproj",
            "Ludots.Engine",
            "Ludots.Graph.Abstractions",
            "Presentation",
            "GAS");
    }

    [Test]
    public void GraphAbstractions_DoNotReferenceCorePresentationOrGas()
    {
        AssertNoForbiddenProjectReferences(
            Path.Combine("src", "Graph", "Ludots.Graph.Abstractions", "Ludots.Graph.Abstractions.csproj"),
            "Ludots.Core.csproj",
            "Ludots.Engine",
            "Presentation",
            "Ludots.GAS");
    }

    [Test]
    public void SystemGroup_LivesInContractsAssembly()
    {
        Assert.That(typeof(SystemGroup).Assembly.GetName().Name, Is.EqualTo("Ludots.Contracts"));
        Assert.That(typeof(SystemGroupOrder).Assembly.GetName().Name, Is.EqualTo("Ludots.Contracts"));
        Assert.That(SystemGroupOrder.All, Is.EqualTo(Enum.GetValues<SystemGroup>()));
    }

    [Test]
    public void GraphContracts_LiveInGraphAbstractionsAssembly()
    {
        Assert.That(typeof(GraphKind).Assembly.GetName().Name, Is.EqualTo("Ludots.Graph.Abstractions"));
        Assert.That(typeof(GraphInstruction).Assembly.GetName().Name, Is.EqualTo("Ludots.Graph.Abstractions"));
        Assert.That(typeof(GraphNodeOp).Assembly.GetName().Name, Is.EqualTo("Ludots.Graph.Abstractions"));
    }

    [Test]
    public void SimulationOwnedPresentationMarkers_LiveInContracts_WithWriteOwner()
    {
        Assert.That(typeof(PresentationStableId).Assembly.GetName().Name, Is.EqualTo("Ludots.Contracts"));
        Assert.That(typeof(PresentationDestroyPending).Assembly.GetName().Name, Is.EqualTo("Ludots.Contracts"));

        WriteOwnerAttribute? stableOwner = typeof(PresentationStableId).GetCustomAttributes(typeof(WriteOwnerAttribute), false)
            .Cast<WriteOwnerAttribute>()
            .SingleOrDefault();
        WriteOwnerAttribute? pendingOwner = typeof(PresentationDestroyPending).GetCustomAttributes(typeof(WriteOwnerAttribute), false)
            .Cast<WriteOwnerAttribute>()
            .SingleOrDefault();

        Assert.That(stableOwner, Is.Not.Null);
        Assert.That(stableOwner!.Owner, Is.EqualTo(LayerOwner.Simulation));
        Assert.That(pendingOwner, Is.Not.Null);
        Assert.That(pendingOwner!.Owner, Is.EqualTo(LayerOwner.Simulation));
    }

    private static void AssertNoForbiddenProjectReferences(string relativeProject, params string[] forbiddenTokens)
    {
        string repoRoot = FindRepoRoot();
        string projectPath = Path.Combine(repoRoot, relativeProject);
        Assert.That(File.Exists(projectPath), Is.True, projectPath);

        XDocument document = XDocument.Load(projectPath);
        string[] includes = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();

        foreach (string token in forbiddenTokens)
        {
            Assert.That(
                includes.Any(include => include.Contains(token, StringComparison.OrdinalIgnoreCase)),
                Is.False,
                $"{relativeProject} must not reference '{token}'. Actual: {string.Join("; ", includes)}");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }
}
