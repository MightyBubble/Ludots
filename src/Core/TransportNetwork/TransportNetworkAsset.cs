using System;
using System.Collections.Generic;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkAsset
    {
        public string Id { get; set; } = string.Empty;
        public int SampleStepCm { get; set; } = 600;
        public float DefaultVisualWidthMeters { get; set; } = 1.65f;
        public List<TransportNetworkNode> Nodes { get; set; } = new();
        public List<TransportNetworkSegment> Segments { get; set; } = new();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || !string.Equals(Id.Trim(), Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("TransportNetworkAsset.id must be a non-empty canonical string.");
            }

            if (SampleStepCm <= 0)
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{Id}' requires sampleStepCm > 0.");
            }

            if (DefaultVisualWidthMeters <= 0f || float.IsNaN(DefaultVisualWidthMeters))
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{Id}' requires defaultVisualWidthMeters > 0.");
            }

            if (Nodes == null)
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{Id}' requires nodes array.");
            }

            if (Segments == null || Segments.Count == 0)
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{Id}' requires at least one segment.");
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Nodes.Count; i++)
            {
                TransportNetworkNode node = Nodes[i] ?? throw new InvalidOperationException($"TransportNetworkAsset '{Id}' nodes[{i}] is null.");
                node.Validate(Id, i);
                if (!nodeIds.Add(node.Id))
                {
                    throw new InvalidOperationException($"TransportNetworkAsset '{Id}' contains duplicate node id '{node.Id}'.");
                }
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                TransportNetworkSegment segment = Segments[i] ?? throw new InvalidOperationException($"TransportNetworkAsset '{Id}' segments[{i}] is null.");
                segment.Validate(Id, i, nodeIds);
            }
        }
    }

    public sealed class TransportNetworkNode
    {
        public string Id { get; set; } = string.Empty;
        public int Xcm { get; set; }
        public int Ycm { get; set; }
        public TransportNetworkNodeKind Kind { get; set; } = TransportNetworkNodeKind.Normal;
        public List<string> Tags { get; set; } = new();

        public void Validate(string assetId, int index)
        {
            string path = $"TransportNetworkAsset '{assetId}' nodes[{index}]";
            if (string.IsNullOrWhiteSpace(Id) || !string.Equals(Id.Trim(), Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.id must be a non-empty canonical string.");
            }

            TransportNetworkValidation.RequireCanonicalTags(Tags, $"{path}.tags");
        }
    }

    public sealed class TransportNetworkSegment
    {
        public string Id { get; set; } = string.Empty;
        public List<TransportNetworkPoint> Points { get; set; } = new();
        public int SampleStepCm { get; set; }
        public TransportNetworkDirection Direction { get; set; } = TransportNetworkDirection.Bidirectional;
        public TransportNetworkFlowDirection FlowDirection { get; set; } = TransportNetworkFlowDirection.None;
        public string AreaId { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public int DepthCm { get; set; }
        public int WidthCm { get; set; }
        public int LaneCount { get; set; }
        public float VisualWidthMeters { get; set; }

        public void Validate(string assetId, int index, HashSet<string> nodeIds)
        {
            string path = $"TransportNetworkAsset '{assetId}' segments[{index}]";
            if (string.IsNullOrWhiteSpace(Id) || !string.Equals(Id.Trim(), Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.id must be a non-empty canonical string.");
            }

            if (Points == null || Points.Count < 2)
            {
                throw new InvalidOperationException($"{path}.points must contain at least two points.");
            }

            if (SampleStepCm < 0)
            {
                throw new InvalidOperationException($"{path}.sampleStepCm must be >= 0.");
            }

            if (DepthCm < 0)
            {
                throw new InvalidOperationException($"{path}.depthCm must be >= 0.");
            }

            if (WidthCm < 0)
            {
                throw new InvalidOperationException($"{path}.widthCm must be >= 0.");
            }

            if (LaneCount < 0)
            {
                throw new InvalidOperationException($"{path}.laneCount must be >= 0.");
            }

            if (VisualWidthMeters < 0f || float.IsNaN(VisualWidthMeters))
            {
                throw new InvalidOperationException($"{path}.visualWidthMeters must be >= 0.");
            }

            if (!string.IsNullOrWhiteSpace(AreaId) && !string.Equals(AreaId.Trim(), AreaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.areaId must not contain leading or trailing whitespace.");
            }

            TransportNetworkValidation.RequireCanonicalTags(Tags, $"{path}.tags");
            for (int i = 0; i < Points.Count; i++)
            {
                TransportNetworkPoint point = Points[i] ?? throw new InvalidOperationException($"{path}.points[{i}] is null.");
                point.Validate($"{path}.points[{i}]", nodeIds);
            }
        }
    }

    public sealed class TransportNetworkPoint
    {
        public string NodeId { get; set; } = string.Empty;
        public int Xcm { get; set; }
        public int Ycm { get; set; }

        public static TransportNetworkPoint At(int xcm, int ycm)
        {
            return new TransportNetworkPoint { Xcm = xcm, Ycm = ycm };
        }

        public static TransportNetworkPoint FromNode(string nodeId)
        {
            return new TransportNetworkPoint { NodeId = nodeId };
        }

        public void Validate(string path, HashSet<string> nodeIds)
        {
            if (string.IsNullOrWhiteSpace(NodeId))
            {
                return;
            }

            if (!string.Equals(NodeId.Trim(), NodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.nodeId must not contain leading or trailing whitespace.");
            }

            if (!nodeIds.Contains(NodeId))
            {
                throw new InvalidOperationException($"{path}.nodeId references unknown node '{NodeId}'.");
            }
        }
    }

    public enum TransportNetworkNodeKind : byte
    {
        Normal = 0,
        Port = 1,
        Embark = 2,
        Bridge = 3,
        Ford = 4
    }

    public enum TransportNetworkDirection : byte
    {
        Bidirectional = 0,
        ForwardOnly = 1,
        ReverseOnly = 2
    }

    public enum TransportNetworkFlowDirection : byte
    {
        None = 0,
        Forward = 1,
        Reverse = 2
    }

    internal static class TransportNetworkValidation
    {
        public static void RequireCanonicalTags(List<string> tags, string path)
        {
            if (tags == null)
            {
                throw new InvalidOperationException($"{path} must be an explicit array.");
            }

            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag) || !string.Equals(tag.Trim(), tag, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}[{i}] must be a non-empty canonical string.");
                }
            }
        }
    }
}
