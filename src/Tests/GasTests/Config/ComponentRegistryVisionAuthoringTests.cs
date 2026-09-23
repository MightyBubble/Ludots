using System.Text.Json.Nodes;
using Arch.Core;
<<<<<<< Updated upstream
=======
using Ludots.Core.Association;
using Ludots.Core.Config;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream

        CoreComponentRegistry.Apply(entity, "VisionEmitterCm", ValidEmitter());
=======
        ComponentAuthoringContext context = CreateAuthoringContext(out int expectedScopeId, out uint expectedLayerMask);

        CoreComponentRegistry.Apply(entity, "VisionEmitterCm", ValidEmitter(), context);
>>>>>>> Stashed changes

        VisionEmitterCm emitter = world.Get<VisionEmitterCm>(entity);
        Assert.Multiple(() =>
        {
<<<<<<< Updated upstream
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(2));
            Assert.That(emitter.LayerMask, Is.EqualTo(5u));
=======
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(expectedScopeId));
            Assert.That(emitter.LayerMask, Is.EqualTo(expectedLayerMask));
>>>>>>> Stashed changes
            Assert.That(emitter.Polarity, Is.EqualTo(VisionPolarity.Reveal));
            Assert.That(emitter.Aperture.Kind, Is.EqualTo(VisionApertureKind.Cone));
            Assert.That(emitter.Aperture.RangeCm, Is.EqualTo(3_200));
            Assert.That(emitter.Aperture.HalfAngleDeg, Is.EqualTo(45));
            Assert.That(emitter.Aperture.HalfWidthCm, Is.Zero);
            Assert.That(emitter.AltitudeBand, Is.EqualTo(3));
            Assert.That(emitter.Priority, Is.EqualTo(4));
<<<<<<< Updated upstream
            Assert.That(emitter.TargetScopeSelectorId, Is.EqualTo(6));
            Assert.That(emitter.UpdatePolicyId, Is.EqualTo(7));
=======
            Assert.That(emitter.TargetScopeSelectorId, Is.Zero);
            Assert.That(emitter.UpdatePolicyId, Is.Zero);
>>>>>>> Stashed changes
            Assert.That(emitter.DetectionStrength, Is.EqualTo(8));
            Assert.That(emitter.TrueSightStrength, Is.EqualTo(9));
        });
    }

    [Test]
    public void VisionEmitterCm_RejectsMissingAndUnsupportedApertureData()
    {
        using World world = World.Create();
<<<<<<< Updated upstream
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
=======
        ComponentAuthoringContext context = CreateAuthoringContext(out _, out _);
        Entity missingRangeEntity = world.Create();
        JsonNode missingRange = ValidEmitter();
        missingRange["aperture"]!.AsObject().Remove("rangeCm");

        Assert.That(
            () => CoreComponentRegistry.Apply(missingRangeEntity, "VisionEmitterCm", missingRange, context),
            Throws.InvalidOperationException.With.Message.Contains("requires explicit 'rangeCm'"));

        Entity unsupportedKindEntity = world.Create();
        JsonNode unsupportedKind = ValidEmitter();
        unsupportedKind["aperture"]!["kind"] = "Triangle";
        Assert.That(
            () => CoreComponentRegistry.Apply(unsupportedKindEntity, "VisionEmitterCm", unsupportedKind, context),
            Throws.InvalidOperationException.With.Message.Contains("Unsupported VisionEmitterCm.aperture.kind 'Triangle'"));
    }

    private static ComponentAuthoringContext CreateAuthoringContext(out int scopeId, out uint layerMask)
    {
        var scopeKeys = new ScopeKeyRegistry();
        scopeId = scopeKeys.Register("core.team");

        var fogLayers = new FogLayerRegistry();
        FogLayerId groundLayer = fogLayers.Register("ground", cellSizeCm: 100, updateHz: 10);
        FogLayerId airLayer = fogLayers.Register("air", cellSizeCm: 100, updateHz: 5);
        layerMask = fogLayers.ToMask(groundLayer) | fogLayers.ToMask(airLayer);

        var context = new ComponentAuthoringContext();
        context.Set(ComponentAuthoringServiceKeys.ScopeKeyRegistry, scopeKeys);
        context.Set(ComponentAuthoringServiceKeys.VisionFogLayerRegistry, fogLayers);
        return context;
>>>>>>> Stashed changes
    }

    private static JsonNode ValidEmitter() => JsonNode.Parse("""
        {
<<<<<<< Updated upstream
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
=======
          "scope": "core.team",
          "layers": ["ground", "air"],
          "polarity": "Reveal",
          "aperture": {
            "kind": "Cone",
            "rangeCm": 3200,
            "halfAngleDeg": 45
          },
          "altitudeBand": 3,
          "priority": 4,
          "detectionStrength": 8,
          "trueSightStrength": 9
>>>>>>> Stashed changes
        }
        """)!;
}
