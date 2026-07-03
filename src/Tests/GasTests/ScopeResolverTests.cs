using Arch.Core;
using Ludots.Core.Association;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ScopeResolverTests
    {
        [Test]
        public void ResolveHostAndRoleSlots_UseSingleAssociationModel()
        {
            using World world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create();
            Entity contextEntity = world.Create();
            Entity actor = world.Create();
            Entity subject = world.Create();
            Entity viewer = world.Create();
            Entity explicitHost = world.Create();
            Entity namedHost = world.Create(new ScopeMembershipRevision());
            int cityScopeId = 7;

            var refs = new ScopeRefBuffer();
            Assert.That(refs.TryAdd(cityScopeId, namedHost), Is.True);
            world.Add(subject, refs);

            var resolver = new ScopeResolver(world);
            var roles = new RoleResolverContext(
                source: source,
                target: target,
                context: contextEntity,
                actor: actor,
                subject: subject,
                viewer: viewer,
                explicitScopeHost: explicitHost);

            Assert.Multiple(() =>
            {
                Assert.That(RoleResolver.Resolve(RoleSlot.Source, in roles), Is.EqualTo(source));
                Assert.That(RoleResolver.Resolve(RoleSlot.Target, in roles), Is.EqualTo(target));
                Assert.That(RoleResolver.Resolve(RoleSlot.Context, in roles), Is.EqualTo(contextEntity));
                Assert.That(RoleResolver.Resolve(RoleSlot.Actor, in roles), Is.EqualTo(actor));
                Assert.That(RoleResolver.Resolve(RoleSlot.Subject, in roles), Is.EqualTo(subject));
                Assert.That(RoleResolver.Resolve(RoleSlot.Viewer, in roles), Is.EqualTo(viewer));
                Assert.That(resolver.TryResolveHost(ScopeKey.Self, in roles, out Entity selfHost), Is.True);
                Assert.That(selfHost, Is.EqualTo(subject));
                Assert.That(resolver.TryResolveHost(ScopeKey.Explicit(), in roles, out Entity resolvedExplicit), Is.True);
                Assert.That(resolvedExplicit, Is.EqualTo(explicitHost));
                Assert.That(resolver.TryResolveHost(ScopeKey.Named(cityScopeId), in roles, out Entity resolvedNamed), Is.True);
                Assert.That(resolvedNamed, Is.EqualTo(namedHost));
            });
        }

        [Test]
        public void ResolveMembers_WritesScopeMembersToCallerSpanWithoutAllocations()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            Entity host = world.Create(new ScopeMembershipRevision());
            Entity memberA = world.Create();
            Entity memberB = world.Create();
            Entity outsider = world.Create();
            int squadScopeId = 3;
            int cityScopeId = 4;
            AddScopeRef(world, subject, squadScopeId, host);
            AddScopeRef(world, memberA, squadScopeId, host);
            AddScopeRef(world, memberB, squadScopeId, host);
            AddScopeRef(world, outsider, cityScopeId, host);
            var resolver = new ScopeResolver(world);
            var roles = new RoleResolverContext(subject: subject);
            Span<Entity> buffer = stackalloc Entity[4];

            int warmup = resolver.ResolveMembers(ScopeKey.Named(squadScopeId), in roles, buffer);
            Assert.That(warmup, Is.EqualTo(3));
            Assert.That(buffer.Slice(0, warmup).ToArray(), Is.EquivalentTo(new[] { subject, memberA, memberB }));
            for (int i = 0; i < 64; i++)
            {
                resolver.ResolveMembers(ScopeKey.Named(squadScopeId), in roles, buffer);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                resolver.ResolveMembers(ScopeKey.Named(squadScopeId), in roles, buffer);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static void AddScopeRef(World world, Entity entity, int scopeKeyId, Entity host)
        {
            var refs = new ScopeRefBuffer();
            Assert.That(refs.TryAdd(scopeKeyId, host), Is.True);
            world.Add(entity, refs);
        }
    }
}
