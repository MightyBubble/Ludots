using System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    /// <summary>
    /// Region volume component formatters (#1461): identity/shape, tag filter, and
    /// emission contract persisted as flat primitive arrays (fixed-point via raw
    /// values, polygon vertices and payload entries as nested arrays).
    /// </summary>
    public sealed class RegionVolumeCmFormatter : IMessagePackFormatter<RegionVolumeCm>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(RegionVolumeCm);

        public void Serialize(ref MessagePackWriter writer, RegionVolumeCm value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(8);
            writer.Write(value.VolumeKey ?? string.Empty);
            writer.Write((int)value.Shape.Kind);
            writer.Write(value.Shape.Kind == RegionVolumeShapeKind.Circle ? value.Shape.Radius.RawValue : 0L);
            writer.Write(value.Shape.Kind == RegionVolumeShapeKind.Rect ? value.Shape.HalfWidth.RawValue : 0L);
            writer.Write(value.Shape.Kind == RegionVolumeShapeKind.Rect ? value.Shape.HalfHeight.RawValue : 0L);
            if (value.Shape.PolygonPoints is { } points)
            {
                writer.WriteArrayHeader(points.Length * 2);
                for (int i = 0; i < points.Length; i++)
                {
                    writer.Write(points[i].X.RawValue);
                    writer.Write(points[i].Y.RawValue);
                }
            }
            else
            {
                writer.WriteNil();
            }
            writer.WriteArrayHeader(5);
            writer.Write(value.Shape.SegmentA.X.RawValue);
            writer.Write(value.Shape.SegmentA.Y.RawValue);
            writer.Write(value.Shape.SegmentB.X.RawValue);
            writer.Write(value.Shape.SegmentB.Y.RawValue);
            writer.Write(value.Shape.HalfThickness.RawValue);
        }

        public RegionVolumeCm Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadArrayHeader();
            if (count != 8)
            {
                throw new MessagePackSerializationException($"RegionVolumeCm payload expects 8 entries, got {count}.");
            }

            var volume = new RegionVolumeCm
            {
                VolumeKey = reader.ReadString() ?? string.Empty,
                Shape = new RegionVolumeShape { Kind = (RegionVolumeShapeKind)reader.ReadInt32() },
            };
            volume.Shape.Radius = Fix64.FromRaw(reader.ReadInt64());
            volume.Shape.HalfWidth = Fix64.FromRaw(reader.ReadInt64());
            volume.Shape.HalfHeight = Fix64.FromRaw(reader.ReadInt64());
            if (reader.TryReadNil())
            {
                volume.Shape.PolygonPoints = null;
            }
            else
            {
                int flat = reader.ReadArrayHeader();
                var points = new Fix64Vec2[flat / 2];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = new Fix64Vec2(Fix64.FromRaw(reader.ReadInt64()), Fix64.FromRaw(reader.ReadInt64()));
                }

                volume.Shape.PolygonPoints = points;
            }

            int segmentCount = reader.ReadArrayHeader();
            if (segmentCount != 5)
            {
                throw new MessagePackSerializationException($"RegionVolumeCm segment payload expects 5 entries, got {segmentCount}.");
            }

            volume.Shape.SegmentA = new Fix64Vec2(Fix64.FromRaw(reader.ReadInt64()), Fix64.FromRaw(reader.ReadInt64()));
            volume.Shape.SegmentB = new Fix64Vec2(Fix64.FromRaw(reader.ReadInt64()), Fix64.FromRaw(reader.ReadInt64()));
            volume.Shape.HalfThickness = Fix64.FromRaw(reader.ReadInt64());
            return volume;
        }
    }

    public sealed class RegionVolumeTagFilterCmFormatter : IMessagePackFormatter<RegionVolumeTagFilterCm>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(RegionVolumeTagFilterCm);

        public unsafe void Serialize(ref MessagePackWriter writer, RegionVolumeTagFilterCm value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(4);
            for (int i = 0; i < 4; i++)
            {
                writer.Write(value.Filter.Bits[i]);
            }
        }

        public unsafe RegionVolumeTagFilterCm Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadArrayHeader();
            if (count != 4)
            {
                throw new MessagePackSerializationException($"RegionVolumeTagFilterCm payload expects 4 entries, got {count}.");
            }

            var filter = default(GameplayTagContainer);
            for (int i = 0; i < 4; i++)
            {
                filter.Bits[i] = reader.ReadUInt64();
            }

            return new RegionVolumeTagFilterCm { Filter = filter };
        }
    }

    public sealed class RegionVolumeEmissionCmFormatter : IMessagePackFormatter<RegionVolumeEmissionCm>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(RegionVolumeEmissionCm);

        public void Serialize(ref MessagePackWriter writer, RegionVolumeEmissionCm value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.EnterEvent.Value ?? string.Empty);
            writer.Write(value.ExitEvent.Value ?? string.Empty);
            if (value.Payload is { } payload)
            {
                writer.WriteArrayHeader(payload.Length);
                for (int i = 0; i < payload.Length; i++)
                {
                    writer.WriteArrayHeader(5);
                    writer.Write(payload[i].Key ?? string.Empty);
                    writer.Write((int)payload[i].Type);
                    writer.Write(payload[i].IntValue);
                    writer.Write(payload[i].FloatValue);
                    writer.Write(payload[i].StringValue ?? string.Empty);
                }
            }
            else
            {
                writer.WriteNil();
            }
        }

        public RegionVolumeEmissionCm Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadArrayHeader();
            if (count != 3)
            {
                throw new MessagePackSerializationException($"RegionVolumeEmissionCm payload expects 3 entries, got {count}.");
            }

            var emission = new RegionVolumeEmissionCm
            {
                EnterEvent = new EventKey(reader.ReadString() ?? string.Empty),
                ExitEvent = new EventKey(reader.ReadString() ?? string.Empty),
            };
            if (reader.TryReadNil())
            {
                emission.Payload = null;
                return emission;
            }

            int payloadCount = reader.ReadArrayHeader();
            var payload = new RegionVolumePayloadEntry[payloadCount];
            for (int i = 0; i < payloadCount; i++)
            {
                int entryCount = reader.ReadArrayHeader();
                if (entryCount != 5)
                {
                    throw new MessagePackSerializationException($"RegionVolumePayloadEntry payload expects 5 entries, got {entryCount}.");
                }

                payload[i] = new RegionVolumePayloadEntry
                {
                    Key = reader.ReadString() ?? string.Empty,
                    Type = (RegionVolumePayloadValueType)reader.ReadInt32(),
                    IntValue = reader.ReadInt32(),
                    FloatValue = reader.ReadSingle(),
                    StringValue = reader.ReadString() ?? string.Empty,
                };
            }

            emission.Payload = payload;
            return emission;
        }
    }
}
