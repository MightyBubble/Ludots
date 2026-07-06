using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Registry;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class SaveContextHeaderTests
{
    [Test]
    public void CapturedSaveContextHeaderValidatesAgainstSameEngine()
    {
        using GameEngine engine = CreateInitializedEngine();

        SaveContextHeader header = SaveContextFactory.Capture(engine);

        Assert.DoesNotThrow(() => SaveContextValidator.Validate(header, engine));
        Assert.That(header.SchemaVersion, Is.EqualTo(SaveContextHeader.CurrentSchemaVersion));
        Assert.That(header.ModSetHash, Is.Not.Empty);
        Assert.That(header.RegistryFingerprint, Is.Not.Empty);
        Assert.That(header.MapId, Is.EqualTo(engine.CurrentMapSession.MapId.Value));
        Assert.That(header.Tick, Is.EqualTo(engine.GameSession.CurrentTick));
    }

    [Test]
    public void SchemaVersionMismatchFailsFastWithExpectedAndActualValues()
    {
        using GameEngine engine = CreateInitializedEngine();
        SaveContextHeader header = SaveContextFactory.Capture(engine) with
        {
            SchemaVersion = SaveContextHeader.CurrentSchemaVersion + 1
        };

        var error = Assert.Throws<SaveContextException>(() => SaveContextValidator.Validate(header, engine));

        Assert.That(error!.Message, Does.Contain("schemaVersion"));
        Assert.That(error.Message, Does.Contain($"expected {SaveContextHeader.CurrentSchemaVersion}"));
        Assert.That(error.Message, Does.Contain($"actual {SaveContextHeader.CurrentSchemaVersion + 1}"));
    }

    [Test]
    public void ModSetHashMismatchFailsFastWithCorrespondingModAndMapHint()
    {
        using GameEngine engine = CreateInitializedEngine();
        SaveContextHeader header = SaveContextFactory.Capture(engine) with
        {
            ModSetHash = "tampered-mod-set"
        };

        var error = Assert.Throws<SaveContextException>(() => SaveContextValidator.Validate(header, engine));

        Assert.That(error!.Message, Does.Contain("modSetHash"));
        Assert.That(error.Message, Does.Contain("expected"));
        Assert.That(error.Message, Does.Contain("actual tampered-mod-set"));
        Assert.That(error.Message, Does.Contain("corresponding mod set and map"));
    }

    [Test]
    public void RegistryFingerprintMismatchFailsFast()
    {
        using GameEngine engine = CreateInitializedEngine();
        SaveContextHeader header = SaveContextFactory.Capture(engine) with
        {
            RegistryFingerprint = "tampered-registry"
        };

        var error = Assert.Throws<SaveContextException>(() => SaveContextValidator.Validate(header, engine));

        Assert.That(error!.Message, Does.Contain("registryFingerprint"));
        Assert.That(error.Message, Does.Contain("expected"));
        Assert.That(error.Message, Does.Contain("actual tampered-registry"));
    }

    [Test]
    public void RegistryFingerprintIsStableAcrossDictionaryAndMappingEnumerationOrder()
    {
        var first = new Dictionary<string, IReadOnlyList<RegistryMapping>>
        {
            ["tag"] = new[]
            {
                new RegistryMapping("State.Ready", 2),
                new RegistryMapping("State.Busy", 1),
            },
            ["attribute"] = new[]
            {
                new RegistryMapping("Health", 0),
                new RegistryMapping("MoveSpeed", 1),
            },
        };

        var second = new Dictionary<string, IReadOnlyList<RegistryMapping>>
        {
            ["attribute"] = new[]
            {
                new RegistryMapping("MoveSpeed", 1),
                new RegistryMapping("Health", 0),
            },
            ["tag"] = new[]
            {
                new RegistryMapping("State.Busy", 1),
                new RegistryMapping("State.Ready", 2),
            },
        };

        string firstFingerprint = SaveContextHashes.ComputeRegistryFingerprint(first);
        string secondFingerprint = SaveContextHashes.ComputeRegistryFingerprint(second);

        Assert.That(secondFingerprint, Is.EqualTo(firstFingerprint));
    }

    private static GameEngine CreateInitializedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
            Path.Combine(repoRoot, "assets"));
        engine.LoadStartupMap();
        return engine;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
