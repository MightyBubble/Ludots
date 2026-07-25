namespace Ludots.Core.Networking.Runtime
{
    public readonly struct AuthoritativeNetworkPublishMetrics
    {
        public AuthoritativeNetworkPublishMetrics(
            int publishedSeatCount,
            long totalElapsedTimestampTicks,
            long interestPrepareElapsedTimestampTicks,
            long interestValidationElapsedTimestampTicks,
            long knowledgeCommitElapsedTimestampTicks,
            long projectionElapsedTimestampTicks,
            long channelBuildElapsedTimestampTicks,
            long packetEncodeElapsedTimestampTicks,
            long transportSendElapsedTimestampTicks,
            long acknowledgementAndFlushElapsedTimestampTicks)
        {
            PublishedSeatCount = publishedSeatCount;
            TotalElapsedTimestampTicks = totalElapsedTimestampTicks;
            InterestPrepareElapsedTimestampTicks = interestPrepareElapsedTimestampTicks;
            InterestValidationElapsedTimestampTicks = interestValidationElapsedTimestampTicks;
            KnowledgeCommitElapsedTimestampTicks = knowledgeCommitElapsedTimestampTicks;
            ProjectionElapsedTimestampTicks = projectionElapsedTimestampTicks;
            ChannelBuildElapsedTimestampTicks = channelBuildElapsedTimestampTicks;
            PacketEncodeElapsedTimestampTicks = packetEncodeElapsedTimestampTicks;
            TransportSendElapsedTimestampTicks = transportSendElapsedTimestampTicks;
            AcknowledgementAndFlushElapsedTimestampTicks = acknowledgementAndFlushElapsedTimestampTicks;
        }

        public int PublishedSeatCount { get; }
        public long TotalElapsedTimestampTicks { get; }
        public long InterestPrepareElapsedTimestampTicks { get; }
        public long InterestValidationElapsedTimestampTicks { get; }
        public long KnowledgeCommitElapsedTimestampTicks { get; }
        public long ProjectionElapsedTimestampTicks { get; }
        public long ChannelBuildElapsedTimestampTicks { get; }
        public long PacketEncodeElapsedTimestampTicks { get; }
        public long TransportSendElapsedTimestampTicks { get; }
        public long AcknowledgementAndFlushElapsedTimestampTicks { get; }

        public long AccountedElapsedTimestampTicks =>
            InterestPrepareElapsedTimestampTicks +
            InterestValidationElapsedTimestampTicks +
            KnowledgeCommitElapsedTimestampTicks +
            ProjectionElapsedTimestampTicks +
            ChannelBuildElapsedTimestampTicks +
            PacketEncodeElapsedTimestampTicks +
            TransportSendElapsedTimestampTicks +
            AcknowledgementAndFlushElapsedTimestampTicks;

        public long UnattributedElapsedTimestampTicks =>
            System.Math.Max(0, TotalElapsedTimestampTicks - AccountedElapsedTimestampTicks);
    }
}
