namespace Ludots.Client.WebGpu.Runtime;

/// <summary>
/// Shared byte-offset math for retained world-HUD <c>QueueWriteBuffer</c> subrange uploads.
/// Destination offset is instanceStart * instanceStrideBytes; source must be sliced the same way.
/// </summary>
public static class WorldHudUploadMath
{
    public static ulong DestinationByteOffset(int instanceStart, int instanceStrideBytes)
    {
        if (instanceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceStart));
        }

        if (instanceStrideBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceStrideBytes));
        }

        return checked((ulong)instanceStart * (ulong)instanceStrideBytes);
    }
}
