using Ludots.Core.Networking.Protocol;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class CommandFragmentAllocationTests
{
    [Test]
    public void SteadyStateEncodeDecodeReassembly_10000Operations_AllocatesZeroManagedBytes()
    {
        const int maxDatagram = 1200;
        const int maxCommandPayload = 8192;
        const int maxFragments = 16;
        var encoder = new CommandFragmentEncoder(maxDatagram, maxCommandPayload, maxFragments);
        var reassembler = new CommandFragmentReassembler(maxCommandPayload, maxFragments);

        byte[] commandPayload = new byte[encoder.MaxFragmentDataBytes * 2 + 11];
        for (int i = 0; i < commandPayload.Length; i++)
        {
            commandPayload[i] = (byte)(i & 0xFF);
        }

        Assert.That(
            encoder.TryGetFragmentCount(commandPayload.Length, out ushort fragmentCount),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        var fragmentBuffers = new byte[fragmentCount][];
        for (ushort i = 0; i < fragmentCount; i++)
        {
            Assert.That(
                CommandFragmentWireCodec.TryGetFragmentDataRange(
                    commandPayload.Length,
                    encoder.MaxFragmentDataBytes,
                    i,
                    fragmentCount,
                    out _,
                    out int length),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            fragmentBuffers[i] = new byte[CommandFragmentWireCodec.GetWirePayloadSize(length)];
        }

        byte[] framed = new byte[maxDatagram];

        for (int i = 0; i < 64; i++)
        {
            RunOnce(encoder, reassembler, commandPayload, fragmentCount, fragmentBuffers, framed);
        }

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            RunOnce(encoder, reassembler, commandPayload, fragmentCount, fragmentBuffers, framed);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(
            allocated,
            Is.EqualTo(0),
            $"Expected 0 B managed allocation over 10,000 command fragment encode/decode/reassembly ops; observed {allocated} B.");
    }

    private static void RunOnce(
        CommandFragmentEncoder encoder,
        CommandFragmentReassembler reassembler,
        byte[] commandPayload,
        ushort fragmentCount,
        byte[][] fragmentBuffers,
        byte[] framed)
    {
        reassembler.Reset();
        for (ushort i = 0; i < fragmentCount; i++)
        {
            AssertSuccess(encoder.TryEncodeFragment(
                sessionEpoch: 42,
                clientBatchSequence: 7,
                commandPayload,
                i,
                fragmentCount,
                fragmentBuffers[i],
                out int payloadBytes));

            AssertSuccess(CommandFragmentWireCodec.TryDecode(
                fragmentBuffers[i].AsSpan(0, payloadBytes),
                out _,
                out _));

            AssertSuccess(NetworkWireEnvelopeCodec.TryEncode(
                NetworkWireKind.CommandFragment,
                fragmentBuffers[i].AsSpan(0, payloadBytes),
                framed,
                out int framedBytes));
            AssertSuccess(NetworkWireEnvelopeCodec.TryDecode(framed.AsSpan(0, framedBytes), out _, out _));
        }

        for (int step = 0; step < fragmentCount; step++)
        {
            ushort index = (ushort)((fragmentCount - 1 - step + fragmentCount) % fragmentCount);
            CommandReassemblyStatus status = reassembler.TryAcceptWirePayload(fragmentBuffers[index]);
            bool last = step + 1 == fragmentCount;
            if (last)
            {
                if (status != CommandReassemblyStatus.Completed)
                {
                    throw new InvalidOperationException($"Expected Completed, got {status}.");
                }
            }
            else if (status != CommandReassemblyStatus.Incomplete)
            {
                throw new InvalidOperationException($"Expected Incomplete, got {status}.");
            }
        }

        ReadOnlySpan<byte> assembled = reassembler.AssembledPayload;
        if (!assembled.SequenceEqual(commandPayload))
        {
            throw new InvalidOperationException("Assembled command payload mismatch.");
        }
    }

    private static void AssertSuccess(NetworkWireCodecStatus status)
    {
        if (status != NetworkWireCodecStatus.Success)
        {
            throw new InvalidOperationException($"Expected Success, got {status}.");
        }
    }
}
