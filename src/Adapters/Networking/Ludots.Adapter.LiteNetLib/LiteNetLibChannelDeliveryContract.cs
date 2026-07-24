using System;
using global::LiteNetLib;
using Ludots.Core.Networking.Transport;

namespace Ludots.Adapter.LiteNetLib;

internal readonly struct LiteNetLibChannelDeliveryContract
{
    private readonly byte _controlChannel;
    private readonly byte _commandChannel;
    private readonly byte _stateChannel;

    public LiteNetLibChannelDeliveryContract(
        int channelCount,
        ChannelId controlChannel,
        ChannelId commandChannel,
        ChannelId stateChannel)
    {
        if ((uint)(channelCount - 1) >= 64u) throw new ArgumentOutOfRangeException(nameof(channelCount));
        if (controlChannel == commandChannel || controlChannel == stateChannel || commandChannel == stateChannel)
        {
            throw new ArgumentException("Control, command, and state channels must be distinct.");
        }

        ValidateWithinCount(controlChannel, channelCount, nameof(controlChannel));
        ValidateWithinCount(commandChannel, channelCount, nameof(commandChannel));
        ValidateWithinCount(stateChannel, channelCount, nameof(stateChannel));
        _controlChannel = controlChannel.Value;
        _commandChannel = commandChannel.Value;
        _stateChannel = stateChannel.Value;
    }

    public DeliveryMethod GetExpected(byte channel)
    {
        if (channel == _stateChannel)
        {
            return DeliveryMethod.Sequenced;
        }

        if (channel == _controlChannel || channel == _commandChannel)
        {
            return DeliveryMethod.ReliableOrdered;
        }

        throw new InvalidOperationException($"Channel {channel} has no configured networking delivery contract.");
    }

    public void ValidateReceived(byte channel, DeliveryMethod actual)
    {
        DeliveryMethod expected = GetExpected(channel);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Unexpected delivery method {actual} on channel {channel}; {expected} is required.");
        }
    }

    private static void ValidateWithinCount(ChannelId channel, int channelCount, string parameterName)
    {
        if (channel.Value >= channelCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, channel.Value, $"Channel must be below configured count {channelCount}.");
        }
    }
}
