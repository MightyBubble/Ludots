using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Fields
{
    public interface IFieldValueCodec<T>
        where T : struct
    {
        int ChannelCount { get; }
        FieldChannelKind ChannelKind { get; }
        Array[] CreateChannels(int cellCount, T defaultValue);
        T Read(Array[] channels, int localIndex);
        void Write(Array[] channels, int localIndex, T value);
        bool ValueEquals(T left, T right);
        ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex);
    }

    public static class FieldValueCodec<T>
        where T : struct
    {
        public static readonly IFieldValueCodec<T> Instance = Create();

        private static IFieldValueCodec<T> Create()
        {
            Type type = typeof(T);
            if (type == typeof(float))
            {
                return (IFieldValueCodec<T>)(object)new FloatFieldValueCodec();
            }

            if (type == typeof(Vector2))
            {
                return (IFieldValueCodec<T>)(object)new Vector2FieldValueCodec();
            }

            if (type == typeof(Vector3))
            {
                return (IFieldValueCodec<T>)(object)new Vector3FieldValueCodec();
            }

            if (type == typeof(Vector4))
            {
                return (IFieldValueCodec<T>)(object)new Vector4FieldValueCodec();
            }

            return new StructFieldValueCodec<T>();
        }
    }

    internal sealed class StructFieldValueCodec<T> : IFieldValueCodec<T>
        where T : struct
    {
        private readonly EqualityComparer<T> _comparer = EqualityComparer<T>.Default;

        public int ChannelCount => 1;
        public FieldChannelKind ChannelKind => FieldChannelKind.Struct;

        public Array[] CreateChannels(int cellCount, T defaultValue)
        {
            var values = new T[cellCount];
            if (!_comparer.Equals(defaultValue, default))
            {
                Array.Fill(values, defaultValue);
            }

            return new Array[] { values };
        }

        public T Read(Array[] channels, int localIndex) => ((T[])channels[0])[localIndex];

        public void Write(Array[] channels, int localIndex, T value)
        {
            ((T[])channels[0])[localIndex] = value;
        }

        public bool ValueEquals(T left, T right) => _comparer.Equals(left, right);

        public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
        {
            throw new InvalidOperationException($"{typeof(T).Name} field values do not expose float channels.");
        }
    }

    internal sealed class FloatFieldValueCodec : IFieldValueCodec<float>
    {
        public int ChannelCount => 1;
        public FieldChannelKind ChannelKind => FieldChannelKind.Float32;

        public Array[] CreateChannels(int cellCount, float defaultValue)
        {
            var values = new float[cellCount];
            if (defaultValue != 0f)
            {
                Array.Fill(values, defaultValue);
            }

            return new Array[] { values };
        }

        public float Read(Array[] channels, int localIndex) => ((float[])channels[0])[localIndex];

        public void Write(Array[] channels, int localIndex, float value)
        {
            ((float[])channels[0])[localIndex] = value;
        }

        public bool ValueEquals(float left, float right) => left.Equals(right);

        public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
        {
            ValidateChannel(channelIndex, ChannelCount);
            return (float[])channels[channelIndex];
        }

        public Span<float> GetMutableFloatChannel(Array[] channels, int channelIndex)
        {
            ValidateChannel(channelIndex, ChannelCount);
            return ((float[])channels[channelIndex]).AsSpan();
        }

        internal static void ValidateChannel(int channelIndex, int channelCount)
        {
            if ((uint)channelIndex >= (uint)channelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            }
        }
    }

    internal sealed class Vector2FieldValueCodec : IFieldValueCodec<Vector2>
    {
        public int ChannelCount => 2;
        public FieldChannelKind ChannelKind => FieldChannelKind.Float32;

        public Array[] CreateChannels(int cellCount, Vector2 defaultValue)
        {
            var x = new float[cellCount];
            var y = new float[cellCount];
            if (defaultValue.X != 0f)
            {
                Array.Fill(x, defaultValue.X);
            }

            if (defaultValue.Y != 0f)
            {
                Array.Fill(y, defaultValue.Y);
            }

            return new Array[] { x, y };
        }

        public Vector2 Read(Array[] channels, int localIndex)
        {
            return new Vector2(
                ((float[])channels[0])[localIndex],
                ((float[])channels[1])[localIndex]);
        }

        public void Write(Array[] channels, int localIndex, Vector2 value)
        {
            ((float[])channels[0])[localIndex] = value.X;
            ((float[])channels[1])[localIndex] = value.Y;
        }

        public bool ValueEquals(Vector2 left, Vector2 right) => left.Equals(right);

        public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
        {
            FloatFieldValueCodec.ValidateChannel(channelIndex, ChannelCount);
            return (float[])channels[channelIndex];
        }
    }

    internal sealed class Vector3FieldValueCodec : IFieldValueCodec<Vector3>
    {
        public int ChannelCount => 3;
        public FieldChannelKind ChannelKind => FieldChannelKind.Float32;

        public Array[] CreateChannels(int cellCount, Vector3 defaultValue)
        {
            var x = new float[cellCount];
            var y = new float[cellCount];
            var z = new float[cellCount];
            if (defaultValue.X != 0f) Array.Fill(x, defaultValue.X);
            if (defaultValue.Y != 0f) Array.Fill(y, defaultValue.Y);
            if (defaultValue.Z != 0f) Array.Fill(z, defaultValue.Z);
            return new Array[] { x, y, z };
        }

        public Vector3 Read(Array[] channels, int localIndex)
        {
            return new Vector3(
                ((float[])channels[0])[localIndex],
                ((float[])channels[1])[localIndex],
                ((float[])channels[2])[localIndex]);
        }

        public void Write(Array[] channels, int localIndex, Vector3 value)
        {
            ((float[])channels[0])[localIndex] = value.X;
            ((float[])channels[1])[localIndex] = value.Y;
            ((float[])channels[2])[localIndex] = value.Z;
        }

        public bool ValueEquals(Vector3 left, Vector3 right) => left.Equals(right);

        public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
        {
            FloatFieldValueCodec.ValidateChannel(channelIndex, ChannelCount);
            return (float[])channels[channelIndex];
        }
    }

    internal sealed class Vector4FieldValueCodec : IFieldValueCodec<Vector4>
    {
        public int ChannelCount => 4;
        public FieldChannelKind ChannelKind => FieldChannelKind.Float32;

        public Array[] CreateChannels(int cellCount, Vector4 defaultValue)
        {
            var x = new float[cellCount];
            var y = new float[cellCount];
            var z = new float[cellCount];
            var w = new float[cellCount];
            if (defaultValue.X != 0f) Array.Fill(x, defaultValue.X);
            if (defaultValue.Y != 0f) Array.Fill(y, defaultValue.Y);
            if (defaultValue.Z != 0f) Array.Fill(z, defaultValue.Z);
            if (defaultValue.W != 0f) Array.Fill(w, defaultValue.W);
            return new Array[] { x, y, z, w };
        }

        public Vector4 Read(Array[] channels, int localIndex)
        {
            return new Vector4(
                ((float[])channels[0])[localIndex],
                ((float[])channels[1])[localIndex],
                ((float[])channels[2])[localIndex],
                ((float[])channels[3])[localIndex]);
        }

        public void Write(Array[] channels, int localIndex, Vector4 value)
        {
            ((float[])channels[0])[localIndex] = value.X;
            ((float[])channels[1])[localIndex] = value.Y;
            ((float[])channels[2])[localIndex] = value.Z;
            ((float[])channels[3])[localIndex] = value.W;
        }

        public bool ValueEquals(Vector4 left, Vector4 right) => left.Equals(right);

        public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
        {
            FloatFieldValueCodec.ValidateChannel(channelIndex, ChannelCount);
            return (float[])channels[channelIndex];
        }
    }
}
