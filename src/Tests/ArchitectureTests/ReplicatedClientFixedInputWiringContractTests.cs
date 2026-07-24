using System;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class ReplicatedClientFixedInputWiringContractTests
{
    [Test]
    public void GameEngineTick_UsesRawPlatformDeltaForReplicatedClientPumpAndClock()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Core", "Engine", "GameEngine.cs"));
        int tickIndex = source.IndexOf("public void Tick(float platformDeltaTime)", StringComparison.Ordinal);
        Assert.That(tickIndex, Is.GreaterThanOrEqualTo(0));
        string tickBody = source[tickIndex..];

        Assert.That(
            tickBody,
            Does.Contain("networkRuntime!.PumpReplicatedClient(platformDeltaTime);"));
        Assert.That(
            tickBody,
            Does.Contain("AdvanceReplicatedClientFixedInputClock(platformDeltaTime);"));
        Assert.That(
            tickBody,
            Does.Not.Contain("PumpReplicatedClient(dt);"));
    }

    [Test]
    public void GameEngineStart_RequiresCompositeClientPort_ButNotMapOwnedClockOrPayloadSource()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Core", "Engine", "GameEngine.cs"));
        int validateIndex = source.IndexOf(
            "private void ValidateNetworkRuntimeBeforeStart()",
            StringComparison.Ordinal);
        Assert.That(validateIndex, Is.GreaterThanOrEqualTo(0));
        string validateBody = source[validateIndex..];
        int nextMethod = validateBody.IndexOf(
            "private INetworkRuntimePort GetRequiredNetworkRuntime()",
            StringComparison.Ordinal);
        Assert.That(nextMethod, Is.GreaterThan(0));
        validateBody = validateBody[..nextMethod];

        Assert.That(
            validateBody,
            Does.Contain("Replicated client start requires the composite IReplicatedClientNetworkRuntimePort identity."));
        Assert.That(
            validateBody,
            Does.Not.Contain("Replicated client start requires ReplicatedClientFixedInputClock."));
        Assert.That(
            validateBody,
            Does.Not.Contain("Replicated client start requires FixedInputPayloadSource."));
    }

    [Test]
    public void LiteNetLibInstaller_DefersClientValidationAndClockUntilFirstMaterialization()
    {
        string source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "src",
                "Adapters",
                "Networking",
                "Ludots.Adapter.LiteNetLib",
                "LiteNetLibNetworkRuntimeInstaller.cs"));

        int installIndex = source.IndexOf("public static void Install(", StringComparison.Ordinal);
        Assert.That(installIndex, Is.GreaterThanOrEqualTo(0));
        string installBody = source[installIndex..];
        int validateMethod = installBody.IndexOf(
            "internal static ClientCompositionPlan ValidateClientCompositionBeforeEndpointOpen(",
            StringComparison.Ordinal);
        Assert.That(validateMethod, Is.GreaterThan(0));
        installBody = installBody[..validateMethod];

        Assert.That(installBody, Does.Contain("MaterializeClient("));
        Assert.That(installBody, Does.Not.Contain("ValidateClientCompositionBeforeEndpointOpen("));
        Assert.That(installBody, Does.Not.Contain("PublishClientFixedInputClock("));

        Assert.That(source, Does.Contain("private static INetworkRuntimePort MaterializeClient("));
        Assert.That(
            source,
            Does.Contain("ClientCompositionPlan plan = ValidateClientCompositionBeforeEndpointOpen(engine, config);"));
        Assert.That(source, Does.Contain("PublishClientFixedInputClock(engine, outerClientPort, plan);"));
    }

    [Test]
    public void CompositeClientPort_IsDeclaredAsSingleRuntimeContract()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "Core", "Networking", "Runtime", "NetworkRuntimeContracts.cs"));
        Assert.That(
            source,
            Does.Contain(
                "public interface IReplicatedClientNetworkRuntimePort : INetworkRuntimePort, IReplicatedClientFixedInputPort"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && directory != null; i++)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }
}
