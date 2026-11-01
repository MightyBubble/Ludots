using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class PresenterCommandOwnerCollectionTests
{
    private const string CollectionKey = "sample.actors";
    private const int ContextKeyId = 7;

    [Test]
    public void ContextActivated_CreatesOnTheIndexedCollectionMember()
    {
        using World world = World.Create();
        Entity subject = world.Create();
        Entity actor = world.Create();
        world.Add(actor, VisualTransform.Default);
        var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var store = new EntityCollectionStore(keys);
        store.Replace(
            subject,
            EntityCollectionDescriptor.Create(CollectionKey, EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.Display),
            new[] { actor });
        int keyId = keys.GetId(CollectionKey);

        using PresenterRuleSystem rules = Build(world, store, keyId, InlineConditionKind.OwnerHasVisualTransform);
        var events = rulesEvents;
        Assert.That(events.TryAdd(new PresentationEvent
        {
            Kind = PresentationEventKind.ContextActivated,
            KeyId = ContextKeyId,
            Source = subject,
            Target = subject,
        }), Is.True);

        rules.Update(0f);

        Assert.That(commands.Count, Is.EqualTo(1));
        Assert.That(commands.GetSpan()[0].Source, Is.EqualTo(actor));
        Assert.That(commands.GetSpan()[0].CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
    }

    [Test]
    public void ContextActivated_MissingMember_FailsNamingTheSubject()
    {
        using World world = World.Create();
        Entity subject = world.Create();
        var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var store = new EntityCollectionStore(keys);
        int keyId = keys.Register(CollectionKey);
        using PresenterRuleSystem rules = Build(world, store, keyId, InlineConditionKind.None);
        Assert.That(rulesEvents.TryAdd(new PresentationEvent
        {
            Kind = PresentationEventKind.ContextActivated,
            KeyId = ContextKeyId,
            Source = subject,
            Target = subject,
        }), Is.True);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => rules.Update(0f));
        Assert.That(error!.Message, Does.Contain("EventSourceCollectionMember"));
        Assert.That(error.Message, Does.Contain(subject.ToString()));
        Assert.That(error.Message, Does.Contain("index 0"));
    }

    [Test]
    public void MemberSwappedMidContext_DeactivateDestroysTheOriginalPresenter()
    {
        using World world = World.Create();
        using var pipeline = Pipeline.Open(world);
        Entity subject = world.Create();
        Entity actor = world.Create();
        Entity replacement = world.Create();
        world.Add(actor, VisualTransform.Default);
        world.Add(replacement, VisualTransform.Default);
        pipeline.ReplaceMembers(subject, actor);

        pipeline.Activate(subject);
        Entity presenter = pipeline.RequirePresenter(subject);
        Assert.That(world.Get<PresenterState>(presenter).OwnerEntity, Is.EqualTo(actor));
        Assert.That(world.Get<PresenterState>(presenter).ScopeSubject, Is.EqualTo(subject));

        pipeline.ReplaceMembers(subject, replacement);
        pipeline.Deactivate(subject);

        Assert.That(world.IsAlive(presenter), Is.False);
        Assert.That(pipeline.Instances.ActiveCount, Is.EqualTo(0));
        Assert.That(pipeline.TryFind(subject, out _), Is.False);
        Assert.That(pipeline.TryFind(replacement, out _), Is.False);
    }

    [Test]
    public void OwnerDeadBeforeDeactivate_InstanceIsCleaned()
    {
        using World world = World.Create();
        using var pipeline = Pipeline.Open(world);
        Entity subject = world.Create();
        Entity actor = world.Create();
        world.Add(actor, VisualTransform.Default);
        pipeline.ReplaceMembers(subject, actor);
        pipeline.Activate(subject);
        Entity presenter = pipeline.RequirePresenter(subject);

        world.Destroy(actor);
        pipeline.Deactivate(subject);

        Assert.That(world.IsAlive(presenter), Is.False);
        Assert.That(pipeline.Instances.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void TwoSubjects_SameContextId_DoNotClobber()
    {
        using World world = World.Create();
        using var pipeline = Pipeline.Open(world);
        Entity subjectA = world.Create();
        Entity subjectB = world.Create();
        Entity actorA = world.Create();
        Entity actorB = world.Create();
        world.Add(actorA, VisualTransform.Default);
        world.Add(actorB, VisualTransform.Default);
        pipeline.ReplaceMembers(subjectA, actorA);
        pipeline.ReplaceMembers(subjectB, actorB);

        pipeline.Activate(subjectA);
        pipeline.Activate(subjectB);

        Entity presenterA = pipeline.RequirePresenter(subjectA);
        Entity presenterB = pipeline.RequirePresenter(subjectB);
        Assert.That(presenterA, Is.Not.EqualTo(presenterB));
        Assert.That(world.Get<PresenterState>(presenterA).OwnerEntity, Is.EqualTo(actorA));
        Assert.That(world.Get<PresenterState>(presenterB).OwnerEntity, Is.EqualTo(actorB));
        Assert.That(pipeline.Instances.ActiveCount, Is.EqualTo(2));

        pipeline.Deactivate(subjectA);

        Assert.That(world.IsAlive(presenterA), Is.False);
        Assert.That(world.IsAlive(presenterB), Is.True);
        Assert.That(world.Get<PresenterState>(presenterB).OwnerEntity, Is.EqualTo(actorB));
        Assert.That(world.Get<PresenterState>(presenterB).ScopeSubject, Is.EqualTo(subjectB));
        Assert.That(pipeline.Instances.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void DirectCreates_SameScopeDifferentOwners_StayDistinct()
    {
        using World world = World.Create();
        var definitions = new PresenterDefinitionRegistry();
        int markerId = definitions.Register("range_marker", new PresenterDefinition());
        var commands = new PresenterCommandBuffer();
        var events = new PresentationEventStream(8);
        var instances = new PresenterEntityRuntime(world);
        using var runtime = new PresenterRuntimeSystem(
            world,
            commands,
            events,
            new TransientMarkerBuffer(),
            new PresentationRequestBuffer(),
            instances,
            new PresentationStableIdAllocator(),
            definitions);
        Entity ownerA = world.Create();
        Entity ownerB = world.Create();
        const int scope = 9;
        Assert.That(commands.TryAdd(Create(markerId, scope, ownerA)), Is.True);
        Assert.That(commands.TryAdd(Create(markerId, scope, ownerB)), Is.True);
        runtime.Update(0f);

        Assert.That(instances.TryGetActiveScopedInstance(
            markerId, ownerA, scope, PresentationAnchorKind.Entity, Vector3.Zero, out Entity presenterA), Is.True);
        Assert.That(instances.TryGetActiveScopedInstance(
            markerId, ownerB, scope, PresentationAnchorKind.Entity, Vector3.Zero, out Entity presenterB), Is.True);
        Assert.That(presenterA, Is.Not.EqualTo(presenterB));
        Assert.That(world.Get<PresenterState>(presenterA).OwnerEntity, Is.EqualTo(ownerA));
        Assert.That(world.Get<PresenterState>(presenterB).OwnerEntity, Is.EqualTo(ownerB));
        Assert.That(instances.ActiveCount, Is.EqualTo(2));
    }

    private static PresenterCommand Create(int markerId, int scope, Entity owner)
    {
        return new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = markerId,
            ScopeTag = scope,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = owner,
        };
    }

    private PresentationEventStream rulesEvents = null!;
    private PresenterCommandBuffer commands = null!;

    private PresenterRuleSystem Build(
        World world,
        EntityCollectionStore store,
        int collectionKeyId,
        InlineConditionKind condition)
    {
        rulesEvents = new PresentationEventStream(8);
        commands = new PresenterCommandBuffer();
        var definitions = new PresenterDefinitionRegistry();
        int markerId = definitions.Register("range_marker", new PresenterDefinition());
        definitions.Register("rules", new PresenterDefinition
        {
            Rules = new[]
            {
                new PresenterRule
                {
                    Event = new EventFilter
                    {
                        Kind = PresentationEventKind.ContextActivated,
                        KeyId = ContextKeyId,
                    },
                    Condition = new ConditionRef { Inline = condition },
                    Command = new PresenterCommand
                    {
                        CommandKind = PresenterCommandKind.CreatePresenter,
                        CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                        RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                        PresenterDefinitionId = markerId,
                        ScopeSource = PresenterCommandScopeSource.EventKeyId,
                        OwnerSource = PresenterCommandEntitySource.EventSourceCollectionMember,
                        OwnerCollectionKeyId = collectionKeyId,
                    },
                },
            },
        });

        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.EntityCollectionStore.Name] = store,
        };
        return new PresenterRuleSystem(
            world,
            rulesEvents,
            commands,
            definitions,
            runtime: null,
            new GraphProgramRegistry(),
            new GasGraphRuntimeApi(world),
            globals);
    }

    private sealed class Pipeline : IDisposable
    {
        public PresenterEntityRuntime Instances { get; }
        private readonly PresentationEventStream _events;
        private readonly PresenterRuleSystem _rules;
        private readonly PresenterRuntimeSystem _runtime;
        private readonly EntityCollectionStore _store;
        private readonly int _markerId;
        private readonly string _collectionKey;

        private Pipeline(
            PresenterEntityRuntime instances,
            PresentationEventStream events,
            PresenterRuleSystem rules,
            PresenterRuntimeSystem runtime,
            EntityCollectionStore store,
            int markerId,
            string collectionKey)
        {
            Instances = instances;
            _events = events;
            _rules = rules;
            _runtime = runtime;
            _store = store;
            _markerId = markerId;
            _collectionKey = collectionKey;
        }

        public static Pipeline Open(World world)
        {
            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
            var store = new EntityCollectionStore(keys);
            keys.Register(CollectionKey);
            var events = new PresentationEventStream(16);
            var commands = new PresenterCommandBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int markerId = definitions.Register("range_marker", new PresenterDefinition());
            int collectionKeyId = keys.GetId(CollectionKey);
            definitions.Register("rules", new PresenterDefinition
            {
                Rules = new[]
                {
                    Rule(PresentationEventKind.ContextActivated, PresenterCommandKind.CreatePresenter, markerId, collectionKeyId),
                    Rule(PresentationEventKind.ContextDeactivated, PresenterCommandKind.DestroyScopedPresenter, markerId, collectionKeyId),
                },
            });
            var instances = new PresenterEntityRuntime(world);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.EntityCollectionStore.Name] = store,
            };
            var rules = new PresenterRuleSystem(
                world,
                events,
                commands,
                definitions,
                instances,
                new GraphProgramRegistry(),
                new GasGraphRuntimeApi(world),
                globals);
            var runtime = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);
            return new Pipeline(instances, events, rules, runtime, store, markerId, CollectionKey);
        }

        public void ReplaceMembers(Entity subject, params Entity[] members)
        {
            _store.Replace(
                subject,
                EntityCollectionDescriptor.Create(_collectionKey, EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.Display),
                members);
        }

        public void Activate(Entity subject) => Publish(PresentationEventKind.ContextActivated, subject);

        public void Deactivate(Entity subject) => Publish(PresentationEventKind.ContextDeactivated, subject);

        public Entity RequirePresenter(Entity subject)
        {
            Assert.That(TryFind(subject, out Entity presenter), Is.True);
            return presenter;
        }

        public bool TryFind(Entity subject, out Entity presenter)
        {
            return Instances.TryGetActiveScopedInstance(
                _markerId,
                subject,
                ContextKeyId,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                out presenter);
        }

        public void Dispose()
        {
            _rules.Dispose();
            _runtime.Dispose();
        }

        private void Publish(PresentationEventKind kind, Entity subject)
        {
            _events.Clear();
            Assert.That(_events.TryAdd(new PresentationEvent
            {
                Kind = kind,
                KeyId = ContextKeyId,
                Source = subject,
                Target = subject,
            }), Is.True);
            _rules.Update(0f);
            _runtime.Update(0f);
        }

        private static PresenterRule Rule(
            PresentationEventKind kind,
            PresenterCommandKind commandKind,
            int markerId,
            int collectionKeyId)
        {
            return new PresenterRule
            {
                Event = new EventFilter
                {
                    Kind = kind,
                    KeyId = ContextKeyId,
                },
                Condition = ConditionRef.AlwaysTrue,
                Command = new PresenterCommand
                {
                    CommandKind = commandKind,
                    CommandKindId = (byte)commandKind,
                    RouteStrategy = commandKind == PresenterCommandKind.CreatePresenter
                        ? PresenterCommandRouteStrategy.CreatePresenter
                        : PresenterCommandRouteStrategy.ScopedInstance,
                    PresenterDefinitionId = markerId,
                    ScopeSource = PresenterCommandScopeSource.EventKeyId,
                    OwnerSource = PresenterCommandEntitySource.EventSourceCollectionMember,
                    OwnerCollectionKeyId = collectionKeyId,
                },
            };
        }
    }
}
