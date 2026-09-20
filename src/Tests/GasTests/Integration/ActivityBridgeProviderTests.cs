using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Integration;

[TestFixture]
public sealed class ActivityBridgeProviderTests
{
    [Test]
    public void ActivityOfferEffect_CreatesInstanceForSubjectScope()
    {
        using World world = World.Create();
        var definitions = new ActivityDefinitionRegistry();
        definitions.Register("bridge.forced", new ActivityDefinition
        {
            SourceKey = "fixture.signal_ping",
            DispatchPolicy = ActivityDispatchPolicy.Forced,
            Options = { new ActivityOptionDefinition { Id = "hold", IsBaseline = true } },
        });
        var providers = new ProviderServices();
        FixtureProviderInstaller.InstallMinimal(providers);
        var runtime = new ActivityRuntimeService(
            world,
            definitions,
            providers,
            new ActivityPresentationBuffer());
        ActivityBridgeProviderInstaller.Install(providers, runtime);
        Assert.That(providers.Effects.Contains("activity.offer"), Is.True);

        Entity scopeHost = world.Create();
        ProviderLookupResult<IEffectHandler> effectLookup = providers.Effects.TryGet("activity.offer");
        Assert.That(effectLookup.Found, Is.True);
        IEffectHandler effect = effectLookup.Implementation!;
        ProviderParameterSchema schema = effectLookup.Schema!;
        var call = new ProviderEffectCall(
            "activity.offer",
            "context.subject",
            new Dictionary<string, object?> { ["activity_id"] = "bridge.forced" },
            1);
        effect.Execute(in call, new ProviderExecutionContext(world, scopeHost, new Dictionary<string, object?>()));

        List<ActivityView> views = runtime.CaptureViews();
        Assert.That(views, Has.Count.EqualTo(1));
        Assert.That(views[0].State, Is.EqualTo(ActivityInstanceState.Active));
        Assert.That(views[0].ScopeHost, Is.EqualTo(scopeHost));
        schema.Validate(
            new Dictionary<string, object?> { ["activity_id"] = "bridge.forced" },
            "activity.offer");
    }

    [Test]
    public void ActivityOfferEffect_WithoutActivityId_FailsFast()
    {
        using World world = World.Create();
        var providers = new ProviderServices();
        FixtureProviderInstaller.InstallMinimal(providers);
        var runtime = new ActivityRuntimeService(
            world,
            new ActivityDefinitionRegistry(),
            providers,
            new ActivityPresentationBuffer());
        ActivityBridgeProviderInstaller.Install(providers, runtime);
        IEffectHandler effect = providers.Effects.TryGet("activity.offer").Implementation!;

        Assert.Throws<InvalidOperationException>(() =>
            effect.Execute(
                new ProviderEffectCall("activity.offer", "context.subject", new Dictionary<string, object?>(), 1),
                new ProviderExecutionContext(world, world.Create(), new Dictionary<string, object?>())));
    }

    [Test]
    public void SubjectAttributeCondition_ComparesAgainstScopeHostAttribute()
    {
        using World world = World.Create();
        var providers = new ProviderServices();
        FixtureProviderInstaller.InstallMinimal(providers);
        var runtime = new ActivityRuntimeService(
            world,
            new ActivityDefinitionRegistry(),
            providers,
            new ActivityPresentationBuffer());
        ActivityBridgeProviderInstaller.Install(providers, runtime);

        int healthId = AttributeRegistry.Register("Health");
        Assert.That(healthId, Is.GreaterThanOrEqualTo(0), "Health attribute must be registerable for the showcase condition.");

        Entity subject = world.Create();
        var buffer = new AttributeBuffer();
        buffer.SetCurrent(healthId, 60f);
        world.Add(subject, buffer);
        var context = new ProviderExecutionContext(world, subject, new Dictionary<string, object?>());

        IConditionProvider condition = providers.Conditions.TryGet("world.subject_attribute").Implementation!;

        Assert.That(condition.Evaluate(context, new Dictionary<string, object?>
        {
            ["attribute_key"] = "Health",
            ["op"] = "greater_equal",
            ["value"] = 50.0,
        }), Is.True);
        Assert.That(condition.Evaluate(context, new Dictionary<string, object?>
        {
            ["attribute_key"] = "Health",
            ["op"] = "greater_equal",
            ["value"] = 80.0,
        }), Is.False);
    }

    [Test]
    public void SubjectAttributeCondition_UnknownAttributeOrSubject_FailsClosedOrFalse()
    {
        using World world = World.Create();
        var providers = new ProviderServices();
        FixtureProviderInstaller.InstallMinimal(providers);
        var runtime = new ActivityRuntimeService(
            world,
            new ActivityDefinitionRegistry(),
            providers,
            new ActivityPresentationBuffer());
        ActivityBridgeProviderInstaller.Install(providers, runtime);
        IConditionProvider condition = providers.Conditions.TryGet("world.subject_attribute").Implementation!;
        var withBuffer = new ProviderExecutionContext(world, world.Create(), new Dictionary<string, object?>());
        Assert.Throws<InvalidOperationException>(() =>
            condition.Evaluate(withBuffer, new Dictionary<string, object?>
            {
                ["attribute_key"] = "No.Such.Attribute",
                ["op"] = "greater_equal",
                ["value"] = 1.0,
            }));

        Entity noBuffer = world.Create();
        var noBufferContext = new ProviderExecutionContext(world, noBuffer, new Dictionary<string, object?>());
        Assert.That(condition.Evaluate(noBufferContext, new Dictionary<string, object?>
        {
            ["attribute_key"] = "Health",
            ["op"] = "greater_equal",
            ["value"] = 1.0,
        }), Is.False, "Subject without an AttributeBuffer must evaluate false, not throw.");
    }
}
