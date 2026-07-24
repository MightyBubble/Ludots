using Ludots.Core.Hosting;
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
        };
        var client = new NetworkHostBootstrapConfig
        {
            ProcessRole = "replicatedClient",
            Host = "127.0.0.1",
            Port = 27777,
            ConnectionKey = "rts-duel",
            ClientInstanceId = 1,
            CredentialPath = "runtime/client-1.session",
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
        };
        var clientWithoutCredentials = new NetworkHostBootstrapConfig
        {
            ProcessRole = "replicatedClient",
            Host = "127.0.0.1",
            Port = 27777,
            ConnectionKey = "rts-duel",
            ClientInstanceId = 1,
        };

        Assert.That(
            serverWithClientState.Validate,
            Throws.InvalidOperationException.With.Message.Contains("must not declare"));
        Assert.That(
            clientWithoutCredentials.Validate,
            Throws.InvalidOperationException.With.Message.Contains("credentialPath is required"));
    }
}
