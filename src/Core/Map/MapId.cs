using System;

namespace Ludots.Core.Map
{
    public readonly struct MapId : IEquatable<MapId>
    {
        public string Value { get; }

        public MapId(string value)
        {
            Value = value?.Trim() ?? "";
        }

        public bool Equals(MapId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is MapId other && Equals(other);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? "");
        public override string ToString() => Value ?? "";

        public static bool operator ==(MapId left, MapId right) => left.Equals(right);
        public static bool operator !=(MapId left, MapId right) => !left.Equals(right);
    }
}
