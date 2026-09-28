using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class RelationshipRoleOwnershipTests
    {
        [Test]
        public void Resolve_BindsRolesToTheTypesTheCatalogDeclares_WhateverTheirNames()
        {
            var catalog = new RelationshipCatalogConfig
            {
                Types =
                {
                    new RelationshipTypeConfig { Id = "Holds", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "Commands", Role = RelationshipRole.ControlGrant },
                    new RelationshipTypeConfig { Id = "SwornTo", Role = RelationshipRole.Membership },
                    new RelationshipTypeConfig { Id = "Allied", IsSymmetric = true },
                },
            };
            var types = RegisterTypes(catalog);

            RelationshipRoleBindings roles = RelationshipRoleBindings.Resolve(catalog, types);

            Assert.That(roles.OwnershipTypeId, Is.EqualTo(types.GetId("Holds")));
            Assert.That(roles.ControlGrantTypeId, Is.EqualTo(types.GetId("Commands")));
            Assert.That(roles.MembershipTypeId, Is.EqualTo(types.GetId("SwornTo")));
        }

        [Test]
        public void Resolve_FailsWhenARequiredRoleIsMissing()
        {
            var catalog = new RelationshipCatalogConfig
            {
                Types =
                {
                    new RelationshipTypeConfig { Id = "Owns", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "MemberOf", Role = RelationshipRole.Membership },
                    new RelationshipTypeConfig { Id = "Controls" },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() => RelationshipRoleBindings.Resolve(catalog, RegisterTypes(catalog)));
            Assert.That(ex!.Message, Does.Contain("ControlGrant"));
        }

        [Test]
        public void Resolve_FailsWhenTwoTypesClaimTheSameRole()
        {
            var catalog = new RelationshipCatalogConfig
            {
                Types =
                {
                    new RelationshipTypeConfig { Id = "Owns", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "Holds", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "MemberOf", Role = RelationshipRole.Membership },
                    new RelationshipTypeConfig { Id = "Controls", Role = RelationshipRole.ControlGrant },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() => RelationshipRoleBindings.Resolve(catalog, RegisterTypes(catalog)));
            Assert.That(ex!.Message, Does.Contain("'Owns'").And.Contain("'Holds'"));
        }

        [Test]
        public void Resolve_FailsWhenARoleTypeIsSymmetric()
        {
            var catalog = new RelationshipCatalogConfig
            {
                Types =
                {
                    new RelationshipTypeConfig { Id = "Owns", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "MemberOf", Role = RelationshipRole.Membership, IsSymmetric = true },
                    new RelationshipTypeConfig { Id = "Controls", Role = RelationshipRole.ControlGrant },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() => RelationshipRoleBindings.Resolve(catalog, RegisterTypes(catalog)));
            Assert.That(ex!.Message, Does.Contain("'MemberOf'").And.Contain("symmetric"));
        }

        [Test]
        public void BareOwnershipLink_ReplacesThePreviousOwnerAndReprojectsTheWholeOwnedTree()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateBoundRuntime(world, out OwnershipResolver ownership, out RelationshipRoleBindings roles);

            Entity red = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity blue = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity castle = world.Create();
            Entity garrison = world.Create();
            Entity cannon = world.Create();
            relationships.EnsureLink(red, castle, roles.OwnershipTypeId);
            relationships.EnsureLink(castle, garrison, roles.OwnershipTypeId);
            relationships.EnsureLink(garrison, cannon, roles.OwnershipTypeId);
            Assert.That(cannon.Get<PlayerOwner>().PlayerId, Is.EqualTo(1));

            relationships.EnsureLink(blue, castle, roles.OwnershipTypeId);

            Span<Entity> owners = stackalloc Entity[4];
            Assert.That(relationships.CollectIncoming(castle, roles.OwnershipTypeId, owners), Is.EqualTo(1));
            Assert.That(owners[0], Is.EqualTo(blue));
            Assert.That(relationships.HasLink(red, castle, roles.OwnershipTypeId), Is.False);
            Assert.That(castle.Get<PlayerOwner>().PlayerId, Is.EqualTo(2));
            Assert.That(garrison.Get<PlayerOwner>().PlayerId, Is.EqualTo(2));
            Assert.That(cannon.Get<PlayerOwner>().PlayerId, Is.EqualTo(2));
            Assert.That(ownership.TryResolveRootOwner(cannon, out Entity root), Is.True);
            Assert.That(root, Is.EqualTo(blue));
        }

        [Test]
        public void RemovingAnOwnershipLink_ClearsPlayerOwnerFromTheWholeDetachedTree()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateBoundRuntime(world, out _, out RelationshipRoleBindings roles);

            Entity red = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity castle = world.Create();
            Entity garrison = world.Create();
            relationships.EnsureLink(red, castle, roles.OwnershipTypeId);
            relationships.EnsureLink(castle, garrison, roles.OwnershipTypeId);
            Assert.That(garrison.Has<PlayerOwner>(), Is.True);

            relationships.RemoveLink(red, castle, roles.OwnershipTypeId);

            Assert.That(castle.Has<PlayerOwner>(), Is.False);
            Assert.That(garrison.Has<PlayerOwner>(), Is.False);
            Assert.That(relationships.HasLink(castle, garrison, roles.OwnershipTypeId), Is.True);
        }

        [Test]
        public void RelinkingTheSameOwner_KeepsExactlyOneOwnershipEdge()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateBoundRuntime(world, out _, out RelationshipRoleBindings roles);

            Entity red = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity castle = world.Create();
            relationships.EnsureLink(red, castle, roles.OwnershipTypeId);
            relationships.EnsureLink(red, castle, roles.OwnershipTypeId);

            Span<Entity> owners = stackalloc Entity[4];
            Assert.That(relationships.CollectIncoming(castle, roles.OwnershipTypeId, owners), Is.EqualTo(1));
            Assert.That(castle.Get<PlayerOwner>().PlayerId, Is.EqualTo(1));
        }

        [Test]
        public void MembershipLink_ProjectsTeamFromTheRepresentativeTheRoleNames()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateBoundRuntime(world, out _, out RelationshipRoleBindings roles);

            Entity blueBanner = world.Create(new TeamIdentity { TeamId = 7 });
            Entity knight = world.Create();
            relationships.EnsureLink(knight, blueBanner, roles.MembershipTypeId);
            Assert.That(knight.Get<Team>().Id, Is.EqualTo(7));

            relationships.RemoveLink(knight, blueBanner, roles.MembershipTypeId);
            Assert.That(knight.Has<Team>(), Is.False);
        }

        [Test]
        public void SecondOwnershipResolverOnAnotherType_FailsFast()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateBoundRuntime(world, out _, out RelationshipRoleBindings roles);

            Assert.Throws<InvalidOperationException>(() => new OwnershipResolver(relationships, roles.MembershipTypeId));
        }

        private static RelationshipRuntime CreateBoundRuntime(
            World world,
            out OwnershipResolver ownership,
            out RelationshipRoleBindings roles)
        {
            var catalog = new RelationshipCatalogConfig
            {
                Types =
                {
                    new RelationshipTypeConfig { Id = "Holds", Role = RelationshipRole.Ownership },
                    new RelationshipTypeConfig { Id = "Commands", Role = RelationshipRole.ControlGrant },
                    new RelationshipTypeConfig { Id = "SwornTo", Role = RelationshipRole.Membership },
                },
            };
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, metrics, flags, bands);
            var relationships = new RelationshipRuntime(
                world,
                types,
                metrics,
                flags,
                bands,
                new RelationshipChangeBuffer(capacity: 32),
                new RelationshipReverseIndex(world));

            roles = RelationshipRoleBindings.Resolve(catalog, types);
            ownership = new OwnershipResolver(relationships, roles.OwnershipTypeId);
            ownership.BindIdentityProjection(world);
            relationships.BindParticipantIdentityProjection(roles);
            return relationships;
        }

        private static RelationshipTypeRegistry RegisterTypes(RelationshipCatalogConfig catalog)
        {
            var types = new RelationshipTypeRegistry();
            RelationshipCatalogInstaller.RegisterCatalog(
                catalog,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry());
            return types;
        }
    }
}
