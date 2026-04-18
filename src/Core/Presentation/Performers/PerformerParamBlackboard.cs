using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Performers
{
    public enum ParamLane : byte
    {
        Float = 0,
        Int = 1,
        Vector = 2,
    }

    public sealed class PerformerParamBlackboard
    {
        private readonly int[] _floatKeys;
        private readonly float[] _floatValues;
        private readonly int[] _intKeys;
        private readonly int[] _intValues;
        private readonly int[] _vectorKeys;
        private readonly Vector4[] _vectorValues;
        private readonly int[] _parentHandles;
        private readonly int[] _floatCounts;
        private readonly int[] _intCounts;
        private readonly int[] _vectorCounts;
        private readonly int _floatCapacityPerHandle;
        private readonly int _intCapacityPerHandle;
        private readonly int _vectorCapacityPerHandle;

        public PerformerParamBlackboard(
            int handleCapacity = 256,
            int floatCapacityPerHandle = 16,
            int intCapacityPerHandle = 16,
            int vectorCapacityPerHandle = 8)
        {
            if (handleCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(handleCapacity), "Handle capacity must be positive.");
            }

            if (floatCapacityPerHandle <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(floatCapacityPerHandle), "Float lane capacity must be positive.");
            }

            if (intCapacityPerHandle <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intCapacityPerHandle), "Int lane capacity must be positive.");
            }

            if (vectorCapacityPerHandle <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vectorCapacityPerHandle), "Vector lane capacity must be positive.");
            }

            _floatKeys = new int[handleCapacity * floatCapacityPerHandle];
            _floatValues = new float[handleCapacity * floatCapacityPerHandle];
            _intKeys = new int[handleCapacity * intCapacityPerHandle];
            _intValues = new int[handleCapacity * intCapacityPerHandle];
            _vectorKeys = new int[handleCapacity * vectorCapacityPerHandle];
            _vectorValues = new Vector4[handleCapacity * vectorCapacityPerHandle];
            _parentHandles = new int[handleCapacity];
            _floatCounts = new int[handleCapacity];
            _intCounts = new int[handleCapacity];
            _vectorCounts = new int[handleCapacity];
            _floatCapacityPerHandle = floatCapacityPerHandle;
            _intCapacityPerHandle = intCapacityPerHandle;
            _vectorCapacityPerHandle = vectorCapacityPerHandle;

            Array.Fill(_floatKeys, -1);
            Array.Fill(_intKeys, -1);
            Array.Fill(_vectorKeys, -1);
            Array.Fill(_parentHandles, -1);
        }

        public int HandleCapacity => _parentHandles.Length;

        public void SetParent(int handle, int parentHandle)
        {
            ValidateHandle(handle);
            if (parentHandle >= HandleCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(parentHandle), "Parent handle is outside blackboard capacity.");
            }

            _parentHandles[handle] = parentHandle;
        }

        public int GetParent(int handle)
        {
            ValidateHandle(handle);
            return _parentHandles[handle];
        }

        public void SetFloat(int handle, int paramKey, float value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _floatCapacityPerHandle;
            int count = _floatCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_floatKeys[index] != paramKey)
                {
                    continue;
                }

                _floatValues[index] = value;
                return;
            }

            if (count >= _floatCapacityPerHandle)
            {
                throw new InvalidOperationException($"Float lane for performer handle {handle} is full.");
            }

            int slot = offset + count;
            _floatKeys[slot] = paramKey;
            _floatValues[slot] = value;
            _floatCounts[handle] = count + 1;
        }

        public void SetInt(int handle, int paramKey, int value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _intCapacityPerHandle;
            int count = _intCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_intKeys[index] != paramKey)
                {
                    continue;
                }

                _intValues[index] = value;
                return;
            }

            if (count >= _intCapacityPerHandle)
            {
                throw new InvalidOperationException($"Int lane for performer handle {handle} is full.");
            }

            int slot = offset + count;
            _intKeys[slot] = paramKey;
            _intValues[slot] = value;
            _intCounts[handle] = count + 1;
        }

        public void SetBool(int handle, int paramKey, bool value)
        {
            SetInt(handle, paramKey, value ? 1 : 0);
        }

        public void SetVector(int handle, int paramKey, in Vector4 value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _vectorCapacityPerHandle;
            int count = _vectorCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_vectorKeys[index] != paramKey)
                {
                    continue;
                }

                _vectorValues[index] = value;
                return;
            }

            if (count >= _vectorCapacityPerHandle)
            {
                throw new InvalidOperationException($"Vector lane for performer handle {handle} is full.");
            }

            int slot = offset + count;
            _vectorKeys[slot] = paramKey;
            _vectorValues[slot] = value;
            _vectorCounts[handle] = count + 1;
        }

        public bool TryGetFloat(int handle, int paramKey, out float value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _floatCapacityPerHandle;
            int count = _floatCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_floatKeys[index] != paramKey)
                {
                    continue;
                }

                value = _floatValues[index];
                return true;
            }

            value = 0f;
            return false;
        }

        public bool TryResolveFloat(int handle, int paramKey, out float value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int current = handle;
            int remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (TryGetFloat(current, paramKey, out value))
                {
                    return true;
                }

                current = _parentHandles[current];
            }

            value = 0f;
            return false;
        }

        public bool TryGetInt(int handle, int paramKey, out int value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _intCapacityPerHandle;
            int count = _intCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_intKeys[index] != paramKey)
                {
                    continue;
                }

                value = _intValues[index];
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryResolveInt(int handle, int paramKey, out int value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int current = handle;
            int remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (TryGetInt(current, paramKey, out value))
                {
                    return true;
                }

                current = _parentHandles[current];
            }

            value = 0;
            return false;
        }

        public bool TryGetVector(int handle, int paramKey, out Vector4 value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int offset = handle * _vectorCapacityPerHandle;
            int count = _vectorCounts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (_vectorKeys[index] != paramKey)
                {
                    continue;
                }

                value = _vectorValues[index];
                return true;
            }

            value = Vector4.Zero;
            return false;
        }

        public bool TryResolveVector(int handle, int paramKey, out Vector4 value)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int current = handle;
            int remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (TryGetVector(current, paramKey, out value))
                {
                    return true;
                }

                current = _parentHandles[current];
            }

            value = Vector4.Zero;
            return false;
        }

        public float ResolveFloat(int handle, int paramKey, float defaultValue = 0f)
        {
            return ResolveValue(
                handle,
                paramKey,
                defaultValue,
                TryGetFloat);
        }

        public int ResolveInt(int handle, int paramKey, int defaultValue = 0)
        {
            return ResolveValue(
                handle,
                paramKey,
                defaultValue,
                TryGetInt);
        }

        public Vector4 ResolveVector(int handle, int paramKey, Vector4 defaultValue)
        {
            return ResolveValue(
                handle,
                paramKey,
                defaultValue,
                TryGetVector);
        }

        public void ClearAll(int handle)
        {
            ValidateHandle(handle);

            ClearLane(handle, _floatKeys, _floatCapacityPerHandle, _floatCounts);
            ClearLane(handle, _intKeys, _intCapacityPerHandle, _intCounts);
            ClearLane(handle, _vectorKeys, _vectorCapacityPerHandle, _vectorCounts);
            _parentHandles[handle] = -1;
        }

        public void ClearAll()
        {
            Array.Fill(_floatKeys, -1);
            Array.Fill(_intKeys, -1);
            Array.Fill(_vectorKeys, -1);
            Array.Clear(_floatValues, 0, _floatValues.Length);
            Array.Clear(_intValues, 0, _intValues.Length);
            Array.Clear(_vectorValues, 0, _vectorValues.Length);
            Array.Clear(_floatCounts, 0, _floatCounts.Length);
            Array.Clear(_intCounts, 0, _intCounts.Length);
            Array.Clear(_vectorCounts, 0, _vectorCounts.Length);
            Array.Fill(_parentHandles, -1);
        }

        public void ClearFloat(int handle, int paramKey)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);
            ClearLaneEntry(handle, paramKey, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts);
        }

        public void ClearInt(int handle, int paramKey)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);
            ClearLaneEntry(handle, paramKey, _intKeys, _intValues, _intCapacityPerHandle, _intCounts);
        }

        public void ClearVector(int handle, int paramKey)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);
            ClearLaneEntry(handle, paramKey, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts);
        }

        private T ResolveValue<T>(int handle, int paramKey, T defaultValue, TryResolveValue<T> tryResolve)
        {
            ValidateParamKey(paramKey);
            ValidateHandle(handle);

            int current = handle;
            int remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (tryResolve(current, paramKey, out T value))
                {
                    return value;
                }

                current = _parentHandles[current];
            }

            return defaultValue;
        }

        private delegate bool TryResolveValue<T>(int handle, int paramKey, out T value);

        private static void ClearLane(int handle, int[] keys, int laneCapacityPerHandle, int[] counts)
        {
            int offset = handle * laneCapacityPerHandle;
            int count = counts[handle];
            for (int i = 0; i < count; i++)
            {
                keys[offset + i] = -1;
            }

            counts[handle] = 0;
        }

        private static void ClearLaneEntry<TValue>(int handle, int paramKey, int[] keys, TValue[] values, int laneCapacityPerHandle, int[] counts)
        {
            int offset = handle * laneCapacityPerHandle;
            int count = counts[handle];
            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                if (keys[index] != paramKey)
                {
                    continue;
                }

                int lastIndex = offset + count - 1;
                keys[index] = keys[lastIndex];
                values[index] = values[lastIndex];
                keys[lastIndex] = -1;
                values[lastIndex] = default!;
                counts[handle] = count - 1;
                return;
            }
        }

        private void ValidateHandle(int handle)
        {
            if ((uint)handle >= (uint)HandleCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), $"Handle must be in [0, {HandleCapacity - 1}].");
            }
        }

        private static void ValidateParamKey(int paramKey)
        {
            if (paramKey < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paramKey), "Parameter key must be non-negative.");
            }
        }
    }
}
