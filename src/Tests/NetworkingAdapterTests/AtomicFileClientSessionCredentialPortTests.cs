using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class AtomicFileClientSessionCredentialPortTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AtomicFileClientSessionCredentialPortTests),
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Test]
    public void MissingParentDirectory_LoadsEmptyWithoutCreatingStorage()
    {
        string path = CredentialPath();
        var port = new AtomicFileClientSessionCredentialPort(path);

        ClientCredentialLoadStatus status = port.TryLoad(out ClientSessionCredentials credentials);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(ClientCredentialLoadStatus.Empty));
            Assert.That(credentials.SessionEpoch.IsEmpty, Is.True);
            Assert.That(credentials.ReconnectToken.IsEmpty, Is.True);
            Assert.That(Directory.Exists(_testDirectory), Is.False);
        });
    }

    [Test]
    public void Store_CreatesParentAndRoundTripsCredentials()
    {
        string path = CredentialPath();
        var port = new AtomicFileClientSessionCredentialPort(path);
        var expected = Credentials(epoch: 41, tokenLow: 101, tokenHigh: 202);

        bool stored = port.TryStore(expected);
        ClientCredentialLoadStatus status = port.TryLoad(out ClientSessionCredentials actual);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.True);
            Assert.That(status, Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(actual.SessionEpoch, Is.EqualTo(expected.SessionEpoch));
            Assert.That(actual.ReconnectToken, Is.EqualTo(expected.ReconnectToken));
            Assert.That(Directory.GetFiles(_testDirectory), Is.EqualTo(new[] { path }));
        });
    }

    [Test]
    public void Store_ReplacesExistingCredentialAsOneCommittedFile()
    {
        string path = CredentialPath();
        var port = new AtomicFileClientSessionCredentialPort(path);
        var first = Credentials(epoch: 1, tokenLow: 2, tokenHigh: 3);
        var second = Credentials(epoch: 4, tokenLow: 5, tokenHigh: 6);

        Assert.That(port.TryStore(first), Is.True);
        Assert.That(port.TryStore(second), Is.True);

        ClientCredentialLoadStatus status = port.TryLoad(out ClientSessionCredentials actual);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(actual.SessionEpoch, Is.EqualTo(second.SessionEpoch));
            Assert.That(actual.ReconnectToken, Is.EqualTo(second.ReconnectToken));
            Assert.That(Directory.GetFiles(_testDirectory), Is.EqualTo(new[] { path }));
        });
    }

    [Test]
    public void CorruptedCredential_LoadFailsAndLeavesEvidenceUntouched()
    {
        string path = CredentialPath();
        var port = new AtomicFileClientSessionCredentialPort(path);
        Assert.That(port.TryStore(Credentials(epoch: 7, tokenLow: 8, tokenHigh: 9)), Is.True);
        byte[] corrupted = File.ReadAllBytes(path);
        corrupted[12] ^= 0x5A;
        File.WriteAllBytes(path, corrupted);

        ClientCredentialLoadStatus status = port.TryLoad(out ClientSessionCredentials credentials);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(ClientCredentialLoadStatus.Failed));
            Assert.That(credentials.SessionEpoch.IsEmpty, Is.True);
            Assert.That(credentials.ReconnectToken.IsEmpty, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(corrupted));
        });
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(65)]
    public void WrongLengthCredential_LoadFailsAndLeavesEvidenceUntouched(int length)
    {
        string path = CredentialPath();
        Directory.CreateDirectory(_testDirectory);
        byte[] malformed = Enumerable.Repeat((byte)0xA5, length).ToArray();
        File.WriteAllBytes(path, malformed);
        var port = new AtomicFileClientSessionCredentialPort(path);

        ClientCredentialLoadStatus status = port.TryLoad(out _);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(ClientCredentialLoadStatus.Failed));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(malformed));
        });
    }

    [Test]
    public void EmptyCredential_StoreFailsWithoutCreatingParent()
    {
        var port = new AtomicFileClientSessionCredentialPort(CredentialPath());
        ClientSessionCredentials empty = default;

        bool stored = port.TryStore(empty);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.False);
            Assert.That(Directory.Exists(_testDirectory), Is.False);
        });
    }

    [Test]
    public void Clear_DeletesCredentialAndIsIdempotent()
    {
        string path = CredentialPath();
        var port = new AtomicFileClientSessionCredentialPort(path);
        Assert.That(port.TryStore(Credentials(epoch: 10, tokenLow: 11, tokenHigh: 12)), Is.True);

        bool firstClear = port.TryClear();
        bool secondClear = port.TryClear();

        Assert.Multiple(() =>
        {
            Assert.That(firstClear, Is.True);
            Assert.That(secondClear, Is.True);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(port.TryLoad(out _), Is.EqualTo(ClientCredentialLoadStatus.Empty));
        });
    }

    [Test]
    public void DirectoryAtCredentialPath_ReportsExplicitFailures()
    {
        string path = CredentialPath();
        Directory.CreateDirectory(path);
        var port = new AtomicFileClientSessionCredentialPort(path);

        Assert.Multiple(() =>
        {
            Assert.That(port.TryLoad(out _), Is.EqualTo(ClientCredentialLoadStatus.Failed));
            Assert.That(port.TryStore(Credentials(epoch: 13, tokenLow: 14, tokenHigh: 15)), Is.False);
            Assert.That(port.TryClear(), Is.False);
            Assert.That(Directory.Exists(path), Is.True);
        });
    }

    private string CredentialPath() => Path.Combine(_testDirectory, "session.credential");

    private static ClientSessionCredentials Credentials(ulong epoch, ulong tokenLow, ulong tokenHigh) =>
        new(new SessionEpoch(epoch), new ReconnectToken(tokenLow, tokenHigh));
}
