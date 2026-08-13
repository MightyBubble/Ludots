using System;
using System.Linq;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class PresenterKindInventoryGuardTests
    {
        [Test]
        public void AssetKind_AllowlistMatchesEpicInventory()
        {
            string[] expected =
            {
                nameof(AssetKind.Mesh),
                nameof(AssetKind.SkinnedMesh),
                nameof(AssetKind.Decal),
                nameof(AssetKind.VFX),
                nameof(AssetKind.Sound),
                nameof(AssetKind.Spline),
                nameof(AssetKind.WorldHud),
                nameof(AssetKind.WorldText),
                nameof(AssetKind.GroundOverlay),
                nameof(AssetKind.Surface),
            };

            AssertKindAllowlist(
                typeof(AssetKind),
                expected,
                "AssetKind allowlist is frozen by Epic #924 P0. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)AssetKind.Mesh, Is.EqualTo(1));
            Assert.That((byte)AssetKind.SkinnedMesh, Is.EqualTo(2));
            Assert.That((byte)AssetKind.Decal, Is.EqualTo(3));
            Assert.That((byte)AssetKind.VFX, Is.EqualTo(4));
            Assert.That((byte)AssetKind.Sound, Is.EqualTo(5));
            Assert.That((byte)AssetKind.Spline, Is.EqualTo(6));
            Assert.That((byte)AssetKind.WorldHud, Is.EqualTo(7));
            Assert.That((byte)AssetKind.WorldText, Is.EqualTo(8));
            Assert.That((byte)AssetKind.GroundOverlay, Is.EqualTo(9));
            Assert.That((byte)AssetKind.Surface, Is.EqualTo(10));
        }

        [Test]
        public void BehaviorKind_AllowlistMatchesEpicInventory()
        {
            string[] expected =
            {
                nameof(BehaviorKind.AssetBinding),
                nameof(BehaviorKind.AttributeBinding),
                nameof(BehaviorKind.TagBinding),
                nameof(BehaviorKind.Animator),
                nameof(BehaviorKind.Attachment),
                nameof(BehaviorKind.Sound),
                nameof(BehaviorKind.Material),
                nameof(BehaviorKind.Spline),
                nameof(BehaviorKind.Grounding),
                nameof(BehaviorKind.MinimapMarker),
                nameof(BehaviorKind.WorldText),
                nameof(BehaviorKind.SurfaceSource),
                nameof(BehaviorKind.InstancedBatch),
            };

            AssertKindAllowlist(
                typeof(BehaviorKind),
                expected,
                "BehaviorKind allowlist is frozen by Epic #924 P0. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)BehaviorKind.AssetBinding, Is.EqualTo(1));
            Assert.That((byte)BehaviorKind.AttributeBinding, Is.EqualTo(2));
            Assert.That((byte)BehaviorKind.TagBinding, Is.EqualTo(3));
            Assert.That((byte)BehaviorKind.Animator, Is.EqualTo(4));
            Assert.That((byte)BehaviorKind.Attachment, Is.EqualTo(5));
            Assert.That((byte)BehaviorKind.Sound, Is.EqualTo(6));
            Assert.That((byte)BehaviorKind.Material, Is.EqualTo(7));
            Assert.That((byte)BehaviorKind.Spline, Is.EqualTo(8));
            Assert.That((byte)BehaviorKind.Grounding, Is.EqualTo(9));
            Assert.That((byte)BehaviorKind.MinimapMarker, Is.EqualTo(10));
            Assert.That((byte)BehaviorKind.WorldText, Is.EqualTo(11));
            Assert.That((byte)BehaviorKind.SurfaceSource, Is.EqualTo(12));
            Assert.That((byte)BehaviorKind.InstancedBatch, Is.EqualTo(13));
        }

        [Test]
        public void PresentationRequestKind_AllowlistMatchesEpicInventory()
        {
            // Prefab is doomed: P2 (#927) deletes PresentationRequestKind.Prefab from this allowlist.
            // RoadSpline / RemoveRoadSpline are doomed: P4 (#929) renames them to the generic spline ribbon contract.
            string[] expected =
            {
                nameof(PresentationRequestKind.VisualProxy),
                nameof(PresentationRequestKind.Prefab),
                nameof(PresentationRequestKind.GroundOverlay),
                nameof(PresentationRequestKind.WorldHud),
                nameof(PresentationRequestKind.RoadSpline),
                nameof(PresentationRequestKind.SurfaceSource),
                nameof(PresentationRequestKind.RemoveGroundOverlay),
                nameof(PresentationRequestKind.RemoveWorldHud),
                nameof(PresentationRequestKind.RemoveRoadSpline),
                nameof(PresentationRequestKind.RemoveSurfaceSource),
                nameof(PresentationRequestKind.ClearTransientVisualProjection),
            };

            AssertKindAllowlist(
                typeof(PresentationRequestKind),
                expected,
                "PresentationRequestKind allowlist is frozen by Epic #924 P0. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)PresentationRequestKind.VisualProxy, Is.EqualTo(1));
            Assert.That((byte)PresentationRequestKind.Prefab, Is.EqualTo(2));
            Assert.That((byte)PresentationRequestKind.GroundOverlay, Is.EqualTo(3));
            Assert.That((byte)PresentationRequestKind.WorldHud, Is.EqualTo(4));
            Assert.That((byte)PresentationRequestKind.RoadSpline, Is.EqualTo(5));
            Assert.That((byte)PresentationRequestKind.SurfaceSource, Is.EqualTo(6));
            Assert.That((byte)PresentationRequestKind.RemoveGroundOverlay, Is.EqualTo(7));
            Assert.That((byte)PresentationRequestKind.RemoveWorldHud, Is.EqualTo(8));
            Assert.That((byte)PresentationRequestKind.RemoveRoadSpline, Is.EqualTo(9));
            Assert.That((byte)PresentationRequestKind.RemoveSurfaceSource, Is.EqualTo(10));
            Assert.That((byte)PresentationRequestKind.ClearTransientVisualProjection, Is.EqualTo(11));
        }

        private static void AssertKindAllowlist(Type enumType, string[] expected, string failMessage)
        {
            string[] actual = Enum.GetNames(enumType);
            Assert.That(
                actual.Length,
                Is.EqualTo(expected.Length),
                $"{failMessage} {enumType.Name} has {actual.Length} values [{string.Join(", ", actual)}] but the frozen allowlist has {expected.Length} [{string.Join(", ", expected)}].");
            Assert.That(
                actual,
                Is.EqualTo(expected),
                failMessage);
            Assert.That(
                actual.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(actual.Length),
                $"{enumType.Name} must not duplicate names.");
        }
    }
}
