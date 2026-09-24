using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LauncherProcessGroupContractTests
{
    [Test]
    public void ProcessGroupValidation_RejectsInvalidTopology()
    {
        var valid = CreateValidProcessGroup();
        Assert.DoesNotThrow(() => LauncherProcessGroupValidation.Validate(valid, static _ => true));

        var missingServer = CreateValidProcessGroup();
        missingServer.Processes.RemoveAll(process => process.ProcessRole == "authoritativeServer");
        Assert.That(
            () => LauncherProcessGroupValidation.Validate(missingServer, static _ => true),
            Throws.InvalidOperationException.With.Message.Contains("exactly one authoritativeServer"));

        var duplicateClientIds = CreateValidProcessGroup();
        duplicateClientIds.Processes[2].ClientInstanceId = duplicateClientIds.Processes[1].ClientInstanceId;
        Assert.That(
            () => LauncherProcessGroupValidation.Validate(duplicateClientIds, static _ => true),
            Throws.InvalidOperationException.With.Message.Contains("clientInstanceId"));

        var unsupportedAdapter = CreateValidProcessGroup();
        unsupportedAdapter.Processes[0].AdapterId = "browser-only";
        Assert.That(
            () => LauncherProcessGroupValidation.Validate(unsupportedAdapter, static id => id != "browser-only"),
            Throws.InvalidOperationException.With.Message.Contains("unsupported adapter"));
    }

    [Test]
    public void MaterializeProcessGroupArtifacts_WritesFreshRoleBootstrapsWithSharedContentFingerprint()
    {
        var repoRoot = FindRepoRoot();
        var service = new LauncherService(repoRoot);
        var resolve = service.Resolve(
            new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
            LauncherPlatformIds.Raylib,
            LauncherBuildMode.Never);
        var processGroup = service.TryResolveProcessGroup(new[] { "preset:rts_multiplayer_frontline_networked_raylib" });
        Assert.That(processGroup, Is.Not.Null);

        var artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "tests",
            $"process-group-materialize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactDirectory);
        var sentinelPath = Path.Combine(artifactDirectory, "keep-me.txt");
        File.WriteAllText(sentinelPath, "sentinel");

        try
        {
            var artifacts = service.MaterializeProcessGroupArtifacts(
                resolve.Plan,
                processGroup!,
                artifactDirectory,
                connectionKey: "unit-test-connection-key");

            Assert.That(File.Exists(sentinelPath), Is.True, "Prepare must not wipe the caller artifact directory.");
            Assert.That(artifacts.ContentPlanFingerprint, Is.EqualTo(resolve.Plan.PlanFingerprint));
            Assert.That(artifacts.Roles, Has.Count.EqualTo(3));
            Assert.That(artifacts.Roles.Count(role => role.ProcessRole == "authoritativeServer"), Is.EqualTo(1));
            Assert.That(artifacts.Roles.Count(role => role.ProcessRole == "replicatedClient"), Is.EqualTo(2));

            foreach (var role in artifacts.Roles)
            {
                Assert.That(File.Exists(role.GraphPath), Is.True);
                Assert.That(File.Exists(role.BootstrapPath), Is.True);
                var bootstrap = JsonSerializer.Deserialize<AppBootstrapConfig>(
                    File.ReadAllText(role.BootstrapPath),
                    StrictJsonOptions.CreateExact());
                Assert.That(bootstrap, Is.Not.Null);
                Assert.That(bootstrap!.PlanFingerprint, Is.EqualTo(resolve.Plan.PlanFingerprint));
                Assert.That(bootstrap.PlanOrderedModIds, Is.EqualTo(resolve.Plan.OrderedModIds));
                Assert.That(bootstrap.NetworkHost, Is.Not.Null);
                Assert.That(bootstrap.NetworkHost!.ConnectionKey, Is.EqualTo("unit-test-connection-key"));
                Assert.That(bootstrap.NetworkHost.Port, Is.EqualTo(processGroup!.Port));
                bootstrap.NetworkHost.Validate();

                var graph = JsonSerializer.Deserialize<LauncherGraphDocument>(
                    File.ReadAllText(role.GraphPath),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                Assert.That(graph, Is.Not.Null);
                Assert.That(graph!.PlanFingerprint, Is.EqualTo(resolve.Plan.PlanFingerprint));
                Assert.That(graph.RuntimeArtifacts.BootstrapArtifactPath, Is.EqualTo(role.BootstrapPath));
                Assert.That(graph.RuntimeArtifacts.GraphArtifactPath, Is.EqualTo(role.GraphPath));
            }

            var clientCredentials = artifacts.Roles
                .Where(role => role.ProcessRole == "replicatedClient")
                .Select(role => role.CredentialPath)
                .ToList();
            Assert.That(clientCredentials, Has.Count.EqualTo(2));
            Assert.That(clientCredentials[0], Is.Not.EqualTo(clientCredentials[1]));
            Assert.That(clientCredentials.All(path => path.StartsWith(artifactDirectory, StringComparison.OrdinalIgnoreCase)), Is.True);
        }
        finally
        {
            if (Directory.Exists(artifactDirectory))
            {
                Directory.Delete(artifactDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void FormalFrontlineShowcase_PrefersProcessGroupPreset_AndRejectsBindingOnlyNetworkedEntry()
    {
        var repoRoot = FindRepoRoot();
        var registry = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")))?.AsObject()
            ?? throw new InvalidOperationException("showcase.registry.json is missing.");
        var showcases = registry["showcases"]?.AsArray()
            ?? throw new InvalidOperationException("showcase.registry.json is missing showcases.");
        var formal = showcases
            .Select(node => node?.AsObject())
            .FirstOrDefault(entry => string.Equals(entry?["id"]?.GetValue<string>(), "rts_multiplayer_frontline", StringComparison.Ordinal));
        Assert.That(formal, Is.Not.Null);

        var binding = formal!["binding"]?.GetValue<string>();
        var presetId = formal["preset"]?.GetValue<string>();
        Assert.That(binding, Is.EqualTo("rts_multiplayer_frontline_networked"));
        Assert.That(presetId, Is.EqualTo("rts_multiplayer_frontline_networked_raylib"));

        var showcaseTs = File.ReadAllText(
            Path.Combine(repoRoot, "src", "Tools", "Ludots.Launcher.React", "src", "lib", "showcase.ts"));
        var presetIndex = showcaseTs.IndexOf("if (entry.preset)", StringComparison.Ordinal);
        var bindingIndex = showcaseTs.IndexOf("if (entry.binding)", StringComparison.Ordinal);
        Assert.That(presetIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(bindingIndex, Is.GreaterThan(presetIndex), "Gallery launchHint must prefer preset over binding.");

        var service = new LauncherService(repoRoot);
        var processGroup = service.TryResolveProcessGroup(new[] { $"preset:{presetId}" });
        Assert.That(processGroup, Is.Not.Null);
        Assert.That(processGroup!.Processes.Count(process => process.ProcessRole == "authoritativeServer"), Is.EqualTo(1));
        Assert.That(processGroup.Processes.Count(process => process.ProcessRole == "replicatedClient"), Is.EqualTo(2));
        Assert.That(
            processGroup.Processes.Any(process =>
                process.ProcessRole == "authoritativeServer" &&
                string.Equals(process.AdapterId, LauncherPlatformIds.DedicatedServer, StringComparison.OrdinalIgnoreCase)),
            Is.True);

        Assert.That(
            service.TryResolveProcessGroup(new[] { $"${binding}" }),
            Is.Null,
            "Binding-only launch must not silently invent a process group.");

        var presets = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")))?.AsObject()
            ?? throw new InvalidOperationException("launcher.presets.json is missing.");
        var networkedPreset = presets["presets"]!.AsArray()
            .Select(node => node!.AsObject())
            .First(entry => string.Equals(entry["id"]?.GetValue<string>(), presetId, StringComparison.Ordinal));
        Assert.That(networkedPreset["processGroup"], Is.Not.Null);
        Assert.That(networkedPreset.ContainsKey("connectionKey"), Is.False, "Connection keys must never be stored in presets.");
    }

    [Test]
    public void DedicatedServer_IsInternalLaunchableAdapter()
    {
        var service = new LauncherService(FindRepoRoot());
        var state = service.GetState();
        Assert.That(
            state.Platforms.Any(platform =>
                string.Equals(platform.Id, LauncherPlatformIds.DedicatedServer, StringComparison.OrdinalIgnoreCase)),
            Is.True);
    }

    private static LauncherProcessGroupDefinition CreateValidProcessGroup()
    {
        return new LauncherProcessGroupDefinition
        {
            Host = "127.0.0.1",
            Port = 27709,
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            Processes =
            [
                new LauncherProcessGroupMemberDefinition
                {
                    Name = "server",
                    AdapterId = LauncherPlatformIds.DedicatedServer,
                    ProcessRole = "authoritativeServer",
                    ClientInstanceId = 0,
                    FaultSeed = 1
                },
                new LauncherProcessGroupMemberDefinition
                {
                    Name = "client-a",
                    AdapterId = LauncherPlatformIds.Raylib,
                    ProcessRole = "replicatedClient",
                    ClientInstanceId = 1,
                    FaultSeed = 2
                },
                new LauncherProcessGroupMemberDefinition
                {
                    Name = "client-b",
                    AdapterId = LauncherPlatformIds.Raylib,
                    ProcessRole = "replicatedClient",
                    ClientInstanceId = 2,
                    FaultSeed = 3
                }
            ]
        };
    }

    private static string FindRepoRoot()
    {
        return LauncherService.FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    }
}
