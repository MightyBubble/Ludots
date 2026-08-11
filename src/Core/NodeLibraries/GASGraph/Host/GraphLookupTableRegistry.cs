using System;
using Ludots.Core.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public enum GraphLookupColumnKind : byte
    {
        Int = 1,
        Float = 2,
        TextToken = 3,
    }

    public readonly struct GraphLookupFieldInfo
    {
        public GraphLookupFieldInfo(int tableId, int columnIndex, GraphLookupColumnKind kind)
        {
            TableId = tableId;
            ColumnIndex = columnIndex;
            Kind = kind;
        }

        public int TableId { get; }
        public int ColumnIndex { get; }
        public GraphLookupColumnKind Kind { get; }
    }

    /// <summary>
    /// Read-only Mod/user lookup tables for ResolveTableRow / TableRead*.
    /// Hot path is 0Alloc after Freeze.
    /// </summary>
    public sealed class GraphLookupTableRegistry
    {
        public const string UnknownTableError = "GAS.GRAPH_LOOKUP.ERR.UnknownTable";
        public const string UnknownFieldError = "GAS.GRAPH_LOOKUP.ERR.UnknownField";
        public const string RowMissingError = "GAS.GRAPH_LOOKUP.ERR.RowMissing";
        public const string FieldKindMismatchError = "GAS.GRAPH_LOOKUP.ERR.FieldKindMismatch";
        public const string InvalidRowHandleError = "GAS.GRAPH_LOOKUP.ERR.InvalidRowHandle";
        public const string FrozenError = "GAS.GRAPH_LOOKUP.ERR.Frozen";

        private readonly StringIntRegistry _tableIds;
        private readonly StringIntRegistry _fieldIds;
        private GraphLookupFieldInfo[] _fields;
        private TableSlot[] _tables;
        private bool _frozen;

        public GraphLookupTableRegistry(int initialTableCapacity = 8)
        {
            int capacity = Math.Max(4, initialTableCapacity);
            _tableIds = new StringIntRegistry(
                capacity: capacity,
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            _fieldIds = new StringIntRegistry(
                capacity: capacity * 4,
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            _fields = new GraphLookupFieldInfo[Math.Max(8, capacity * 4)];
            _tables = new TableSlot[capacity];
        }

        public bool IsFrozen => _frozen;

        public int TableCount => _tableIds.Count;

        public int RegisterTable(
            string tableId,
            ReadOnlySpan<(string FieldId, GraphLookupColumnKind Kind)> columns,
            ReadOnlySpan<int> keys,
            ReadOnlySpan<int> intValues,
            ReadOnlySpan<float> floatValues)
        {
            if (_frozen)
            {
                throw new InvalidOperationException(FrozenError);
            }

            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("tableId is required.", nameof(tableId));
            }

            if (_tableIds.TryGetId(tableId, out _))
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' is already registered.");
            }

            if (columns.Length == 0)
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' must declare at least one column.");
            }

            if (keys.Length == 0)
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' must declare at least one row.");
            }

            int intColumnCount = 0;
            int floatColumnCount = 0;
            var columnKinds = new GraphLookupColumnKind[columns.Length];
            var columnStorageIndex = new int[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                string fieldId = columns[c].FieldId;
                if (string.IsNullOrWhiteSpace(fieldId))
                {
                    throw new ArgumentException($"Lookup table '{tableId}' has an empty field id.");
                }

                GraphLookupColumnKind kind = columns[c].Kind;
                columnKinds[c] = kind;
                switch (kind)
                {
                    case GraphLookupColumnKind.Int:
                    case GraphLookupColumnKind.TextToken:
                        columnStorageIndex[c] = intColumnCount++;
                        break;
                    case GraphLookupColumnKind.Float:
                        columnStorageIndex[c] = floatColumnCount++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Lookup table '{tableId}' field '{fieldId}' has unsupported kind '{kind}'.");
                }
            }

            int rowCount = keys.Length;
            if (intValues.Length != rowCount * intColumnCount)
            {
                throw new ArgumentException(
                    $"Lookup table '{tableId}' intValues length mismatch (expected {rowCount * intColumnCount}, got {intValues.Length}).",
                    nameof(intValues));
            }

            if (floatValues.Length != rowCount * floatColumnCount)
            {
                throw new ArgumentException(
                    $"Lookup table '{tableId}' floatValues length mismatch (expected {rowCount * floatColumnCount}, got {floatValues.Length}).",
                    nameof(floatValues));
            }

            int id = _tableIds.Register(tableId);
            EnsureTableSlot(id);

            var intColumns = new int[intColumnCount][];
            for (int c = 0; c < intColumnCount; c++)
            {
                intColumns[c] = new int[rowCount];
            }

            var floatColumns = new float[floatColumnCount][];
            for (int c = 0; c < floatColumnCount; c++)
            {
                floatColumns[c] = new float[rowCount];
            }

            var keyCopy = new int[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                keyCopy[r] = keys[r];
                for (int c = 0; c < columns.Length; c++)
                {
                    int storage = columnStorageIndex[c];
                    if (columnKinds[c] == GraphLookupColumnKind.Float)
                    {
                        floatColumns[storage][r] = floatValues[r * floatColumnCount + storage];
                    }
                    else
                    {
                        intColumns[storage][r] = intValues[r * intColumnCount + storage];
                    }
                }
            }

            BuildKeyIndex(keyCopy, out int[]? denseMap, out int denseMin, out int[]? openKeys, out int[]? openRows);

            _tables[id] = new TableSlot(
                keyCopy,
                intColumns,
                floatColumns,
                columnKinds,
                columnStorageIndex,
                denseMap,
                denseMin,
                openKeys,
                openRows);

            for (int c = 0; c < columns.Length; c++)
            {
                string fieldSymbol = EncodeFieldSymbol(tableId, columns[c].FieldId);
                if (_fieldIds.TryGetId(fieldSymbol, out _))
                {
                    throw new InvalidOperationException($"Lookup field '{fieldSymbol}' is already registered.");
                }

                int fieldId = _fieldIds.Register(fieldSymbol);
                EnsureFieldSlot(fieldId);
                _fields[fieldId] = new GraphLookupFieldInfo(id, c, columnKinds[c]);
            }

            return id;
        }

        public void Freeze() => _frozen = true;

        public int GetTableId(string tableId)
        {
            if (!_tableIds.TryGetId(tableId, out int id) || id <= 0)
            {
                throw new InvalidOperationException($"{UnknownTableError}: '{tableId}'.");
            }

            return id;
        }

        public bool TryGetTableId(string tableId, out int id) => _tableIds.TryGetId(tableId, out id) && id > 0;

        public int GetFieldId(string tableId, string fieldId)
        {
            string symbol = EncodeFieldSymbol(tableId, fieldId);
            if (!_fieldIds.TryGetId(symbol, out int id) || id <= 0)
            {
                throw new InvalidOperationException($"{UnknownFieldError}: '{symbol}'.");
            }

            return id;
        }

        public int GetFieldId(string fieldSymbol)
        {
            if (!_fieldIds.TryGetId(fieldSymbol, out int id) || id <= 0)
            {
                throw new InvalidOperationException($"{UnknownFieldError}: '{fieldSymbol}'.");
            }

            return id;
        }

        public int ResolveRow(int tableId, int key)
        {
            ref readonly TableSlot table = ref RequireTable(tableId);
            int rowIndex = FindRowIndex(in table, key);
            if (rowIndex < 0)
            {
                throw new InvalidOperationException($"{RowMissingError}: tableId={tableId} key={key}.");
            }

            return PackRowHandle(tableId, rowIndex);
        }

        public int ReadInt(int rowHandle, int fieldId)
        {
            GraphLookupFieldInfo field = RequireField(fieldId);
            if (field.Kind is not (GraphLookupColumnKind.Int or GraphLookupColumnKind.TextToken))
            {
                throw new InvalidOperationException(
                    $"{FieldKindMismatchError}: fieldId={fieldId} kind={field.Kind} expected Int/TextToken.");
            }

            UnpackRowHandle(rowHandle, out int tableId, out int rowIndex);
            if (tableId != field.TableId)
            {
                throw new InvalidOperationException(
                    $"{InvalidRowHandleError}: rowHandle tableId={tableId} field tableId={field.TableId}.");
            }

            ref readonly TableSlot table = ref RequireTable(tableId);
            if ((uint)rowIndex >= (uint)table.Keys.Length)
            {
                throw new InvalidOperationException($"{InvalidRowHandleError}: rowIndex={rowIndex}.");
            }

            int storage = table.ColumnStorageIndex[field.ColumnIndex];
            return table.IntColumns[storage][rowIndex];
        }

        public float ReadFloat(int rowHandle, int fieldId)
        {
            GraphLookupFieldInfo field = RequireField(fieldId);
            if (field.Kind != GraphLookupColumnKind.Float)
            {
                throw new InvalidOperationException(
                    $"{FieldKindMismatchError}: fieldId={fieldId} kind={field.Kind} expected Float.");
            }

            UnpackRowHandle(rowHandle, out int tableId, out int rowIndex);
            if (tableId != field.TableId)
            {
                throw new InvalidOperationException(
                    $"{InvalidRowHandleError}: rowHandle tableId={tableId} field tableId={field.TableId}.");
            }

            ref readonly TableSlot table = ref RequireTable(tableId);
            if ((uint)rowIndex >= (uint)table.Keys.Length)
            {
                throw new InvalidOperationException($"{InvalidRowHandleError}: rowIndex={rowIndex}.");
            }

            int storage = table.ColumnStorageIndex[field.ColumnIndex];
            return table.FloatColumns[storage][rowIndex];
        }

        public static string EncodeFieldSymbol(string tableId, string fieldId) => tableId + "/" + fieldId;

        public static bool TrySplitFieldSymbol(string fieldSymbol, out string tableId, out string fieldId)
        {
            tableId = string.Empty;
            fieldId = string.Empty;
            if (string.IsNullOrWhiteSpace(fieldSymbol))
            {
                return false;
            }

            int slash = fieldSymbol.IndexOf('/');
            if (slash <= 0 || slash >= fieldSymbol.Length - 1)
            {
                return false;
            }

            tableId = fieldSymbol.Substring(0, slash);
            fieldId = fieldSymbol.Substring(slash + 1);
            return !string.IsNullOrWhiteSpace(tableId) && !string.IsNullOrWhiteSpace(fieldId);
        }

        private static int PackRowHandle(int tableId, int rowIndex)
        {
            if ((uint)tableId > 0xFFFF || (uint)rowIndex >= 0xFFFF)
            {
                throw new InvalidOperationException(
                    $"{InvalidRowHandleError}: tableId={tableId} rowIndex={rowIndex} exceeds pack limits.");
            }

            return (tableId << 16) | (rowIndex + 1);
        }

        private static void UnpackRowHandle(int rowHandle, out int tableId, out int rowIndex)
        {
            if (rowHandle <= 0)
            {
                throw new InvalidOperationException($"{InvalidRowHandleError}: rowHandle={rowHandle}.");
            }

            tableId = rowHandle >>> 16;
            int packedRow = rowHandle & 0xFFFF;
            if (packedRow == 0)
            {
                throw new InvalidOperationException($"{InvalidRowHandleError}: rowHandle={rowHandle}.");
            }

            rowIndex = packedRow - 1;
        }

        private static int FindRowIndex(in TableSlot table, int key)
        {
            if (table.DenseMap != null)
            {
                int idx = key - table.DenseMin;
                if ((uint)idx >= (uint)table.DenseMap.Length)
                {
                    return -1;
                }

                return table.DenseMap[idx];
            }

            int[] openKeys = table.OpenKeys!;
            int[] openRows = table.OpenRows!;
            int mask = openKeys.Length - 1;
            int probe = Mix(key) & mask;
            for (int i = 0; i < openKeys.Length; i++)
            {
                int stored = openKeys[probe];
                if (stored == int.MinValue)
                {
                    return -1;
                }

                if (stored == key)
                {
                    return openRows[probe];
                }

                probe = (probe + 1) & mask;
            }

            return -1;
        }

        private static void BuildKeyIndex(
            int[] keys,
            out int[]? denseMap,
            out int denseMin,
            out int[]? openKeys,
            out int[]? openRows)
        {
            denseMap = null;
            denseMin = 0;
            openKeys = null;
            openRows = null;

            int min = keys[0];
            int max = keys[0];
            for (int i = 1; i < keys.Length; i++)
            {
                int key = keys[i];
                if (key < min) min = key;
                if (key > max) max = key;
            }

            long span = (long)max - min + 1;
            // Prefer dense when span is modest relative to row count.
            if (span > 0 && span <= Math.Max(64, keys.Length * 4L) && span <= 1 << 20)
            {
                var map = new int[(int)span];
                Array.Fill(map, -1);
                for (int i = 0; i < keys.Length; i++)
                {
                    int idx = keys[i] - min;
                    if (map[idx] >= 0)
                    {
                        throw new InvalidOperationException($"Duplicate lookup key {keys[i]}.");
                    }

                    map[idx] = i;
                }

                denseMap = map;
                denseMin = min;
                return;
            }

            int capacity = 1;
            while (capacity < keys.Length * 2)
            {
                capacity <<= 1;
            }

            openKeys = new int[capacity];
            openRows = new int[capacity];
            Array.Fill(openKeys, int.MinValue);
            int mask = capacity - 1;
            for (int i = 0; i < keys.Length; i++)
            {
                int key = keys[i];
                int probe = Mix(key) & mask;
                for (int n = 0; n < capacity; n++)
                {
                    if (openKeys[probe] == int.MinValue)
                    {
                        openKeys[probe] = key;
                        openRows[probe] = i;
                        break;
                    }

                    if (openKeys[probe] == key)
                    {
                        throw new InvalidOperationException($"Duplicate lookup key {key}.");
                    }

                    probe = (probe + 1) & mask;
                }
            }
        }

        private static int Mix(int key)
        {
            unchecked
            {
                uint x = (uint)key;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (int)x;
            }
        }

        private ref readonly TableSlot RequireTable(int tableId)
        {
            if (tableId <= 0 ||
                tableId >= _tables.Length ||
                _tables[tableId].Keys == null)
            {
                throw new InvalidOperationException($"{UnknownTableError}: id={tableId}.");
            }

            return ref _tables[tableId];
        }

        private GraphLookupFieldInfo RequireField(int fieldId)
        {
            if (fieldId <= 0 ||
                fieldId >= _fields.Length ||
                _fields[fieldId].TableId <= 0)
            {
                throw new InvalidOperationException($"{UnknownFieldError}: id={fieldId}.");
            }

            return _fields[fieldId];
        }

        private void EnsureTableSlot(int id)
        {
            if (id < _tables.Length)
            {
                return;
            }

            Array.Resize(ref _tables, Math.Max(_tables.Length * 2, id + 1));
        }

        private void EnsureFieldSlot(int id)
        {
            if (id < _fields.Length)
            {
                return;
            }

            Array.Resize(ref _fields, Math.Max(_fields.Length * 2, id + 1));
        }

        private readonly struct TableSlot
        {
            public TableSlot(
                int[] keys,
                int[][] intColumns,
                float[][] floatColumns,
                GraphLookupColumnKind[] columnKinds,
                int[] columnStorageIndex,
                int[]? denseMap,
                int denseMin,
                int[]? openKeys,
                int[]? openRows)
            {
                Keys = keys;
                IntColumns = intColumns;
                FloatColumns = floatColumns;
                ColumnKinds = columnKinds;
                ColumnStorageIndex = columnStorageIndex;
                DenseMap = denseMap;
                DenseMin = denseMin;
                OpenKeys = openKeys;
                OpenRows = openRows;
            }

            public int[] Keys { get; }
            public int[][] IntColumns { get; }
            public float[][] FloatColumns { get; }
            public GraphLookupColumnKind[] ColumnKinds { get; }
            public int[] ColumnStorageIndex { get; }
            public int[]? DenseMap { get; }
            public int DenseMin { get; }
            public int[]? OpenKeys { get; }
            public int[]? OpenRows { get; }
        }
    }
}
