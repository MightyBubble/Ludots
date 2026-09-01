using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkHostBootstrapConfigTests
{
    [Test]
    public void ServerAndClientProfiles_ResolveExplicitRoles()
    {
        var server = new NetworkHostBootstrapConfig
        {
            ProcessRole = "authoritativeServer",
            Port = 27777,
            ConnectionKey = "rts-duel",
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 101,
        };
        var client = new NetworkHostBootstrapConfig
        {
            ProcessRole = "replicatedClient",
            Host = "127.0.0.1",
            Port = 27777,
            ConnectionKey = "rts-duel",
            ClientInstanceId = 1,
            CredentialPath = "runtime/client-1.session",
            FaultProfile = NetworkHostBootstrapConfig.UnstableFaultProfile,
            FaultSeed = 202,
        };

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(server.Validate);
            Assert.DoesNotThrow(client.Validate);
            Assert.That(server.ResolveRole(), Is.EqualTo(NetworkProcessRole.AuthoritativeServer));
            Assert.That(client.ResolveRole(), Is.EqualTo(NetworkProcessRole.ReplicatedClient));
        });
    }

    [Test]
    public void RoleSpecificFields_AreRequiredAndCannotLeakAcrossProfiles()
    {
        var serverWithClientState = new NetworkHostBootstrapConfig
        {
            ProcessRole = "authoritativeServer",
            Port = 27777,
            ConnectionKey = "rts-duel",
            ClientInstanceId = 1,
            CredentialPath = "runtime/client-1.session",
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 101,
        };
        var clientWithoutCredentials = new NetworkHostBootstrapConfig
        {
            ProcessRole = "replicatedClient",
            Host = "127.0.0.1",
            Port = 27777,
            ConnectionKey = "rts-duel",
            ClientInstanceId = 1,
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 202,
        };

        Assert.That(
            serverWithClientState.Validate,
            Throws.InvalidOperationException.With.Message.Contains("must not declare"));
        Assert.That(
            clientWithoutCredentials.Validate,
            Throws.InvalidOperationException.With.Message.Contains("credentialPath is required"));
    }

    [Test]
    public void FaultProfileAndSeed_AreExplicitAndResolveVersionedNetworkData()
    {
        var host = new NetworkHostBootstrapConfig
        {
            ProcessRole = "authoritativeServer",
            Port = 27777,
            ConnectionKey = "rts-duel",
            FaultProfile = NetworkHostBootstrapConfig.UnstableFaultProfile,
            FaultSeed = 709,
        };
        var config = new NetworkRuntimeConfig
        {
            NormalConnection = new NetworkFaultProfileConfig(),
            UnstableConnection = new NetworkFaultProfileConfig
            {
                RoundTripLatencyMs = 180,
                JitterMs = 30,
                PacketLossPermille = 50,
                ReorderPermille = 20,
            },
        };

        Assert.That(host.ResolveFaultProfile(config), Is.SameAs(config.UnstableConnection));

        host.FaultProfile = string.Empty;
        Assert.That(host.Validate, Throws.InvalidOperationException.With.Message.Contains("faultProfile"));
        host.FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile;
        host.FaultSeed = 0;
        Assert.That(host.Validate, Throws.InvalidOperationException.With.Message.Contains("faultSeed"));
    }

    [Test]
    public void ReadinessArtifactPath_IsOptionalAndMustResolveToContainedJsonFile()
    {
        string runtimeBaseDirectory = Path.Combine(Path.GetTempPath(), $"ludots-readiness-{Guid.NewGuid():N}");
        var host = new NetworkHostBootstrapConfig
        {
            ProcessRole = "authoritativeServer",
        };

        Assert.That(host.ResolveReadinessArtifactPath(runtimeBaseDirectory), Is.Null);

        host.ReadinessArtifactPath = Path.Combine("network-readiness", "client-a.json");
        Assert.That(
            host.ResolveReadinessArtifactPath(runtimeBaseDirectory),
            Is.EqualTo(Path.Combine(runtimeBaseDirectory, "network-readiness", "client-a.json")));

        host.ReadinessArtifactPath = Path.Combine("..", "escaped.json");
        Assert.That(
            () => host.ResolveReadinessArtifactPath(runtimeBaseDirectory),
            Throws.InvalidOperationException.With.Message.Contains("inside runtimeBaseDirectory"));

        host.ReadinessArtifactPath = "client-a.txt";
        Assert.That(
            host.Validate,
            Throws.InvalidOperationException.With.Message.Contains("named .json"));

        host.ReadinessArtifactPath = Path.Combine(runtimeBaseDirectory, "absolute.json");
        Assert.That(
            host.Validate,
            Throws.InvalidOperationException.With.Message.Contains("relative path"));
    }

    [Test]
    public void BootstrapArtifactBase_UsesResolvedBootstrapDirectory_ForDefaultAndExternalRoles()
    {
        string appDirectory = Path.Combine(Path.GetTempPath(), $"ludots-app-{Guid.NewGuid():N}");
        string roleDirectory = Path.Combine(Path.GetTempPath(), $"ludots-role-{Guid.NewGuid():N}");
        string externalBootstrap = Path.Combine(roleDirectory, "launcher.runtime.json");
        var host = new NetworkHostBootstrapConfig
        {
            ReadinessArtifactPath = "client-a.readiness.json",
        };

        string defaultBootstrap = GameBootstrapper.ResolveBootstrapPath(
            appDirectory,
            "launcher.runtime.json");
        string resolvedExternalBootstrap = GameBootstrapper.ResolveBootstrapPath(
            appDirectory,
            externalBootstrap);
        string defaultBase = Path.GetDirectoryName(defaultBootstrap)!;
        string externalBase = Path.GetDirectoryName(resolvedExternalBootstrap)!;

        Assert.Multiple(() =>
        {
            Assert.That(defaultBase, Is.EqualTo(Path.GetFullPath(appDirectory)));
            Assert.That(externalBase, Is.EqualTo(Path.GetFullPath(roleDirectory)));
            Assert.That(
                host.ResolveReadinessArtifactPath(externalBase),
                Is.EqualTo(Path.Combine(roleDirectory, "client-a.readiness.json")));
        });
    }

    [Test]
    public void BootstrapJson_RoundTripsEmbeddedNetworkHostReadinessPath_ToRoleDirectory()
    {
        string appDirectory = Path.Combine(Path.GetTempPath(), $"ludots-app-{Guid.NewGuid():N}");
        string roleDirectory = Path.Combine(Path.GetTempPath(), $"ludots-role-{Guid.NewGuid():N}");
        string bootstrapPath = Path.Combine(roleDirectory, "launcher.runtime.json");
        Directory.CreateDirectory(roleDirectory);
        try
        {
            var bootstrap = new AppBootstrapConfig
            {
                NetworkHost = new NetworkHostBootstrapConfig
                {
                    ReadinessArtifactPath = "client-a.readiness.json",
                },
            };
            JsonSerializerOptions options = StrictJsonOptions.CreateExact();
            File.WriteAllText(bootstrapPath, JsonSerializer.Serialize(bootstrap, options));

            string resolvedBootstrap = GameBootstrapper.ResolveBootstrapPath(appDirectory, bootstrapPath);
            AppBootstrapConfig loaded = JsonSerializer.Deserialize<AppBootstrapConfig>(
                File.ReadAllText(resolvedBootstrap),
                options)!;
            string hostArtifactBase = Path.GetDirectoryName(resolvedBootstrap)!;

            Assert.Multiple(() =>
            {
                Assert.That(loaded.NetworkHost, Is.Not.Null);
                Assert.That(
                    loaded.NetworkHost!.ResolveReadinessArtifactPath(hostArtifactBase),
                    Is.EqualTo(Path.Combine(roleDirectory, "client-a.readiness.json")));
            });
        }
        finally
        {
            Directory.Delete(roleDirectory, recursive: true);
        }
    }
}
