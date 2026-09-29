using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipRoleBindings
    {
        private static readonly RelationshipRole[] RequiredRoles =
        {
            RelationshipRole.Ownership,
            RelationshipRole.Membership,
            RelationshipRole.ControlGrant,
        };

        public RelationshipRoleBindings(int ownershipTypeId, int membershipTypeId, int controlGrantTypeId)
        {
            if (ownershipTypeId < 0) throw new ArgumentOutOfRangeException(nameof(ownershipTypeId));
            if (membershipTypeId < 0) throw new ArgumentOutOfRangeException(nameof(membershipTypeId));
            if (controlGrantTypeId < 0) throw new ArgumentOutOfRangeException(nameof(controlGrantTypeId));
            if (ownershipTypeId == membershipTypeId || ownershipTypeId == controlGrantTypeId || membershipTypeId == controlGrantTypeId)
            {
                throw new ArgumentException(
                    $"Relationship roles must bind distinct types (ownership={ownershipTypeId}, membership={membershipTypeId}, controlGrant={controlGrantTypeId}).");
            }

            OwnershipTypeId = ownershipTypeId;
            MembershipTypeId = membershipTypeId;
            ControlGrantTypeId = controlGrantTypeId;
        }

        public int OwnershipTypeId { get; }
        public int MembershipTypeId { get; }
        public int ControlGrantTypeId { get; }

        public static RelationshipRoleBindings Resolve(RelationshipCatalogConfig catalog, RelationshipTypeRegistry types)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);

            var bound = new Dictionary<RelationshipRole, string>();
            for (int i = 0; i < catalog.Types.Count; i++)
            {
                RelationshipTypeConfig type = catalog.Types[i];
                if (type.Role == RelationshipRole.None)
                {
                    continue;
                }

                if (!Enum.IsDefined(type.Role))
                {
                    throw new InvalidOperationException(
                        $"Relationship type '{type.Id}' declares unknown role value {(int)type.Role}.");
                }

                if (bound.TryGetValue(type.Role, out string? existing))
                {
                    throw new InvalidOperationException(
                        $"Relationship role '{type.Role}' is bound to both '{existing}' and '{type.Id}'; each role binds exactly one type.");
                }

                if (type.IsSymmetric)
                {
                    throw new InvalidOperationException(
                        $"Relationship type '{type.Id}' carries role '{type.Role}' and must not be symmetric: the role reads source and target as different ends.");
                }

                bound.Add(type.Role, type.Id);
            }

            for (int i = 0; i < RequiredRoles.Length; i++)
            {
                if (!bound.ContainsKey(RequiredRoles[i]))
                {
                    throw new InvalidOperationException(
                        $"Relationships/catalog.json must bind role '{RequiredRoles[i]}' to exactly one type (\"role\": \"{RequiredRoles[i]}\").");
                }
            }

            return new RelationshipRoleBindings(
                types.GetId(bound[RelationshipRole.Ownership]),
                types.GetId(bound[RelationshipRole.Membership]),
                types.GetId(bound[RelationshipRole.ControlGrant]));
        }
    }
}
