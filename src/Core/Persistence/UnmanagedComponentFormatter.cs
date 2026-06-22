using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class UnmanagedComponentFormatter<T> : IMessagePackFormatter<T>
        , ILudotsPersistenceComponentFormatter
        where T : unmanaged
    {
        public Type ComponentType => typeof(T);

        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
        {
            ReadOnlySpan<T> valueSpan = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(valueSpan);
            writer.Write(bytes);
        }

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            ReadOnlySequence<byte>? sequence = reader.ReadBytes();
            if (sequence is null)
            {
                throw new InvalidOperationException($"Component '{typeof(T).FullName}' expected a binary payload.");
            }

            int expectedSize = Unsafe.SizeOf<T>();
            if (sequence.Value.Length != expectedSize)
            {
                throw new InvalidOperationException(
                    $"Component '{typeof(T).FullName}' expected {expectedSize} bytes, got {sequence.Value.Length}.");
            }

            T value = default;
            Span<T> valueSpan = MemoryMarshal.CreateSpan(ref value, 1);
            sequence.Value.CopyTo(MemoryMarshal.AsBytes(valueSpan));
            return value;
        }
    }
}
