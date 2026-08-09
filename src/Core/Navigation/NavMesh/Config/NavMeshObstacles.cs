using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public enum NavObstacleKind : byte
    {
        Polygon = 0,
        Circle = 1,
        Segment = 2
    }

    public sealed class NavObstacleSet : INavObstacleSource
    {
        public int Version { get; set; } = 1;
        public List<NavObstacle> Obstacles { get; set; } = new List<NavObstacle>();

        public int ObstacleCount => Obstacles?.Count ?? 0;

        public void ValidateForBake(IReadOnlyList<NavLayerConfig> layers, string pathPrefix)
        {
            if (Obstacles == null)
            {
                throw new InvalidOperationException($"{pathPrefix} is required.");
            }

            if (layers == null)
            {
                throw new InvalidOperationException("NavBakeContext.config.layers is required for obstacle validation.");
            }

            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i]
                    ?? throw new InvalidOperationException($"NavBakeContext.config.layers[{i}] is null.");
                if (string.IsNullOrWhiteSpace(layer.Id) ||
                    !string.Equals(layer.Id.Trim(), layer.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavBakeContext.config.layers[{i}].id must be a non-empty trimmed string.");
                }

                if (!layerIds.Add(layer.Id))
                {
                    throw new InvalidOperationException($"NavBakeContext.config.layers contains duplicate id '{layer.Id}'.");
                }
            }

            for (int i = 0; i < Obstacles.Count; i++)
            {
                NavObstacle obstacle = Obstacles[i]
                    ?? throw new InvalidOperationException($"{pathPrefix}[{i}] is null.");
                string path = $"{pathPrefix}[{i}]";
                if (string.IsNullOrWhiteSpace(obstacle.Id) ||
                    !string.Equals(obstacle.Id.Trim(), obstacle.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.id must be a non-empty trimmed string.");
                }

                if (string.IsNullOrWhiteSpace(obstacle.LayerId) ||
                    !string.Equals(obstacle.LayerId.Trim(), obstacle.LayerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.layerId must be a non-empty trimmed string.");
                }

                if (!layerIds.Contains(obstacle.LayerId))
                {
                    throw new InvalidOperationException($"{path}.layerId references unknown nav layer '{obstacle.LayerId}'.");
                }

                if (obstacle.AreaId.HasValue && ((uint)obstacle.AreaId.Value > byte.MaxValue))
                {
                    throw new InvalidOperationException($"{path}.areaId must be between 0 and 255.");
                }

                if (obstacle.MinYcm >= obstacle.MaxYcm)
                {
                    throw new InvalidOperationException(
                        $"{path}.minYcm/maxYcm must author an explicit half-open interval [minYcm,maxYcm) with minYcm < maxYcm.");
                }

                switch (obstacle.Kind)
                {
                    case NavObstacleKind.Circle:
                        if (obstacle.RadiusCm <= 0)
                        {
                            throw new InvalidOperationException($"{path}.radiusCm must be > 0 for circle obstacles.");
                        }
                        break;
                    case NavObstacleKind.Polygon:
                        if (obstacle.Points == null || obstacle.Points.Count < 3)
                        {
                            throw new InvalidOperationException($"{path}.points must contain at least 3 points for polygon obstacles.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"{path}.kind '{obstacle.Kind}' is not supported by navmesh bake.");
                }
            }
        }

        public bool IsEnabled(int index)
        {
            return Require(index).Enabled;
        }

        public NavObstacleKind GetKind(int index)
        {
            return Require(index).Kind;
        }

        public bool MatchesLayer(int index, string layerId)
        {
            return string.Equals(Require(index).LayerId, layerId, StringComparison.Ordinal);
        }

        public bool TryGetAreaId(int index, out byte areaId)
        {
            NavObstacle obstacle = Require(index);
            if (!obstacle.AreaId.HasValue)
            {
                areaId = 0;
                return false;
            }

            int value = obstacle.AreaId.Value;
            if ((uint)value > byte.MaxValue)
            {
                throw new InvalidOperationException($"NavObstacleSet.obstacles[{index}].areaId must be between 0 and 255.");
            }

            areaId = (byte)value;
            return true;
        }

        public void GetCircle(int index, out int centerXcm, out int centerZcm, out int radiusCm)
        {
            NavObstacle obstacle = Require(index);
            if (obstacle.Kind != NavObstacleKind.Circle)
            {
                throw new InvalidOperationException($"NavObstacleSet.obstacles[{index}] is not a circle.");
            }

            centerXcm = obstacle.Center.Xcm;
            centerZcm = obstacle.Center.Zcm;
            radiusCm = obstacle.RadiusCm;
        }

        public int GetPolygonVertexCount(int index)
        {
            NavObstacle obstacle = Require(index);
            if (obstacle.Kind != NavObstacleKind.Polygon)
            {
                throw new InvalidOperationException($"NavObstacleSet.obstacles[{index}] is not a polygon.");
            }

            return obstacle.Points?.Count ?? 0;
        }

        public void GetPolygonVertex(int index, int vertexIndex, out int xcm, out int zcm)
        {
            NavObstacle obstacle = Require(index);
            if (obstacle.Kind != NavObstacleKind.Polygon)
            {
                throw new InvalidOperationException($"NavObstacleSet.obstacles[{index}] is not a polygon.");
            }

            if (obstacle.Points == null || (uint)vertexIndex >= (uint)obstacle.Points.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }

            NavPointCm point = obstacle.Points[vertexIndex];
            xcm = point.Xcm;
            zcm = point.Zcm;
        }

        public void GetVerticalRange(int index, out int minYcm, out int maxYcm)
        {
            NavObstacle obstacle = Require(index);
            minYcm = obstacle.MinYcm;
            maxYcm = obstacle.MaxYcm;
        }

        public void AppendHash(int index, StringBuilder sb)
        {
            NavObstacle obstacle = Require(index);
            sb.Append(obstacle.Id).Append(':')
                .Append(obstacle.Enabled).Append(':')
                .Append(obstacle.Kind).Append(':')
                .Append(obstacle.LayerId).Append(':')
                .Append(obstacle.AreaId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(':')
                .Append(obstacle.Center.Xcm).Append(',').Append(obstacle.Center.Zcm).Append(':')
                .Append(obstacle.RadiusCm).Append(':')
                .Append(obstacle.MinYcm).Append(',').Append(obstacle.MaxYcm).Append(':')
                .Append(obstacle.A.Xcm).Append(',').Append(obstacle.A.Zcm).Append(':')
                .Append(obstacle.B.Xcm).Append(',').Append(obstacle.B.Zcm).Append(':');
            if (obstacle.Points != null)
            {
                for (int p = 0; p < obstacle.Points.Count; p++)
                {
                    NavPointCm point = obstacle.Points[p];
                    sb.Append(point.Xcm).Append(',').Append(point.Zcm).Append(',');
                }
            }

            sb.Append(';');
        }

        private NavObstacle Require(int index)
        {
            if (Obstacles == null)
            {
                throw new InvalidOperationException("NavObstacleSet.obstacles is required.");
            }

            if ((uint)index >= (uint)Obstacles.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Obstacles[index]
                ?? throw new InvalidOperationException($"NavObstacleSet.obstacles[{index}] is null.");
        }
    }

    public sealed class NavObstacle
    {
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public NavObstacleKind Kind { get; set; } = NavObstacleKind.Polygon;
        public string LayerId { get; set; } = "Ground";
        public int? AreaId { get; set; }

        public List<NavPointCm> Points { get; set; } = new List<NavPointCm>();
        public NavPointCm Center { get; set; }
        public int RadiusCm { get; set; }
        /// <summary>Absolute world-cm half-open vertical interval start; must be &lt; <see cref="MaxYcm"/>.</summary>
        public int MinYcm { get; set; }
        /// <summary>Absolute world-cm half-open vertical interval end (exclusive).</summary>
        public int MaxYcm { get; set; }
        public NavPointCm A { get; set; }
        public NavPointCm B { get; set; }
    }

    public readonly struct NavPointCm
    {
        public int Xcm { get; init; }
        public int Zcm { get; init; }

        public NavPointCm(int xcm, int zcm)
        {
            Xcm = xcm;
            Zcm = zcm;
        }
    }
}
