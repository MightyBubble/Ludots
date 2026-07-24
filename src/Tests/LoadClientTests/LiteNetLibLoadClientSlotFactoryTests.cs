using Ludots.Adapter.LiteNetLib;
using Ludots.App.LoadClients;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.LoadClients;

[TestFixture]
public sealed class LiteNetLibLoadClientSlotFactoryTests
{
    private string _credentialDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _credentialDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(LiteNetLibLoadClientSlotFactoryTests),
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_credentialDirectory))
        {
            Directory.Delete(_credentialDirectory, recursive: true);
        }
    }

    [Test]
    public void Create_MultipleClients_UseDistinctCredentialFilesAndDistinctBoundPorts()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson(clientCount: 3));
        var factory = new LiteNetLibLoadClientSlotFactory();
        LoadClientSlot[] slots = new LoadClientSlot[3];
        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = factory.Create(i, config, _credentialDirectory);
            }

            Assert.Multiple(() =>
            {
                Assert.That(slots[0].BoundPort, Is.Not.EqualTo(slots[1].BoundPort));
                Assert.That(slots[0].BoundPort, Is.Not.EqualTo(slots[2].BoundPort));
                Assert.That(slots[1].BoundPort, Is.Not.EqualTo(slots[2].BoundPort));
                Assert.That(slots[0].CredentialPath, Is.Not.EqualTo(slots[1].CredentialPath));
                Assert.That(slots[0].Clock.SimulationTickRateHz, Is.EqualTo(30));
                Assert.That(File.Exists(slots[0].CredentialPath), Is.False); // empty until store
            });

            // Prove credential ports are distinct atomic-file adapters by storing unique epochs.
            var first = new AtomicFileClientSessionCredentialPort(slots[0].CredentialPath);
            var second = new AtomicFileClientSessionCredentialPort(slots[1].CredentialPath);
            Assert.That(
                first.TryStore(new ClientSessionCredentials(new SessionEpoch(11), new ReconnectToken(1, 2))),
                Is.True);
            Assert.That(
                second.TryStore(new ClientSessionCredentials(new SessionEpoch(22), new ReconnectToken(3, 4))),
                Is.True);
            Assert.That(File.Exists(slots[0].CredentialPath), Is.True);
            Assert.That(File.Exists(slots[1].CredentialPath), Is.True);
            Assert.That(
                File.ReadAllBytes(slots[0].CredentialPath),
                Is.Not.EqualTo(File.ReadAllBytes(slots[1].CredentialPath)));
        }
        finally
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i]?.Dispose();
            }
        }
    }
}
