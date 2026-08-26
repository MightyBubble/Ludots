using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.TransportNetwork
{
    public static class TransportNavObstacleSink
    {
        public static void AppendTo(
            NavObstacleSet target,
            TransportNetworkAsset asset,
            TransportNavObstacleSinkConfig config)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (config == null) throw new ArgumentNullException(nameof(config));

            asset.Validate();
            config.Validate();
            target.Obstacles ??= new List<NavObstacle>();

            Dictionary<string, TransportNetworkNode> nodesById = IndexNodes(asset);
            for (int segmentIndex = 0; segmentIndex < asset.Segments.Count; segmentIndex++)
            {
                TransportNetworkSegment segment = asset.Segments[segmentIndex]
                    ?? throw new InvalidOperationException($"TransportNetworkAsset '{asset.Id}' segments[{segmentIndex}] is null.");

                for (int ruleIndex = 0; ruleIndex < config.Rules.Count; ruleIndex++)
                {
                    TransportNavObstacleSinkRule rule = config.Rules[ruleIndex];
                    if (!Matches(segment, rule))
                    {
                        continue;
                    }

                    NavObstacle obstacle = BuildObstacle(asset, segment, nodesById, rule);
                    EnsureUniqueId(target, obstacle.Id);
                    target.Obstacles.Add(obstacle);
                }
            }
        }

        public static NavObstacleSet Build(TransportNetworkAsset asset, TransportNavObstacleSinkConfig config)
        {
            var set = new NavObstacleSet();
            AppendTo(set, asset, config);
            return set;
        }

        private static NavObstacle BuildObstacle(
            TransportNetworkAsset asset,
            TransportNetworkSegment segment,
            IReadOnlyDictionary<string, TransportNetworkNode> nodesById,
            TransportNavObstacleSinkRule rule)
        {
            List<NavPointCm> centerline = ResolveCenterline(segment, nodesById);
            string obstacleId = $"transport:{asset.Id}:{segment.Id}:{rule.Id}";

            List<NavPointCm> polygon = rule.Geometry switch
            {
                TransportNavObstacleGeometryKind.Corridor => BuildCorridorPolygon(
                    centerline,
                    ResolveCorridorWidthCm(segment, rule, obstacleId),
                    rule.SampleStepCm,
                    rule.CapEnds,
                    obstacleId),
                TransportNavObstacleGeometryKind.FilledRing => BuildFilledRingPolygon(centerline, obstacleId),
                _ => throw new InvalidOperationException($"Transport nav obstacle rule '{rule.Id}' geometry '{rule.Geometry}' is not supported.")
            };

            return new NavObstacle
            {
                Id = obstacleId,
                Enabled = true,
                Kind = NavObstacleKind.Polygon,
                LayerId = rule.LayerId,
                Points = polygon
            };
        }

        private static int ResolveCorridorWidthCm(
            TransportNetworkSegment segment,
            TransportNavObstacleSinkRule rule,
            string obstacleId)
        {
            if (segment.WidthCm < rule.MinWidthCm)
            {
                throw new InvalidOperationException(
                    $"Transport nav obstacle '{obstacleId}' widthCm={segment.WidthCm} is below rule minWidthCm={rule.MinWidthCm}.");
            }

            if (segment.WidthCm <= 0)
            {
                throw new InvalidOperationException(
                    $"Transport nav obstacle '{obstacleId}' requires widthCm > 0 for Corridor geometry.");
            }

            return segment.WidthCm;
        }

        private static List<NavPointCm> BuildCorridorPolygon(
            IReadOnlyList<NavPointCm> centerline,
            int widthCm,
            int sampleStepCm,
            bool capEnds,
            string obstacleId)
        {
            List<NavPointCm> samples = DensifyLinear(centerline, sampleStepCm);
            if (samples.Count < 2)
            {
                throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' corridor requires at least two distinct samples.");
            }

            int halfWidth = widthCm / 2;
            if (halfWidth <= 0)
            {
                throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' widthCm={widthCm} is too small to form a corridor.");
            }

            if (capEnds)
            {
                samples = CapCenterline(samples, halfWidth);
            }

            var left = new List<NavPointCm>(samples.Count);
            var right = new List<NavPointCm>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                ResolveTangent(samples, i, out double tx, out double tz);
                double len = Math.Sqrt(tx * tx + tz * tz);
                if (len <= 1e-9)
                {
                    throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' has a zero-length tangent at sample {i}.");
                }

                double nx = -tz / len;
                double nz = tx / len;
                left.Add(Offset(samples[i], nx, nz, halfWidth));
                right.Add(Offset(samples[i], -nx, -nz, halfWidth));
            }

            var polygon = new List<NavPointCm>(left.Count + right.Count);
            polygon.AddRange(left);
            for (int i = right.Count - 1; i >= 0; i--)
            {
                polygon.Add(right[i]);
            }

            if (polygon.Count < 3)
            {
                throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' produced fewer than 3 polygon vertices.");
            }

            return polygon;
        }

        private static List<NavPointCm> BuildFilledRingPolygon(IReadOnlyList<NavPointCm> centerline, string obstacleId)
        {
            if (centerline.Count < 3)
            {
                throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' FilledRing requires at least 3 points.");
            }

            var ring = new List<NavPointCm>(centerline.Count);
            for (int i = 0; i < centerline.Count; i++)
            {
                if (ring.Count > 0 &&
                    ring[ring.Count - 1].Xcm == centerline[i].Xcm &&
                    ring[ring.Count - 1].Zcm == centerline[i].Zcm)
                {
                    continue;
                }

                ring.Add(centerline[i]);
            }

            if (ring.Count >= 2 &&
                ring[0].Xcm == ring[ring.Count - 1].Xcm &&
                ring[0].Zcm == ring[ring.Count - 1].Zcm)
            {
                ring.RemoveAt(ring.Count - 1);
            }

            if (ring.Count < 3)
            {
                throw new InvalidOperationException($"Transport nav obstacle '{obstacleId}' FilledRing collapsed below 3 distinct vertices.");
            }

            return ring;
        }

        private static List<NavPointCm> DensifyLinear(IReadOnlyList<NavPointCm> centerline, int sampleStepCm)
        {
            var result = new List<NavPointCm>(centerline.Count * 2) { centerline[0] };
            for (int i = 0; i < centerline.Count - 1; i++)
            {
                NavPointCm a = centerline[i];
                NavPointCm b = centerline[i + 1];
                long dx = (long)b.Xcm - a.Xcm;
                long dz = (long)b.Zcm - a.Zcm;
                double length = Math.Sqrt(dx * dx + dz * dz);
                if (length <= 1e-9)
                {
                    continue;
                }

                int steps = Math.Max(1, (int)Math.Ceiling(length / sampleStepCm));
                for (int step = 1; step <= steps; step++)
                {
                    double t = step / (double)steps;
                    var point = new NavPointCm(
                        (int)Math.Round(a.Xcm + dx * t),
                        (int)Math.Round(a.Zcm + dz * t));
                    NavPointCm last = result[result.Count - 1];
                    if (last.Xcm == point.Xcm && last.Zcm == point.Zcm)
                    {
                        continue;
                    }

                    result.Add(point);
                }
            }

            return result;
        }

        private static List<NavPointCm> CapCenterline(IReadOnlyList<NavPointCm> samples, int halfWidth)
        {
            NavPointCm first = samples[0];
            NavPointCm second = samples[1];
            NavPointCm last = samples[samples.Count - 1];
            NavPointCm previous = samples[samples.Count - 2];

            ResolveUnit(first, second, out double ftx, out double ftz);
            ResolveUnit(previous, last, out double ltx, out double ltz);

            var capped = new List<NavPointCm>(samples.Count + 2)
            {
                Offset(first, -ftx, -ftz, halfWidth)
            };
            capped.AddRange(samples);
            capped.Add(Offset(last, ltx, ltz, halfWidth));
            return capped;
        }

        private static void ResolveTangent(IReadOnlyList<NavPointCm> samples, int index, out double tx, out double tz)
        {
            if (index == 0)
            {
                tx = samples[1].Xcm - samples[0].Xcm;
                tz = samples[1].Zcm - samples[0].Zcm;
                return;
            }

            if (index == samples.Count - 1)
            {
                tx = samples[index].Xcm - samples[index - 1].Xcm;
                tz = samples[index].Zcm - samples[index - 1].Zcm;
                return;
            }

            tx = samples[index + 1].Xcm - samples[index - 1].Xcm;
            tz = samples[index + 1].Zcm - samples[index - 1].Zcm;
        }

        private static void ResolveUnit(NavPointCm from, NavPointCm to, out double ux, out double uz)
        {
            double tx = to.Xcm - from.Xcm;
            double tz = to.Zcm - from.Zcm;
            double len = Math.Sqrt(tx * tx + tz * tz);
            if (len <= 1e-9)
            {
                throw new InvalidOperationException("Cannot normalize a zero-length transport corridor tangent.");
            }

            ux = tx / len;
            uz = tz / len;
        }

        private static NavPointCm Offset(NavPointCm point, double nx, double nz, int distanceCm)
        {
            return new NavPointCm(
                (int)Math.Round(point.Xcm + nx * distanceCm),
                (int)Math.Round(point.Zcm + nz * distanceCm));
        }

        private static List<NavPointCm> ResolveCenterline(
            TransportNetworkSegment segment,
            IReadOnlyDictionary<string, TransportNetworkNode> nodesById)
        {
            var result = new List<NavPointCm>(segment.Points.Count);
            for (int i = 0; i < segment.Points.Count; i++)
            {
                TransportNetworkPoint point = segment.Points[i]
                    ?? throw new InvalidOperationException($"Transport segment '{segment.Id}' points[{i}] is null.");
                if (!string.IsNullOrWhiteSpace(point.NodeId))
                {
                    TransportNetworkNode node = nodesById[point.NodeId];
                    result.Add(new NavPointCm(node.Xcm, node.Ycm));
                }
                else
                {
                    result.Add(new NavPointCm(point.Xcm, point.Ycm));
                }
            }

            return result;
        }

        private static Dictionary<string, TransportNetworkNode> IndexNodes(TransportNetworkAsset asset)
        {
            var result = new Dictionary<string, TransportNetworkNode>(asset.Nodes.Count, StringComparer.Ordinal);
            for (int i = 0; i < asset.Nodes.Count; i++)
            {
                TransportNetworkNode node = asset.Nodes[i]
                    ?? throw new InvalidOperationException($"TransportNetworkAsset '{asset.Id}' nodes[{i}] is null.");
                result.Add(node.Id, node);
            }

            return result;
        }

        private static bool Matches(TransportNetworkSegment segment, TransportNavObstacleSinkRule rule)
        {
            for (int i = 0; i < rule.RequiredTagsAll.Count; i++)
            {
                if (!ContainsTag(segment.Tags, rule.RequiredTagsAll[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < rule.ForbiddenTagsAny.Count; i++)
            {
                if (ContainsTag(segment.Tags, rule.ForbiddenTagsAny[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsTag(IReadOnlyList<string> tags, string expected)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureUniqueId(NavObstacleSet target, string obstacleId)
        {
            for (int i = 0; i < target.Obstacles.Count; i++)
            {
                if (string.Equals(target.Obstacles[i]?.Id, obstacleId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Transport nav obstacle id '{obstacleId}' collides with an existing obstacle.");
                }
            }
        }
    }
}
