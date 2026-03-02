using System;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Unique identifier for a Board within a MapSession.
    /// Case-insensitive string-based identity.
    /// </summary>
    public readonly struct BoardId : IEquatable<BoardId>
    {
        public string Value { get; }

        public BoardId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool Equals(BoardId other) =>
            string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => obj is BoardId other && Equals(other);

        public override int GetHashCode() =>
            Value != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Value) : 0;

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(BoardId left, BoardId right) => left.Equals(right);
        public static bool operator !=(BoardId left, BoardId right) => !left.Equals(right);
    }
}
