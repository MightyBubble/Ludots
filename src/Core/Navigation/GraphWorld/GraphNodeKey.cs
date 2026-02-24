using System;

namespace Ludots.Core.Navigation.GraphWorld
{
    public readonly struct GraphNodeKey : IEquatable<GraphNodeKey>
    {
        public readonly long ChunkKey;
        public readonly ushort LocalNodeId;

        public GraphNodeKey(long chunkKey, ushort localNodeId)
        {
            ChunkKey = chunkKey;
            LocalNodeId = localNodeId;
        }

        public bool Equals(GraphNodeKey other)
        {
            return ChunkKey == other.ChunkKey && LocalNodeId == other.LocalNodeId;
        }

        public override bool Equals(object obj)
        {
            return obj is GraphNodeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkKey, LocalNodeId);
        }

        public static bool operator ==(GraphNodeKey left, GraphNodeKey right) => left.Equals(right);
        public static bool operator !=(GraphNodeKey left, GraphNodeKey right) => !left.Equals(right);
    }
}

