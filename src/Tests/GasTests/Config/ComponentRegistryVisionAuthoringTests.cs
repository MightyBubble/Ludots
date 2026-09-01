using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
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
        ComponentAuthoringContext context = CreateAuthoringContext(out int expectedScopeId, out uint expectedLayerMask);

        CoreComponentRegistry.Apply(entity, "VisionEmitterCm", ValidEmitter(), context);

        VisionEmitterCm emitter = world.Get<VisionEmitterCm>(entity);
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(expectedScopeId));
            Assert.That(emitter.LayerMask, Is.EqualTo(expectedLayerMask));
            Assert.That(emitter.Polarity, Is.EqualTo(VisionPolarity.Reveal));
            Assert.That(emitter.Aperture.Kind, Is.EqualTo(VisionApertureKind.Cone));
            Assert.That(emitter.Aperture.RangeCm, Is.EqualTo(3_200));
            Assert.That(emitter.Aperture.HalfAngleDeg, Is.EqualTo(45));
            Assert.That(emitter.Aperture.HalfWidthCm, Is.Zero);
            Assert.That(emitter.AltitudeBand, Is.EqualTo(3));
            Assert.That(emitter.Priority, Is.EqualTo(4));
            Assert.That(emitter.TargetScopeSelectorId, Is.Zero);
            Assert.That(emitter.UpdatePolicyId, Is.Zero);
            Assert.That(emitter.DetectionStrength, Is.EqualTo(8));
            Assert.That(emitter.TrueSightStrength, Is.EqualTo(9));
        });
    }

    [Test]
    public void VisionEmitterCm_RejectsMissingAndUnsupportedApertureData()
    {
        using World world = World.Create();
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
    }

    private static JsonNode ValidEmitter() => JsonNode.Parse("""
        {
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
        }
        """)!;
}
