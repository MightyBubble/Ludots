using System.Buffers.Binary;
using System.Security.Cryptography;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Persists reconnect credentials as a checksummed file committed by an atomic same-directory move.
/// </summary>
public sealed class AtomicFileClientSessionCredentialPort : IClientSessionCredentialPort
{
    private const int MagicLength = 8;
    private const int PayloadLength = 32;
    private const int DigestLength = 32;
    private const int FileLength = PayloadLength + DigestLength;

    private static ReadOnlySpan<byte> Magic => "LUDCRD01"u8;

    private readonly string _credentialPath;
    private readonly string _parentDirectory;

    public AtomicFileClientSessionCredentialPort(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);

        _credentialPath = Path.GetFullPath(credentialPath);
        _parentDirectory = Path.GetDirectoryName(_credentialPath)
            ?? throw new ArgumentException("Credential path must have a parent directory.", nameof(credentialPath));
    }

    public ClientCredentialLoadStatus TryLoad(out ClientSessionCredentials credentials)
    {
        credentials = default;
        Span<byte> file = stackalloc byte[FileLength];

        try
        {
            using var stream = new FileStream(
                _credentialPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                FileLength,
                FileOptions.SequentialScan);

            if (stream.Length != FileLength)
            {
                return ClientCredentialLoadStatus.Failed;
            }

            stream.ReadExactly(file);
        }
        catch (FileNotFoundException)
        {
            return ClientCredentialLoadStatus.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return ClientCredentialLoadStatus.Empty;
        }
        catch (IOException)
        {
            return ClientCredentialLoadStatus.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            return ClientCredentialLoadStatus.Failed;
        }

        ReadOnlySpan<byte> payload = file[..PayloadLength];
        ReadOnlySpan<byte> storedDigest = file[PayloadLength..];
        Span<byte> computedDigest = stackalloc byte[DigestLength];
        SHA256.HashData(payload, computedDigest);

        if (!payload[..MagicLength].SequenceEqual(Magic) ||
            !CryptographicOperations.FixedTimeEquals(storedDigest, computedDigest))
        {
            return ClientCredentialLoadStatus.Failed;
        }

        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]);
        ulong tokenLow = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..24]);
        ulong tokenHigh = BinaryPrimitives.ReadUInt64LittleEndian(payload[24..32]);
        var sessionEpoch = new SessionEpoch(epoch);
        var reconnectToken = new ReconnectToken(tokenLow, tokenHigh);
        if (sessionEpoch.IsEmpty || reconnectToken.IsEmpty)
        {
            return ClientCredentialLoadStatus.Failed;
        }

        credentials = new ClientSessionCredentials(sessionEpoch, reconnectToken);
        return ClientCredentialLoadStatus.Loaded;
    }

    public bool TryStore(in ClientSessionCredentials credentials)
    {
        if (credentials.SessionEpoch.IsEmpty || credentials.ReconnectToken.IsEmpty)
        {
            return false;
        }

        string temporaryPath = Path.Combine(
            _parentDirectory,
            $".{Path.GetFileName(_credentialPath)}.{Guid.NewGuid():N}.tmp");
        bool committed = false;
        bool succeeded = false;

        try
        {
            Directory.CreateDirectory(_parentDirectory);

            Span<byte> file = stackalloc byte[FileLength];
            Magic.CopyTo(file);
            BinaryPrimitives.WriteUInt64LittleEndian(file[8..16], credentials.SessionEpoch.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(file[16..24], credentials.ReconnectToken.Low);
            BinaryPrimitives.WriteUInt64LittleEndian(file[24..32], credentials.ReconnectToken.High);
            SHA256.HashData(file[..PayloadLength], file[PayloadLength..]);

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileLength,
                FileOptions.WriteThrough))
            {
                stream.Write(file);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _credentialPath, overwrite: true);
            committed = true;
            succeeded = true;
        }
        catch (IOException)
        {
            succeeded = false;
        }
        catch (UnauthorizedAccessException)
        {
            succeeded = false;
        }

        if (!committed)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                succeeded = false;
            }
            catch (UnauthorizedAccessException)
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    public bool TryClear()
    {
        try
        {
            File.Delete(_credentialPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
