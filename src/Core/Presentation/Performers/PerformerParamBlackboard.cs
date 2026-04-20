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
        private readonly int[] _floatDefaultKeys;
        private readonly float[] _floatDefaultValues;
        private readonly int[] _intKeys;
        private readonly int[] _intValues;
        private readonly int[] _intDefaultKeys;
        private readonly int[] _intDefaultValues;
        private readonly int[] _vectorKeys;
        private readonly Vector4[] _vectorValues;
        private readonly int[] _vectorDefaultKeys;
        private readonly Vector4[] _vectorDefaultValues;
        private readonly int[] _parentHandles;
        private readonly int[] _floatCounts;
        private readonly int[] _floatDefaultCounts;
        private readonly int[] _intCounts;
        private readonly int[] _intDefaultCounts;
        private readonly int[] _vectorCounts;
        private readonly int[] _vectorDefaultCounts;
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

            _floatKeys = CreateKeys(handleCapacity, floatCapacityPerHandle);
            _floatValues = new float[handleCapacity * floatCapacityPerHandle];
            _floatDefaultKeys = CreateKeys(handleCapacity, floatCapacityPerHandle);
            _floatDefaultValues = new float[handleCapacity * floatCapacityPerHandle];

            _intKeys = CreateKeys(handleCapacity, intCapacityPerHandle);
            _intValues = new int[handleCapacity * intCapacityPerHandle];
            _intDefaultKeys = CreateKeys(handleCapacity, intCapacityPerHandle);
            _intDefaultValues = new int[handleCapacity * intCapacityPerHandle];

            _vectorKeys = CreateKeys(handleCapacity, vectorCapacityPerHandle);
            _vectorValues = new Vector4[handleCapacity * vectorCapacityPerHandle];
            _vectorDefaultKeys = CreateKeys(handleCapacity, vectorCapacityPerHandle);
            _vectorDefaultValues = new Vector4[handleCapacity * vectorCapacityPerHandle];

            _parentHandles = new int[handleCapacity];
            _floatCounts = new int[handleCapacity];
            _floatDefaultCounts = new int[handleCapacity];
            _intCounts = new int[handleCapacity];
            _intDefaultCounts = new int[handleCapacity];
            _vectorCounts = new int[handleCapacity];
            _vectorDefaultCounts = new int[handleCapacity];
            _floatCapacityPerHandle = floatCapacityPerHandle;
            _intCapacityPerHandle = intCapacityPerHandle;
            _vectorCapacityPerHandle = vectorCapacityPerHandle;

            Array.Fill(_parentHandles, -1);
        }

        public int HandleCapacity => _parentHandles.Length;

        public void SetParent(int handle, int parentHandle)
        {
            ValidateHandle(handle);
            if (parentHandle < -1 || parentHandle >= HandleCapacity)
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
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts, "Float");
        }

        public void SetFloatDefault(int handle, int paramKey, float value)
        {
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _floatDefaultKeys, _floatDefaultValues, _floatCapacityPerHandle, _floatDefaultCounts, "Float default");
        }

        public void SetInt(int handle, int paramKey, int value)
        {
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _intKeys, _intValues, _intCapacityPerHandle, _intCounts, "Int");
        }

        public void SetIntDefault(int handle, int paramKey, int value)
        {
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _intDefaultKeys, _intDefaultValues, _intCapacityPerHandle, _intDefaultCounts, "Int default");
        }

        public void SetBool(int handle, int paramKey, bool value)
        {
            SetInt(handle, paramKey, value ? 1 : 0);
        }

        public void SetVector(int handle, int paramKey, in Vector4 value)
        {
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts, "Vector");
        }

        public void SetVectorDefault(int handle, int paramKey, in Vector4 value)
        {
            ValidateHandleAndParam(handle, paramKey);
            SetLaneValue(handle, paramKey, value, _vectorDefaultKeys, _vectorDefaultValues, _vectorCapacityPerHandle, _vectorDefaultCounts, "Vector default");
        }

        public bool TryGetFloat(int handle, int paramKey, out float value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryGetLaneValue(handle, paramKey, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts, out value);
        }

        public bool TryResolveFloat(int handle, int paramKey, out float value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryResolveLaneValue(handle, paramKey, out value, TryGetFloatCurrent, TryGetFloatDefault);
        }

        public bool TryGetInt(int handle, int paramKey, out int value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryGetLaneValue(handle, paramKey, _intKeys, _intValues, _intCapacityPerHandle, _intCounts, out value);
        }

        public bool TryResolveInt(int handle, int paramKey, out int value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryResolveLaneValue(handle, paramKey, out value, TryGetIntCurrent, TryGetIntDefault);
        }

        public bool TryGetVector(int handle, int paramKey, out Vector4 value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryGetLaneValue(handle, paramKey, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts, out value);
        }

        public bool TryResolveVector(int handle, int paramKey, out Vector4 value)
        {
            ValidateHandleAndParam(handle, paramKey);
            return TryResolveLaneValue(handle, paramKey, out value, TryGetVectorCurrent, TryGetVectorDefault);
        }

        public float ResolveFloat(int handle, int paramKey, float defaultValue = 0f)
        {
            ValidateHandleAndParam(handle, paramKey);
            return ResolveLaneValue(handle, paramKey, defaultValue, TryGetFloatCurrent, TryGetFloatDefault);
        }

        public int ResolveInt(int handle, int paramKey, int defaultValue = 0)
        {
            ValidateHandleAndParam(handle, paramKey);
            return ResolveLaneValue(handle, paramKey, defaultValue, TryGetIntCurrent, TryGetIntDefault);
        }

        public Vector4 ResolveVector(int handle, int paramKey, Vector4 defaultValue)
        {
            ValidateHandleAndParam(handle, paramKey);
            return ResolveLaneValue(handle, paramKey, defaultValue, TryGetVectorCurrent, TryGetVectorDefault);
        }

        public void ClearAll(int handle)
        {
            ValidateHandle(handle);

            ClearLane(handle, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts);
            ClearLane(handle, _floatDefaultKeys, _floatDefaultValues, _floatCapacityPerHandle, _floatDefaultCounts);
            ClearLane(handle, _intKeys, _intValues, _intCapacityPerHandle, _intCounts);
            ClearLane(handle, _intDefaultKeys, _intDefaultValues, _intCapacityPerHandle, _intDefaultCounts);
            ClearLane(handle, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts);
            ClearLane(handle, _vectorDefaultKeys, _vectorDefaultValues, _vectorCapacityPerHandle, _vectorDefaultCounts);
            _parentHandles[handle] = -1;
        }

        public void ClearAll()
        {
            ClearKeys(_floatKeys);
            Array.Clear(_floatValues, 0, _floatValues.Length);
            ClearKeys(_floatDefaultKeys);
            Array.Clear(_floatDefaultValues, 0, _floatDefaultValues.Length);
            ClearKeys(_intKeys);
            Array.Clear(_intValues, 0, _intValues.Length);
            ClearKeys(_intDefaultKeys);
            Array.Clear(_intDefaultValues, 0, _intDefaultValues.Length);
            ClearKeys(_vectorKeys);
            Array.Clear(_vectorValues, 0, _vectorValues.Length);
            ClearKeys(_vectorDefaultKeys);
            Array.Clear(_vectorDefaultValues, 0, _vectorDefaultValues.Length);
            Array.Clear(_floatCounts, 0, _floatCounts.Length);
            Array.Clear(_floatDefaultCounts, 0, _floatDefaultCounts.Length);
            Array.Clear(_intCounts, 0, _intCounts.Length);
            Array.Clear(_intDefaultCounts, 0, _intDefaultCounts.Length);
            Array.Clear(_vectorCounts, 0, _vectorCounts.Length);
            Array.Clear(_vectorDefaultCounts, 0, _vectorDefaultCounts.Length);
            Array.Fill(_parentHandles, -1);
        }

        public void ClearFloat(int handle, int paramKey)
        {
            ValidateHandleAndParam(handle, paramKey);
            ClearLaneEntry(handle, paramKey, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts);
        }

        public void ClearInt(int handle, int paramKey)
        {
            ValidateHandleAndParam(handle, paramKey);
            ClearLaneEntry(handle, paramKey, _intKeys, _intValues, _intCapacityPerHandle, _intCounts);
        }

        public void ClearVector(int handle, int paramKey)
        {
            ValidateHandleAndParam(handle, paramKey);
            ClearLaneEntry(handle, paramKey, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts);
        }

        private bool TryGetFloatCurrent(int handle, int paramKey, out float value)
        {
            return TryGetLaneValue(handle, paramKey, _floatKeys, _floatValues, _floatCapacityPerHandle, _floatCounts, out value);
        }

        private bool TryGetFloatDefault(int handle, int paramKey, out float value)
        {
            return TryGetLaneValue(handle, paramKey, _floatDefaultKeys, _floatDefaultValues, _floatCapacityPerHandle, _floatDefaultCounts, out value);
        }

        private bool TryGetIntCurrent(int handle, int paramKey, out int value)
        {
            return TryGetLaneValue(handle, paramKey, _intKeys, _intValues, _intCapacityPerHandle, _intCounts, out value);
        }

        private bool TryGetIntDefault(int handle, int paramKey, out int value)
        {
            return TryGetLaneValue(handle, paramKey, _intDefaultKeys, _intDefaultValues, _intCapacityPerHandle, _intDefaultCounts, out value);
        }

        private bool TryGetVectorCurrent(int handle, int paramKey, out Vector4 value)
        {
            return TryGetLaneValue(handle, paramKey, _vectorKeys, _vectorValues, _vectorCapacityPerHandle, _vectorCounts, out value);
        }

        private bool TryGetVectorDefault(int handle, int paramKey, out Vector4 value)
        {
            return TryGetLaneValue(handle, paramKey, _vectorDefaultKeys, _vectorDefaultValues, _vectorCapacityPerHandle, _vectorDefaultCounts, out value);
        }

        private T ResolveLaneValue<T>(
            int handle,
            int paramKey,
            T defaultValue,
            TryGetDelegate<T> tryGetCurrent,
            TryGetDelegate<T> tryGetDefault)
        {
            return TryResolveLaneValue(handle, paramKey, out T value, tryGetCurrent, tryGetDefault)
                ? value
                : defaultValue;
        }

        private bool TryResolveLaneValue<T>(
            int handle,
            int paramKey,
            out T value,
            TryGetDelegate<T> tryGetCurrent,
            TryGetDelegate<T> tryGetDefault)
        {
            int current = handle;
            int remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (tryGetCurrent(current, paramKey, out value))
                {
                    return true;
                }

                current = _parentHandles[current];
            }

            current = handle;
            remainingDepth = HandleCapacity;
            while (current >= 0 && remainingDepth-- > 0)
            {
                if (tryGetDefault(current, paramKey, out value))
                {
                    return true;
                }

                current = _parentHandles[current];
            }

            value = default!;
            return false;
        }

        private delegate bool TryGetDelegate<T>(int handle, int paramKey, out T value);

        private static void SetLaneValue<TValue>(
            int handle,
            int paramKey,
            TValue value,
            int[] keys,
            TValue[] values,
            int laneCapacityPerHandle,
            int[] counts,
            string laneName)
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

                values[index] = value;
                return;
            }

            if (count >= laneCapacityPerHandle)
            {
                throw new InvalidOperationException($"{laneName} lane for performer handle {handle} is full.");
            }

            int slot = offset + count;
            keys[slot] = paramKey;
            values[slot] = value;
            counts[handle] = count + 1;
        }

        private static bool TryGetLaneValue<TValue>(
            int handle,
            int paramKey,
            int[] keys,
            TValue[] values,
            int laneCapacityPerHandle,
            int[] counts,
            out TValue value)
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

                value = values[index];
                return true;
            }

            value = default!;
            return false;
        }

        private static void ClearLane<TValue>(int handle, int[] keys, TValue[] values, int laneCapacityPerHandle, int[] counts)
        {
            int offset = handle * laneCapacityPerHandle;
            int count = counts[handle];
            for (int i = 0; i < count; i++)
            {
                keys[offset + i] = -1;
                values[offset + i] = default!;
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

        private void ValidateHandleAndParam(int handle, int paramKey)
        {
            ValidateHandle(handle);
            ValidateParamKey(paramKey);
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

        private static int[] CreateKeys(int handleCapacity, int laneCapacityPerHandle)
        {
            var keys = new int[handleCapacity * laneCapacityPerHandle];
            ClearKeys(keys);
            return keys;
        }

        private static void ClearKeys(int[] keys)
        {
            Array.Fill(keys, -1);
        }
    }
}
