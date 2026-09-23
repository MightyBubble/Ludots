using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class MassNavigationRuntimeBoundaryDebtRatchetTests
{
    private static readonly BoundaryDebtSpec[] TrackedDebt =
    {
        new("entity-id", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", "_spawnedEntityIds.Add(entity.Id"),
        new("entity-id", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", ".TryGetValue(entity.Id"),
        new("entity-id", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", "Dictionary<int, int> _controllableIndexByEntityId"),
        new("entity-id", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", "HashSet<int> _spawnedEntityIds"),
        new("pathing", "src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs", "IPathService"),
        new("pathing", "src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs", "PathDomain.Auto"),
        new("pathing", "src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs", "PathingConfig"),
        new("pathing", "src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs", "PathStore"),
        new("pathing", "src/Core/MassNavigation/Systems/MassNavigationMovePlanExecutionSystem.cs", "IPathService"),
        new("pathing", "src/Core/MassNavigation/Systems/MassNavigationMovePlanExecutionSystem.cs", "PathingConfig"),
        new("pathing", "src/Core/MassNavigation/Systems/MassNavigationMovePlanExecutionSystem.cs", "PathStore"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", "PresentationEntityLifecycle"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "MeshAssetDescriptor"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "MeshAssetRegistry"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "MeshAssetType"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "PresenterDefinitionRegistry"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Runtime/MassNavigationSimulationRuntime.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationAuthoredAgentBindingSystem.cs", "PresentationDestroyPending"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationAuthoredAgentBindingSystem.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationEnvironmentBindingSystem.cs", "PresentationDestroyPending"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationEnvironmentBindingSystem.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationLocomotionAnimatorParamSystem.cs", "PresenterCullState"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationLocomotionAnimatorParamSystem.cs", "PresenterFloatParams"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationLocomotionAnimatorParamSystem.cs", "PresenterState"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationLocomotionAnimatorParamSystem.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationObserverDisclosure.cs", "PresentationDestroyPending"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationObserverDisclosure.cs", "PresenterDefinitionRegistry"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationObserverDisclosure.cs", "using Ludots.Core.Presentation"),
        new("presentation", "src/Core/MassNavigation/Systems/MassNavigationSimulationStepSystem.cs", "using Ludots.Core.Presentation"),
        new("scenario", "src/Core/MassNavigation/MassNavigationIds.cs", "MapSession"),
        new("scenario", "src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs", "SpawnConfiguredScenario"),
        new("scenario", "src/Core/MassNavigation/Runtime/MassNavigationConfig.cs", "SpawnConfiguredScenario"),
        new("scenario", "src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs", "MapSession"),
        new("scenario", "src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs", "SpawnConfiguredScenario"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationObserverDisclosure.cs", "MapSession"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "MapSession"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "PlayerEntityLookup"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "RelationshipRuntime"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "RelationshipTeamBootstrapper"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "RelationshipTypeRegistry"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "ResolveScenarioTeamControlOwner"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "RuntimeEntitySpawnQueue"),
        new("scenario", "src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs", "SpawnConfiguredScenario"),
    };

    private static readonly BoundaryDebtToken[] Tokens =
    {
        new("presentation", "using Ludots.Core.Presentation"),
        new("presentation", "PresenterDefinitionRegistry"),
        new("presentation", "PresenterFloatParams"),
        new("presentation", "PresenterState"),
        new("presentation", "PresenterCullState"),
        new("presentation", "PresentationEntityLifecycle"),
        new("presentation", "PresentationDestroyPending"),
        new("presentation", "MeshAssetRegistry"),
        new("presentation", "MeshAssetDescriptor"),
        new("presentation", "MeshAssetType"),
        new("scenario", "RuntimeEntitySpawnQueue"),
        new("scenario", "MapSession"),
        new("scenario", "PlayerEntityLookup"),
        new("scenario", "RelationshipRuntime"),
        new("scenario", "RelationshipTypeRegistry"),
        new("scenario", "RelationshipTeamBootstrapper"),
        new("scenario", "ResolveScenarioTeamControlOwner"),
        new("scenario", "SpawnConfiguredScenario"),
        new("pathing", "IPathService"),
        new("pathing", "PathStore"),
        new("pathing", "PathingConfig"),
        new("pathing", "PathDomain.Auto"),
        new("entity-id", "HashSet<int> _spawnedEntityIds"),
        new("entity-id", "Dictionary<int, int> _controllableIndexByEntityId"),
        new("entity-id", ".TryGetValue(entity.Id"),
        new("entity-id", "_spawnedEntityIds.Add(entity.Id"),
    };

    [Test]
    public void MassNavigation_CoreBoundaryDebt_DoesNotGrowBeyondTrackedInventory()
    {
        string repoRoot = FindRepoRoot();
        string massNavigationRoot = Path.Combine(repoRoot, "src", "Core", "MassNavigation");
        Assert.That(Directory.Exists(massNavigationRoot), Is.True);

        string[] actual = EnumerateCurrentDebt(repoRoot, massNavigationRoot)
            .Select(static debt => debt.ToKey())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        string[] expected = TrackedDebt
            .Select(static debt => debt.ToKey())
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            actual,
            Is.EqualTo(expected),
            "MassNavigation boundary debt inventory changed. Remove entries when a seam is fixed; do not add new presentation, scenario, pathing, or Id-only runtime ownership inside Core/MassNavigation. Issue: #1644.");
    }

    private static IEnumerable<BoundaryDebtSpec> EnumerateCurrentDebt(string repoRoot, string massNavigationRoot)
    {
        foreach (string file in Directory.EnumerateFiles(massNavigationRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach ((int _, string text) in SourceTextScanner.ReadCodeLines(file))
            {
                for (int i = 0; i < Tokens.Length; i++)
                {
                    BoundaryDebtToken token = Tokens[i];
                    if (text.Contains(token.Text, StringComparison.Ordinal))
                    {
                        yield return new BoundaryDebtSpec(token.Category, relativePath, token.Text);
                    }
                }
            }
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Repo root not found.");
    }

    private readonly record struct BoundaryDebtToken(string Category, string Text);

    private readonly record struct BoundaryDebtSpec(string Category, string Path, string Token)
    {
        public string ToKey() => $"{Category}|{Path}|{Token}";
    }
}
