using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Vision;
using NUnit.Framework;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class ComponentRegistryVisionAuthoringTests
{
    [Test]
    public void VisionEmitterCm_AuthorsImmutableApertureWithoutDroppingItsValues()
    {
        using World world = World.Create();
        Entity entity = world.Create();

        CoreComponentRegistry.Apply(entity, "VisionEmitterCm", ValidEmitter());

        VisionEmitterCm emitter = world.Get<VisionEmitterCm>(entity);
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(2));
            Assert.That(emitter.LayerMask, Is.EqualTo(5u));
            Assert.That(emitter.Polarity, Is.EqualTo(VisionPolarity.Reveal));
            Assert.That(emitter.Aperture.Kind, Is.EqualTo(VisionApertureKind.Cone));
            Assert.That(emitter.Aperture.RangeCm, Is.EqualTo(3_200));
            Assert.That(emitter.Aperture.HalfAngleDeg, Is.EqualTo(45));
            Assert.That(emitter.Aperture.HalfWidthCm, Is.Zero);
            Assert.That(emitter.AltitudeBand, Is.EqualTo(3));
            Assert.That(emitter.Priority, Is.EqualTo(4));
            Assert.That(emitter.TargetScopeSelectorId, Is.EqualTo(6));
            Assert.That(emitter.UpdatePolicyId, Is.EqualTo(7));
            Assert.That(emitter.DetectionStrength, Is.EqualTo(8));
            Assert.That(emitter.TrueSightStrength, Is.EqualTo(9));
        });
    }

    [Test]
    public void VisionEmitterCm_RejectsMissingAndUnsupportedApertureData()
    {
        using World world = World.Create();
        Entity missingRangeEntity = world.Create();
        JsonNode missingRange = ValidEmitter();
        missingRange["Aperture"]!.AsObject().Remove("RangeCm");

        Assert.That(
            () => CoreComponentRegistry.Apply(missingRangeEntity, "VisionEmitterCm", missingRange),
            Throws.InvalidOperationException.With.Message.Contains("requires explicit 'RangeCm'"));

        Entity unsupportedKindEntity = world.Create();
        JsonNode unsupportedKind = ValidEmitter();
        unsupportedKind["Aperture"]!["Kind"] = 99;
        Assert.That(
            () => CoreComponentRegistry.Apply(unsupportedKindEntity, "VisionEmitterCm", unsupportedKind),
            Throws.InvalidOperationException.With.Message.Contains("Kind '99' is not supported"));
    }

    private static JsonNode ValidEmitter() => JsonNode.Parse("""
        {
          "ScopeKeyId": 2,
          "LayerMask": 5,
          "Polarity": 0,
          "Aperture": {
            "Kind": 1,
            "RangeCm": 3200,
            "HalfAngleDeg": 45,
            "HalfWidthCm": 0
          },
          "AltitudeBand": 3,
          "Priority": 4,
          "TargetScopeSelectorId": 6,
          "UpdatePolicyId": 7,
          "DetectionStrength": 8,
          "TrueSightStrength": 9
        }
        """)!;
}
