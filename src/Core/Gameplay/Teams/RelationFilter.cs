using System;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Gameplay.Teams
{
    /// <summary>
    /// Compiled targeting relation requirement: either no requirement (<see cref="All"/>) or one
    /// data-declared relationship type that must link the source team representative to the target
    /// team representative. Core never interprets relationship type names.
    /// </summary>
    public readonly struct RelationFilter : IEquatable<RelationFilter>
    {
        public const string AllKeyword = "All";

        private readonly int _relationTypeIdPlusOne;

        private RelationFilter(int relationTypeIdPlusOne)
        {
            _relationTypeIdPlusOne = relationTypeIdPlusOne;
        }

        public static RelationFilter All => default;

        public bool IsAll => _relationTypeIdPlusOne == 0;

        public int RelationTypeId => _relationTypeIdPlusOne == 0
            ? throw new InvalidOperationException("RelationFilter.All carries no relationship type.")
            : _relationTypeIdPlusOne - 1;

        public static RelationFilter Require(int relationTypeId)
        {
            if (relationTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(relationTypeId), relationTypeId, "Relation filter requires a registered relationship type id.");
            }

            return new RelationFilter(relationTypeId + 1);
        }

        /// <summary>
        /// Load-time compilation of an authored filter: the literal <see cref="AllKeyword"/> or the name of a
        /// relationship type registered by the relationship catalog.
        /// </summary>
        public static RelationFilter Parse(string authored, RelationshipTypeRegistry? types)
        {
            if (string.IsNullOrEmpty(authored))
            {
                throw new ArgumentException("Relation filter must be explicitly authored.", nameof(authored));
            }

            if (!string.Equals(authored, authored.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Relation filter '{authored}' must not contain leading or trailing whitespace.");
            }

            if (string.Equals(authored, AllKeyword, StringComparison.Ordinal))
            {
                return All;
            }

            if (types == null)
            {
                throw new InvalidOperationException(
                    $"Relation filter '{authored}' names a relationship type, but no relationship type registry was provided.");
            }

            if (!types.TryGetId(authored, out int typeId))
            {
                throw new InvalidOperationException(
                    $"Relation filter '{authored}' is neither '{AllKeyword}' nor a relationship type declared in the relationship catalog.");
            }

            return Require(typeId);
        }

        public bool Equals(RelationFilter other) => _relationTypeIdPlusOne == other._relationTypeIdPlusOne;

        public override bool Equals(object? obj) => obj is RelationFilter other && Equals(other);

        public override int GetHashCode() => _relationTypeIdPlusOne;

        public static bool operator ==(RelationFilter left, RelationFilter right) => left.Equals(right);

        public static bool operator !=(RelationFilter left, RelationFilter right) => !left.Equals(right);

        public override string ToString() => IsAll ? AllKeyword : $"Relation#{RelationTypeId}";
    }
}
