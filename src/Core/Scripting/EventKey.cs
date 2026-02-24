using System;

namespace Ludots.Core.Scripting
{
    public readonly struct EventKey : IEquatable<EventKey>
    {
        public string Value { get; }

        public EventKey(string value)
        {
            Value = value?.Trim() ?? "";
        }

        public bool Equals(EventKey other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EventKey other && Equals(other);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? "");
        public override string ToString() => Value ?? "";

        public static bool operator ==(EventKey left, EventKey right) => left.Equals(right);
        public static bool operator !=(EventKey left, EventKey right) => !left.Equals(right);
    }
}
