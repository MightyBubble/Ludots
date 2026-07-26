using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Fixed-capacity numeric SoA obstacle snapshot for runtime-incremental bake.
    /// Preallocates all channels; capture/sort/copy never grow arrays or allocate per tick.
    /// Every primitive owns an explicit absolute world-cm half-open vertical interval [minYcm,maxYcm).
    /// </summary>
    public sealed class RuntimeNavObstacleSnapshot : INavObstacleSource
    {
        private readonly int[] _entityIds;
        private readonly int[] _pieceIndices;
        private readonly byte[] _kinds;
        private readonly byte[] _enabled;
        private readonly int[] _centerXcm;
        private readonly int[] _centerZcm;
        private readonly int[] _radiusCm;
        private readonly int[] _minYcm;
        private readonly int[] _maxYcm;
        private readonly int[] _vertexOffsets;
        private readonly int[] _vertexCounts;
        private readonly int[] _areaIds;
        private readonly int[] _vertexXcm;
        private readonly int[] _vertexZcm;
        private readonly string _boundLayerId;

        private int _obstacleCount;
        private int _vertexCount;
        private int _captureVertexCursor;

        public RuntimeNavObstacleSnapshot(
            int obstaclePrimitiveCapacity,
            int polygonVertexCapacity,
            string boundLayerId)
        {
            if (obstaclePrimitiveCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(obstaclePrimitiveCapacity),
                    "obstaclePrimitiveCapacity must be > 0.");
            }

            if (polygonVertexCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(polygonVertexCapacity),
                    "polygonVertexCapacity must be > 0.");
            }

            if (string.IsNullOrWhiteSpace(boundLayerId) ||
                !string.Equals(boundLayerId.Trim(), boundLayerId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Runtime nav obstacle snapshot requires a non-empty trimmed bound layer id.",
                    nameof(boundLayerId));
            }

            ObstaclePrimitiveCapacity = obstaclePrimitiveCapacity;
            PolygonVertexCapacity = polygonVertexCapacity;
            _boundLayerId = boundLayerId;

            _entityIds = new int[obstaclePrimitiveCapacity];
            _pieceIndices = new int[obstaclePrimitiveCapacity];
            _kinds = new byte[obstaclePrimitiveCapacity];
            _enabled = new byte[obstaclePrimitiveCapacity];
            _centerXcm = new int[obstaclePrimitiveCapacity];
            _centerZcm = new int[obstaclePrimitiveCapacity];
            _radiusCm = new int[obstaclePrimitiveCapacity];
            _minYcm = new int[obstaclePrimitiveCapacity];
            _maxYcm = new int[obstaclePrimitiveCapacity];
            _vertexOffsets = new int[obstaclePrimitiveCapacity];
            _vertexCounts = new int[obstaclePrimitiveCapacity];
            _areaIds = new int[obstaclePrimitiveCapacity];
            _vertexXcm = new int[polygonVertexCapacity];
            _vertexZcm = new int[polygonVertexCapacity];
        }

        public int ObstaclePrimitiveCapacity { get; }

        public int PolygonVertexCapacity { get; }

        public string BoundLayerId => _boundLayerId;

        public int ObstacleCount => _obstacleCount;

        public int PolygonVertexCount => _vertexCount;

        public ReadOnlySpan<int> EntityIds => _entityIds.AsSpan(0, _obstacleCount);

        public ReadOnlySpan<int> PieceIndices => _pieceIndices.AsSpan(0, _obstacleCount);

        public ReadOnlySpan<int> MinYcm => _minYcm.AsSpan(0, _obstacleCount);

        public ReadOnlySpan<int> MaxYcm => _maxYcm.AsSpan(0, _obstacleCount);

        public RuntimeNavObstacleSnapshot CreateCompatibleEmpty()
            => new RuntimeNavObstacleSnapshot(ObstaclePrimitiveCapacity, PolygonVertexCapacity, _boundLayerId);

        public void BeginCapture()
        {
            _obstacleCount = 0;
            _vertexCount = 0;
            _captureVertexCursor = 0;
        }

        /// <summary>
        /// Begins a primitive with an explicit vertical half-open interval so incomplete extents cannot be published.
        /// </summary>
        public int BeginPrimitive(int entityId, int pieceIndex, NavObstacleKind kind, int minYcm, int maxYcm)
        {
            if (minYcm >= maxYcm)
            {
                throw new InvalidOperationException(
                    "Runtime nav obstacle BeginPrimitive requires minYcm < maxYcm for half-open interval [minYcm,maxYcm).");
            }

            if (_obstacleCount >= ObstaclePrimitiveCapacity)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle snapshot exceeded obstaclePrimitiveCapacity ({ObstaclePrimitiveCapacity}); required {_obstacleCount + 1}.");
            }

            int index = _obstacleCount;
            _entityIds[index] = entityId;
            _pieceIndices[index] = pieceIndex;
            _kinds[index] = (byte)kind;
            _enabled[index] = 1;
            _centerXcm[index] = 0;
            _centerZcm[index] = 0;
            _radiusCm[index] = 0;
            _minYcm[index] = minYcm;
            _maxYcm[index] = maxYcm;
            _vertexOffsets[index] = 0;
            _vertexCounts[index] = 0;
            _areaIds[index] = -1;
            _obstacleCount++;
            return index;
        }

        public void SetCircle(int index, int centerXcm, int centerZcm, int radiusCm)
        {
            RequirePrimitiveIndex(index);
            if (_kinds[index] != (byte)NavObstacleKind.Circle)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} is not a circle.");
            }

            if (radiusCm <= 0)
            {
                throw new InvalidOperationException("Circle nav obstacle radiusCm must be > 0.");
            }

            _centerXcm[index] = centerXcm;
            _centerZcm[index] = centerZcm;
            _radiusCm[index] = radiusCm;
        }

        public int BeginPolygonVertices(int index, int vertexCount)
        {
            RequirePrimitiveIndex(index);
            if (_kinds[index] != (byte)NavObstacleKind.Polygon)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} is not a polygon.");
            }

            if (vertexCount < 3)
            {
                throw new InvalidOperationException("Polygon nav obstacle requires at least 3 points.");
            }

            if (_captureVertexCursor > PolygonVertexCapacity - vertexCount)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle snapshot exceeded polygonVertexCapacity ({PolygonVertexCapacity}); required {_captureVertexCursor + vertexCount}.");
            }

            _vertexOffsets[index] = _captureVertexCursor;
            _vertexCounts[index] = vertexCount;
            int offset = _captureVertexCursor;
            _captureVertexCursor += vertexCount;
            return offset;
        }

        public void SetPolygonVertex(int absoluteVertexIndex, int xcm, int zcm)
        {
            if ((uint)absoluteVertexIndex >= (uint)_captureVertexCursor)
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteVertexIndex));
            }

            _vertexXcm[absoluteVertexIndex] = xcm;
            _vertexZcm[absoluteVertexIndex] = zcm;
        }

        public void EndCaptureAndSort()
        {
            _vertexCount = _captureVertexCursor;
            ValidateCapturedPrimitives();
            HeapSortByStableKey();
            RejectDuplicateStableKeys();
        }

        public void CopyTo(RuntimeNavObstacleSnapshot destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.ObstaclePrimitiveCapacity != ObstaclePrimitiveCapacity ||
                destination.PolygonVertexCapacity != PolygonVertexCapacity ||
                !string.Equals(destination._boundLayerId, _boundLayerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime nav obstacle generation snapshot requires identical capacities and bound layer id.");
            }

            destination._obstacleCount = _obstacleCount;
            destination._vertexCount = _vertexCount;
            destination._captureVertexCursor = _vertexCount;

            if (_obstacleCount > 0)
            {
                Array.Copy(_entityIds, destination._entityIds, _obstacleCount);
                Array.Copy(_pieceIndices, destination._pieceIndices, _obstacleCount);
                Array.Copy(_kinds, destination._kinds, _obstacleCount);
                Array.Copy(_enabled, destination._enabled, _obstacleCount);
                Array.Copy(_centerXcm, destination._centerXcm, _obstacleCount);
                Array.Copy(_centerZcm, destination._centerZcm, _obstacleCount);
                Array.Copy(_radiusCm, destination._radiusCm, _obstacleCount);
                Array.Copy(_minYcm, destination._minYcm, _obstacleCount);
                Array.Copy(_maxYcm, destination._maxYcm, _obstacleCount);
                Array.Copy(_vertexOffsets, destination._vertexOffsets, _obstacleCount);
                Array.Copy(_vertexCounts, destination._vertexCounts, _obstacleCount);
                Array.Copy(_areaIds, destination._areaIds, _obstacleCount);
            }

            if (_vertexCount > 0)
            {
                Array.Copy(_vertexXcm, destination._vertexXcm, _vertexCount);
                Array.Copy(_vertexZcm, destination._vertexZcm, _vertexCount);
            }
        }

        public void ValidateForBake(IReadOnlyList<NavLayerConfig> layers, string pathPrefix)
        {
            if (layers == null || layers.Count == 0)
            {
                throw new InvalidOperationException($"{pathPrefix} validation requires authored nav layers.");
            }

            bool layerFound = false;
            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i]
                    ?? throw new InvalidOperationException($"NavBakeContext.config.layers[{i}] is null.");
                if (string.Equals(layer.Id, _boundLayerId, StringComparison.Ordinal))
                {
                    layerFound = true;
                    break;
                }
            }

            if (!layerFound)
            {
                throw new InvalidOperationException(
                    $"{pathPrefix} bound layer id '{_boundLayerId}' is not present in NavBakeContext.config.layers.");
            }

            for (int i = 0; i < _obstacleCount; i++)
            {
                ValidatePrimitiveAt(i, pathPrefix);
            }
        }

        public bool IsEnabled(int index)
        {
            RequirePrimitiveIndex(index);
            return _enabled[index] != 0;
        }

        public NavObstacleKind GetKind(int index)
        {
            RequirePrimitiveIndex(index);
            return (NavObstacleKind)_kinds[index];
        }

        public bool MatchesLayer(int index, string layerId)
        {
            RequirePrimitiveIndex(index);
            return string.Equals(layerId, _boundLayerId, StringComparison.Ordinal);
        }

        public bool TryGetAreaId(int index, out byte areaId)
        {
            RequirePrimitiveIndex(index);
            int value = _areaIds[index];
            if (value < 0)
            {
                areaId = 0;
                return false;
            }

            if (value > byte.MaxValue)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} areaId must be between 0 and 255.");
            }

            areaId = (byte)value;
            return true;
        }

        public void GetCircle(int index, out int centerXcm, out int centerZcm, out int radiusCm)
        {
            RequirePrimitiveIndex(index);
            if (_kinds[index] != (byte)NavObstacleKind.Circle)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} is not a circle.");
            }

            centerXcm = _centerXcm[index];
            centerZcm = _centerZcm[index];
            radiusCm = _radiusCm[index];
        }

        public int GetPolygonVertexCount(int index)
        {
            RequirePrimitiveIndex(index);
            if (_kinds[index] != (byte)NavObstacleKind.Polygon)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} is not a polygon.");
            }

            return _vertexCounts[index];
        }

        public void GetPolygonVertex(int index, int vertexIndex, out int xcm, out int zcm)
        {
            RequirePrimitiveIndex(index);
            if (_kinds[index] != (byte)NavObstacleKind.Polygon)
            {
                throw new InvalidOperationException($"Runtime nav obstacle primitive {index} is not a polygon.");
            }

            if ((uint)vertexIndex >= (uint)_vertexCounts[index])
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }

            int absolute = _vertexOffsets[index] + vertexIndex;
            xcm = _vertexXcm[absolute];
            zcm = _vertexZcm[absolute];
        }

        public void GetVerticalRange(int index, out int minYcm, out int maxYcm)
        {
            RequirePrimitiveIndex(index);
            minYcm = _minYcm[index];
            maxYcm = _maxYcm[index];
        }

        public void AppendHash(int index, StringBuilder sb)
        {
            RequirePrimitiveIndex(index);
            sb.Append(_entityIds[index]).Append(':')
                .Append(_pieceIndices[index]).Append(':')
                .Append(_enabled[index] != 0).Append(':')
                .Append((NavObstacleKind)_kinds[index]).Append(':')
                .Append(_boundLayerId).Append(':');
            if (_areaIds[index] >= 0)
            {
                sb.Append(_areaIds[index].ToString(CultureInfo.InvariantCulture));
            }

            sb.Append(':')
                .Append(_centerXcm[index]).Append(',').Append(_centerZcm[index]).Append(':')
                .Append(_radiusCm[index]).Append(':')
                .Append(_minYcm[index]).Append(',').Append(_maxYcm[index]).Append(':')
                .Append("0,0:0,0:");
            if (_kinds[index] == (byte)NavObstacleKind.Polygon)
            {
                int count = _vertexCounts[index];
                int offset = _vertexOffsets[index];
                for (int p = 0; p < count; p++)
                {
                    sb.Append(_vertexXcm[offset + p]).Append(',').Append(_vertexZcm[offset + p]).Append(',');
                }
            }

            sb.Append(';');
        }

        private void ValidateCapturedPrimitives()
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                ValidatePrimitiveAt(i, pathPrefix: null);
            }
        }

        private void ValidatePrimitiveAt(int index, string? pathPrefix)
        {
            if (_enabled[index] == 0)
            {
                return;
            }

            if (_minYcm[index] >= _maxYcm[index])
            {
                throw new InvalidOperationException(
                    $"{FormatPrimitivePath(pathPrefix, index)}.minYcm/maxYcm must author an explicit half-open interval [minYcm,maxYcm) with minYcm < maxYcm.");
            }

            switch ((NavObstacleKind)_kinds[index])
            {
                case NavObstacleKind.Circle:
                    if (_radiusCm[index] <= 0)
                    {
                        throw new InvalidOperationException(
                            $"{FormatPrimitivePath(pathPrefix, index)}.radiusCm must be > 0 for circle obstacles.");
                    }
                    break;
                case NavObstacleKind.Polygon:
                    if (_vertexCounts[index] < 3)
                    {
                        throw new InvalidOperationException(
                            $"{FormatPrimitivePath(pathPrefix, index)}.points must contain at least 3 points for polygon obstacles.");
                    }

                    int end = checked(_vertexOffsets[index] + _vertexCounts[index]);
                    if (_vertexOffsets[index] < 0 || end > _vertexCount)
                    {
                        throw new InvalidOperationException(
                            $"{FormatPrimitivePath(pathPrefix, index)} polygon vertex range is out of snapshot bounds.");
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{FormatPrimitivePath(pathPrefix, index)}.kind '{(NavObstacleKind)_kinds[index]}' is not supported by navmesh bake.");
            }
        }

        private static string FormatPrimitivePath(string? pathPrefix, int index)
            => pathPrefix == null
                ? $"Runtime nav obstacle snapshot[{index}]"
                : $"{pathPrefix}[{index}]";

        private void HeapSortByStableKey()
        {
            int n = _obstacleCount;
            for (int i = (n / 2) - 1; i >= 0; i--)
            {
                SiftDown(n, i);
            }

            for (int end = n - 1; end > 0; end--)
            {
                SwapRows(0, end);
                SiftDown(end, 0);
            }
        }

        private void SiftDown(int heapSize, int root)
        {
            while (true)
            {
                int largest = root;
                int left = (root * 2) + 1;
                int right = left + 1;
                if (left < heapSize && CompareStableKeys(left, largest) > 0)
                {
                    largest = left;
                }

                if (right < heapSize && CompareStableKeys(right, largest) > 0)
                {
                    largest = right;
                }

                if (largest == root)
                {
                    return;
                }

                SwapRows(root, largest);
                root = largest;
            }
        }

        private void RejectDuplicateStableKeys()
        {
            for (int i = 1; i < _obstacleCount; i++)
            {
                if (CompareStableKeys(i - 1, i) != 0)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Runtime nav obstacle snapshot contains duplicate stable key entityId={_entityIds[i]} pieceIndex={_pieceIndices[i]}.");
            }
        }

        private int CompareStableKeys(int leftIndex, int rightIndex)
        {
            int entity = _entityIds[leftIndex].CompareTo(_entityIds[rightIndex]);
            if (entity != 0)
            {
                return entity;
            }

            return _pieceIndices[leftIndex].CompareTo(_pieceIndices[rightIndex]);
        }

        private void SwapRows(int a, int b)
        {
            Swap(ref _entityIds[a], ref _entityIds[b]);
            Swap(ref _pieceIndices[a], ref _pieceIndices[b]);
            Swap(ref _kinds[a], ref _kinds[b]);
            Swap(ref _enabled[a], ref _enabled[b]);
            Swap(ref _centerXcm[a], ref _centerXcm[b]);
            Swap(ref _centerZcm[a], ref _centerZcm[b]);
            Swap(ref _radiusCm[a], ref _radiusCm[b]);
            Swap(ref _minYcm[a], ref _minYcm[b]);
            Swap(ref _maxYcm[a], ref _maxYcm[b]);
            Swap(ref _vertexOffsets[a], ref _vertexOffsets[b]);
            Swap(ref _vertexCounts[a], ref _vertexCounts[b]);
            Swap(ref _areaIds[a], ref _areaIds[b]);
        }

        private static void Swap<T>(ref T left, ref T right)
        {
            T tmp = left;
            left = right;
            right = tmp;
        }

        private void RequirePrimitiveIndex(int index)
        {
            if ((uint)index >= (uint)_obstacleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
