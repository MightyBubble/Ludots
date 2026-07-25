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
}
