using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
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
}
