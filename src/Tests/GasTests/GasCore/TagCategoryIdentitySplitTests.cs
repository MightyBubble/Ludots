using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.Gas.GasCore;

[Category("ci-gate")]
public sealed class TagCategoryIdentitySplitTests
{
    [SetUp]
    public void SetUp()
    {
        ModRegistryAmbient.Reset();
        PresentationEventKeyRegistry.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        ModRegistryAmbient.Reset();
        PresentationEventKeyRegistry.Clear();
    }

    [Test]
    public void EffectCategory_DoesNotConsumeGameplayTagSlots()
    {
        int before = TagRegistry.SnapshotMappings().Length;
        int categoryId = EffectCategoryRegistry.Register("Effect.Test.IdentitySplit");
        Assert.That(categoryId, Is.GreaterThan(0));
        Assert.That(TagRegistry.GetId("Effect.Test.IdentitySplit"), Is.EqualTo(TagRegistry.InvalidId));
        Assert.That(TagRegistry.SnapshotMappings().Length, Is.EqualTo(before));
        Assert.That(EffectCategoryRegistry.GetName(categoryId), Is.EqualTo("Effect.Test.IdentitySplit"));
    }

    [Test]
    public void AbilityCategory_DoesNotConsumeGameplayTagSlots()
    {
        int before = TagRegistry.SnapshotMappings().Length;
        int categoryId = AbilityCategoryRegistry.Register("castFamily.Test.Strike");
        Assert.That(categoryId, Is.GreaterThan(0));
        Assert.That(TagRegistry.GetId("castFamily.Test.Strike"), Is.EqualTo(TagRegistry.InvalidId));
        Assert.That(TagRegistry.SnapshotMappings().Length, Is.EqualTo(before));
    }

    [Test]
    public void PresentationEventKey_DoesNotConsumeGameplayTagSlots()
    {
        int before = TagRegistry.SnapshotMappings().Length;
        int keyId = PresentationEventKeyRegistry.Register("ability.aim.area.cone");
        Assert.That(keyId, Is.GreaterThan(0));
        Assert.That(TagRegistry.GetId("ability.aim.area.cone"), Is.EqualTo(TagRegistry.InvalidId));
        Assert.That(TagRegistry.SnapshotMappings().Length, Is.EqualTo(before));
    }

    [Test]
    public void GameplayTag_StillRegistersOnExplicitTagSurface()
    {
        int tagId = TagRegistry.Register("State.Test.Burning");
        Assert.That(tagId, Is.GreaterThan(0));
        Assert.That(TagRegistry.GetName(tagId), Is.EqualTo("State.Test.Burning"));
        Assert.That(EffectCategoryRegistry.GetId("State.Test.Burning"), Is.EqualTo(EffectCategoryRegistry.InvalidId));
    }
}
