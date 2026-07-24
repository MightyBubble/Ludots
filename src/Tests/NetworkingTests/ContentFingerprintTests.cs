using System.Text;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class ContentFingerprintTests
{
    [Test]
    public void FromCanonicalBytes_IsDeterministicSha256()
    {
        ReadOnlySpan<byte> canonical = "mods=a,b;map=duel"u8;
        ContentFingerprint first = ContentFingerprintBuilder.FromCanonicalBytes(canonical);
        ContentFingerprint second = ContentFingerprintBuilder.FromCanonicalBytes(canonical);

        Assert.That(first, Is.EqualTo(second));

        Span<byte> digest = stackalloc byte[32];
        Assert.That(System.Security.Cryptography.SHA256.HashData(canonical, digest), Is.EqualTo(32));
        Assert.That(first, Is.EqualTo(ContentFingerprint.FromBytes(digest)));
        Assert.That(first.ToHexString().Length, Is.EqualTo(ContentFingerprint.HexLength));
    }

    [Test]
    public void HexFormatAndParse_RoundTripAndRejectInvalid()
    {
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(Encoding.UTF8.GetBytes("canonical-v1"));
        string hex = fingerprint.ToHexString();

        Assert.That(ContentFingerprint.TryParseHex(hex, out ContentFingerprint parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(fingerprint));

        Assert.That(ContentFingerprint.TryParseHex(hex.ToUpperInvariant(), out parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(fingerprint));

        Assert.That(ContentFingerprint.TryParseHex(hex.AsSpan(0, hex.Length - 1), out _), Is.False);
        Assert.That(ContentFingerprint.TryParseHex("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", out _), Is.False);
        Assert.Throws<ArgumentException>(() => ContentFingerprint.FromBytes(stackalloc byte[16]));
    }

    [Test]
    public void DistinctCanonicalBytes_ProduceDistinctFingerprints()
    {
        ContentFingerprint a = ContentFingerprintBuilder.FromCanonicalBytes("content-a"u8);
        ContentFingerprint b = ContentFingerprintBuilder.FromCanonicalBytes("content-b"u8);
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void EmptyFingerprint_IsEmpty_AndDistinctFromCanonicalDigest()
    {
        Assert.That(ContentFingerprint.Empty.IsEmpty, Is.True);
        Assert.That(default(ContentFingerprint).IsEmpty, Is.True);

        ContentFingerprint digest = ContentFingerprintBuilder.FromCanonicalBytes("non-empty"u8);
        Assert.That(digest.IsEmpty, Is.False);
        Assert.That(digest, Is.Not.EqualTo(ContentFingerprint.Empty));
    }
}
