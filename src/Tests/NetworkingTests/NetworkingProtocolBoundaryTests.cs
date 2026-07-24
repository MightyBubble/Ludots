using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkingProtocolBoundaryTests
{
    [Test]
    public void NetworkingCore_DoesNotReference_Sockets_OrPresentationStableId()
    {
        string repoRoot = FindRepoRoot();
        string networkingRoot = Path.Combine(repoRoot, "src", "Core", "Networking");
        Assert.That(Directory.Exists(networkingRoot), Is.True);

        string[] forbidden =
        {
            "System.Net.Sockets",
            "PresentationStableId",
            "Raylib",
            "Browser",
            "Microsoft.AspNetCore",
            "System.Net.Http",
        };
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(networkingRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    // Allow the word Socket only outside Networking Protocol/Transport contracts if ever needed;
                    // currently any hit under Networking is a boundary violation for this epic.
                    offenders.Add($"{Path.GetRelativePath(repoRoot, file)}:{token}");
                }
            }
        }

        Assert.That(offenders, Is.Empty, "Networking Core must stay platform-neutral:\n" + string.Join("\n", offenders));
    }

    [Test]
    public void ProtocolFolder_Exists_AndContainsWireCodecs()
    {
        string repoRoot = FindRepoRoot();
        string protocolRoot = Path.Combine(repoRoot, "src", "Core", "Networking", "Protocol");
        Assert.That(Directory.Exists(protocolRoot), Is.True);

        string[] required =
        {
            "NetworkWireEnvelopeCodec.cs",
            "HandshakeWireCodec.cs",
            "CommandBatchWireCodec.cs",
            "CommandAdmissionWireCodec.cs",
            "ReplicationPacketWireCodec.cs",
            "SnapshotControlWireCodec.cs",
            "SnapshotFragmentWireCodec.cs",
            "SnapshotFragmentReassembler.cs",
        };

        foreach (string name in required)
        {
            Assert.That(File.Exists(Path.Combine(protocolRoot, name)), Is.True, $"Missing {name}");
        }
    }

    [Test]
    public void ProtocolFolder_DoesNotReference_SocketsRaylibBrowserOrPresentationStableId()
    {
        string repoRoot = FindRepoRoot();
        string protocolRoot = Path.Combine(repoRoot, "src", "Core", "Networking", "Protocol");
        Assert.That(Directory.Exists(protocolRoot), Is.True);

        string[] forbidden =
        {
            "System.Net.Sockets",
            "Socket",
            "PresentationStableId",
            "Raylib",
            "Browser",
            "Microsoft.AspNetCore",
        };
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(protocolRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(repoRoot, file)}:{token}");
                }
            }
        }

        Assert.That(offenders, Is.Empty, "Protocol must stay free of platform hosts:\n" + string.Join("\n", offenders));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
