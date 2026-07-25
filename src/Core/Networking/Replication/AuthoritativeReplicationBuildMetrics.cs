namespace Ludots.Core.Networking.Replication
{
    public readonly struct AuthoritativeReplicationBuildMetrics
    {
        public AuthoritativeReplicationBuildMetrics(
            long projectionElapsedTimestampTicks,
            long channelBuildElapsedTimestampTicks)
        {
            ProjectionElapsedTimestampTicks = projectionElapsedTimestampTicks;
            ChannelBuildElapsedTimestampTicks = channelBuildElapsedTimestampTicks;
        }

        public long ProjectionElapsedTimestampTicks { get; }
        public long ChannelBuildElapsedTimestampTicks { get; }
    }
}
