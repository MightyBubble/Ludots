using System;
using System.Collections.Generic;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// 天气场区域的自定义元数据：形状/强度/效果类型与区域概要。
    /// 数值语义由消费端（宿主天气场系统）解释；Core 只负责传输。
    /// </summary>
    public struct WeatherRegionMeta
    {
        /// <summary>0=Polygon, 1=Circle, 2=Cloud（消费端自定义扩展允许）。</summary>
        public byte Shape;
        public byte EffectType;
        public byte EffectIntensity;
        public byte OutlineOnly;
        public float Intensity;
        public float RadiusCm;
        public float CenterXCm;
        public float CenterYCm;
    }

    /// <summary>
    /// Persistent weather-region buffer, structurally mirroring <see cref="SplineRibbonBuffer"/>:
    /// retained-by-stableId SoA storage with explicit Remove, plus a shared variable-length
    /// vertex pool (per-region offset/count) so arbitrary polygon point counts flow through
    /// the platform contract without per-item allocations.
    /// </summary>
    public sealed class WeatherRegionBuffer
    {
        private readonly int[] _stableIds;
        private readonly int[] _vertexOffsets;
        private readonly int[] _vertexCounts;
        private readonly WeatherRegionMeta[] _metas;
        private readonly float[] _vertexXCm;
        private readonly float[] _vertexYCm;
        private readonly Dictionary<int, int> _retainedIndexByStableId = new();
        private int _count;
        private int _vertexCount;

        public int Count => _count;
        public int Capacity => _stableIds.Length;
        public int VertexCapacity => _vertexXCm.Length;
        public int VertexCount => _vertexCount;

        public WeatherRegionBuffer(int capacity = 32, int vertexCapacity = 4096)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (vertexCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCapacity));
            }

            _stableIds = new int[capacity];
            _vertexOffsets = new int[capacity];
            _vertexCounts = new int[capacity];
            _metas = new WeatherRegionMeta[capacity];
            _vertexXCm = new float[vertexCapacity];
            _vertexYCm = new float[vertexCapacity];
        }

        /// <summary>
        /// Upsert a region by stableId. An existing entry is fully replaced (vertices and meta);
        /// a new entry appends. Vertex pool space is reclaimed by removing stale entries when
        /// the pool would overflow (oldest-first by insertion order).
        /// </summary>
        public bool TryAdd(int stableId, ReadOnlySpan<float> vertexXCm, ReadOnlySpan<float> vertexYCm, in WeatherRegionMeta meta)
        {
            if (stableId <= 0)
            {
                return false;
            }

            if (vertexXCm.Length != vertexYCm.Length)
            {
                throw new ArgumentException("Weather region vertex X/Y spans must have identical lengths.");
            }

            if (_retainedIndexByStableId.TryGetValue(stableId, out int existingIndex))
            {
                WriteEntry(existingIndex, stableId, vertexXCm, vertexYCm, in meta, replaceExisting: true);
                return true;
            }

            if (_count >= _stableIds.Length)
            {
                return false;
            }

            int index = _count++;
            if (!WriteEntry(index, stableId, vertexXCm, vertexYCm, in meta, replaceExisting: false))
            {
                _count = index;
                return false;
            }

            _retainedIndexByStableId[stableId] = index;
            return true;
        }

        private bool WriteEntry(int index, int stableId, ReadOnlySpan<float> vertexXCm, ReadOnlySpan<float> vertexYCm, in WeatherRegionMeta meta, bool replaceExisting)
        {
            int vertexCount = vertexXCm.Length;
            int vertexOffset;
            if (replaceExisting && _vertexCounts[index] >= vertexCount)
            {
                // 原地复用既有顶点段（新顶点数不超过旧段）。
                vertexOffset = _vertexOffsets[index];
            }
            else
            {
                if (_vertexCount + vertexCount > _vertexXCm.Length)
                {
                    ReclaimVertexPool();
                    if (_vertexCount + vertexCount > _vertexXCm.Length)
                    {
                        return false;
                    }

                    if (replaceExisting)
                    {
                        // 原段已被回收，条目仍占位：先释放旧计数再走追加路径。
                        _vertexCounts[index] = 0;
                    }
                }

                vertexOffset = _vertexCount;
                _vertexCount += vertexCount;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                _vertexXCm[vertexOffset + i] = vertexXCm[i];
                _vertexYCm[vertexOffset + i] = vertexYCm[i];
            }

            _stableIds[index] = stableId;
            _vertexOffsets[index] = vertexOffset;
            _vertexCounts[index] = vertexCount;
            _metas[index] = meta;
            return true;
        }

        public void Remove(int stableId)
        {
            if (stableId <= 0 || !_retainedIndexByStableId.TryGetValue(stableId, out int index))
            {
                return;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                CopyEntry(lastIndex, index);
                _retainedIndexByStableId[_stableIds[index]] = index;
            }

            _count = lastIndex;
            _retainedIndexByStableId.Remove(stableId);
            RebuildVertexPool();
        }

        public void Clear()
        {
            _count = 0;
            _vertexCount = 0;
            _retainedIndexByStableId.Clear();
        }

        /// <summary>移除已失效条目后重排顶点池（紧凑化）。</summary>
        private void RebuildVertexPool()
        {
            int writeVertex = 0;
            for (int i = 0; i < _count; i++)
            {
                int count = _vertexCounts[i];
                int offset = _vertexOffsets[i];
                if (offset != writeVertex)
                {
                    for (int v = 0; v < count; v++)
                    {
                        _vertexXCm[writeVertex + v] = _vertexXCm[offset + v];
                        _vertexYCm[writeVertex + v] = _vertexYCm[offset + v];
                    }

                    _vertexOffsets[i] = writeVertex;
                }

                writeVertex += count;
            }

            _vertexCount = writeVertex;
        }

        /// <summary>顶点池将溢出时，按插入序牺牲最旧条目腾出空间。</summary>
        private void ReclaimVertexPool()
        {
            while (_vertexCount + 256 > _vertexXCm.Length && _count > 1)
            {
                int victimStableId = _stableIds[0];
                if (victimStableId > 0)
                {
                    Remove(victimStableId);
                }
                else
                {
                    break;
                }
            }
        }

        private void CopyEntry(int source, int destination)
        {
            _stableIds[destination] = _stableIds[source];
            _vertexOffsets[destination] = _vertexOffsets[source];
            _vertexCounts[destination] = _vertexCounts[source];
            _metas[destination] = _metas[source];
        }

        public bool TryGetMeta(int index, out WeatherRegionMeta meta)
        {
            if ((uint)index >= (uint)_count)
            {
                meta = default;
                return false;
            }

            meta = _metas[index];
            return true;
        }

        public ReadOnlySpan<int> StableIds => _stableIds.AsSpan(0, _count);
        public ReadOnlySpan<int> VertexOffsets => _vertexOffsets.AsSpan(0, _count);
        public ReadOnlySpan<int> VertexCounts => _vertexCounts.AsSpan(0, _count);
        public ReadOnlySpan<WeatherRegionMeta> Metas => _metas.AsSpan(0, _count);
        public ReadOnlySpan<float> VertexXCm => _vertexXCm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<float> VertexYCm => _vertexYCm.AsSpan(0, _vertexCount);

        /// <summary>读取指定条目的顶点切片。</summary>
        public bool TryGetVertices(int index, out ReadOnlySpan<float> vertexXCm, out ReadOnlySpan<float> vertexYCm)
        {
            if ((uint)index >= (uint)_count)
            {
                vertexXCm = default;
                vertexYCm = default;
                return false;
            }

            vertexXCm = _vertexXCm.AsSpan(_vertexOffsets[index], _vertexCounts[index]);
            vertexYCm = _vertexYCm.AsSpan(_vertexOffsets[index], _vertexCounts[index]);
            return true;
        }
    }
}
