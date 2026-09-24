using System.Text;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class ContentFingerprintCanonicalizerTests
{
    private readonly List<string> _temporaryRoots = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _temporaryRoots.Count; i++)
        {
            Directory.Delete(_temporaryRoots[i], recursive: true);
        }

        _temporaryRoots.Clear();
    }

    [Test]
    public void SameGameplayContent_InDifferentRoots_ProducesIdenticalDigest()
    {
        string left = CreateModTree("mod.alpha", "alpha-map", assemblyStamp: 11);
        string right = CreateModTree("mod.alpha", "alpha-map", assemblyStamp: 11);

        ContentFingerprint first = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.alpha", left),
        });
        ContentFingerprint second = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.alpha", right),
        });

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.IsEmpty, Is.False);
    }

    [Test]
    public void ChangedGameplayAsset_ChangesDigest()
    {
        string rootA = CreateModTree("mod.beta", "map-a", assemblyStamp: 3);
        string rootB = CreateModTree("mod.beta", "map-b", assemblyStamp: 3);

        ContentFingerprint first = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.beta", rootA),
        });
        ContentFingerprint second = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.beta", rootB),
        });

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void ChangedMainAssembly_ChangesDigest_WhileBinNoiseIsIgnored()
    {
        string root = CreateModTree("mod.gamma", "stable-map", assemblyStamp: 1);
        ContentFingerprint baseline = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.gamma", root),
        });

        File.WriteAllBytes(Path.Combine(root, "bin", "noise.dll"), Encoding.UTF8.GetBytes("noise-a"));
        ContentFingerprint withNoise = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.gamma", root),
        });
        Assert.That(withNoise, Is.EqualTo(baseline));

        File.WriteAllBytes(Path.Combine(root, "bin", "noise.dll"), Encoding.UTF8.GetBytes("noise-b"));
        ContentFingerprint withDifferentNoise = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.gamma", root),
        });
        Assert.That(withDifferentNoise, Is.EqualTo(baseline));

        File.WriteAllBytes(Path.Combine(root, "bin", "net8.0", "Mod.dll"), new byte[] { 9, 0xAA, 0x55 });
        ContentFingerprint afterMainRewrite = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.gamma", root),
        });
        Assert.That(afterMainRewrite, Is.Not.EqualTo(baseline));
    }

    [Test]
    public void ChangedManifest_ChangesDigest()
    {
        string root = CreateModTree("mod.delta", "map", assemblyStamp: 9);
        ContentFingerprint before = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.delta", root),
        });

        File.WriteAllText(
            Path.Combine(root, "mod.json"),
            """
            {
              "name": "mod.delta",
              "version": "1.0.1",
              "main": "bin/net8.0/Mod.dll"
            }
            """);

        ContentFingerprint after = ContentFingerprintCanonicalizer.FromOrderedMods(new[]
        {
            new ResolvedModLoadEntry("mod.delta", root),
        });
        Assert.That(before, Is.Not.EqualTo(after));
    }

    private string CreateModTree(string modId, string mapPayload, byte assemblyStamp)
    {
        string root = Path.Combine(Path.GetTempPath(), "ludots-content-fp", Guid.NewGuid().ToString("N"));
        _temporaryRoots.Add(root);
        Directory.CreateDirectory(Path.Combine(root, "assets", "Maps"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "net8.0"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        File.WriteAllText(
            Path.Combine(root, "mod.json"),
            $$"""
            {
              "name": "{{modId}}",
              "version": "1.0.0",
              "main": "bin/net8.0/Mod.dll"
            }
            """);
        File.WriteAllText(Path.Combine(root, "assets", "game.json"), """{ "startupMapId": "duel" }""");
        File.WriteAllText(Path.Combine(root, "assets", "Maps", "duel.map"), mapPayload);
        File.WriteAllBytes(Path.Combine(root, "bin", "net8.0", "Mod.dll"), new[] { assemblyStamp, (byte)0xAA, (byte)0x55 });
        File.WriteAllBytes(Path.Combine(root, "obj", "scratch.tmp"), Encoding.UTF8.GetBytes("should-not-hash"));
        return root;
    }
}
