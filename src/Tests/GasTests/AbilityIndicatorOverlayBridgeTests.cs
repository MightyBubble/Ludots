using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Orders;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AbilityIndicatorOverlayBridgeTests
    {
        private World _world = null!;
        private AbilityDefinitionRegistry _abilities = null!;
        private GroundOverlayBuffer _overlays = null!;
        private PerformerDefinitionRegistry _performerDefinitions = null!;
        private PerformerEntityRuntime _performers = null!;
        private AbilityIndicatorOverlayBridge _bridge = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _abilities = new AbilityDefinitionRegistry();
            _overlays = new GroundOverlayBuffer();
            _performerDefinitions = new PerformerDefinitionRegistry();
            _performers = new PerformerEntityRuntime(_world);
            _bridge = new AbilityIndicatorOverlayBridge(_world, _abilities, _overlays, _performerDefinitions, _performers);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void UpdateAiming_PositionCircle_EmitsRangeAndTargetCircle()
        {
            RegisterAbility(1001, new AbilityIndicatorConfig
            {
                Shape = TargetShape.Circle,
                Range = 500f,
                Radius = 120f,
                ShowRangeCircle = true,
                ValidColor = new Vector4(0.1f, 0.8f, 0.2f, 0.4f),
                InvalidColor = new Vector4(0.9f, 0.2f, 0.2f, 0.4f),
                RangeCircleColor = new Vector4(0.2f, 0.4f, 0.9f, 0.2f)
            });
            var actor = CreateActor(1001);

            _bridge.UpdateAiming(
                actor,
                new InputOrderMapping
                {
                    ActionId = "SkillW",
                    SelectionType = OrderSelectionType.Position,
                    ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
                },
                hasCursorWorldCm: true,
                cursorWorldCm: new Vector3(300f, 0f, 400f),
                hoveredEntity: Entity.Null);

            var span = _overlays.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2));
            Assert.That(span[0].Shape, Is.EqualTo(GroundOverlayShape.Circle));
            Assert.That(span[0].Radius, Is.EqualTo(5f).Within(0.001f));
            Assert.That(span[1].Center.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(span[1].Center.Z, Is.EqualTo(4f).Within(0.001f));
            Assert.That(span[1].Radius, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(span[1].FillColor, Is.EqualTo(new Vector4(0.1f, 0.8f, 0.2f, 0.4f)));
        }

        [Test]
        public void UpdateAiming_DirectionCone_OutOfRange_UsesInvalidColor()
        {
            RegisterAbility(1002, new AbilityIndicatorConfig
            {
                Shape = TargetShape.Cone,
                Range = 400f,
                Radius = 400f,
                Angle = MathF.PI / 5f,
                ShowRangeCircle = true,
                ValidColor = new Vector4(0.1f, 0.8f, 0.2f, 0.4f),
                InvalidColor = new Vector4(0.9f, 0.2f, 0.2f, 0.4f),
                RangeCircleColor = new Vector4(0.2f, 0.4f, 0.9f, 0.2f)
            });
            var actor = CreateActor(1002);

            _bridge.UpdateAiming(
                actor,
                new InputOrderMapping
                {
                    ActionId = "SkillE",
                    SelectionType = OrderSelectionType.Direction,
                    ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
                },
                hasCursorWorldCm: true,
                cursorWorldCm: new Vector3(800f, 0f, 0f),
                hoveredEntity: Entity.Null);

            var span = _overlays.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2));
            Assert.That(span[1].Shape, Is.EqualTo(GroundOverlayShape.Cone));
            Assert.That(span[1].Rotation, Is.EqualTo(0f).Within(0.001f));
            Assert.That(span[1].Radius, Is.EqualTo(4f).Within(0.001f));
            Assert.That(span[1].FillColor, Is.EqualTo(new Vector4(0.9f, 0.2f, 0.2f, 0.4f)));
        }

        [Test]
        public void UpdateAiming_Ring_UsesConfiguredInnerRadius()
        {
            RegisterAbility(1003, new AbilityIndicatorConfig
            {
                Shape = TargetShape.Ring,
                Range = 650f,
                Radius = 250f,
                InnerRadius = 120f,
                ShowRangeCircle = true,
                ValidColor = new Vector4(0.3f, 0.7f, 0.9f, 0.4f),
                InvalidColor = new Vector4(0.9f, 0.2f, 0.2f, 0.4f),
                RangeCircleColor = new Vector4(0.2f, 0.4f, 0.9f, 0.2f)
            });
            var actor = CreateActor(1003);

            _bridge.UpdateAiming(
                actor,
                new InputOrderMapping
                {
                    ActionId = "SkillR",
                    SelectionType = OrderSelectionType.Position,
                    ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
                },
                hasCursorWorldCm: true,
                cursorWorldCm: new Vector3(100f, 0f, 200f),
                hoveredEntity: Entity.Null);

            var span = _overlays.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2));
            Assert.That(span[1].Shape, Is.EqualTo(GroundOverlayShape.Ring));
            Assert.That(span[1].Radius, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(span[1].InnerRadius, Is.EqualTo(1.2f).Within(0.001f));
        }

        [Test]
        public void UpdateAiming_WithPreviewPerformer_AllocatesAndTintsGhost()
        {
            _performerDefinitions.Register("test.preview", new PerformerDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1,
                            MaterialId = 0,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalRotation = Quaternion.Identity,
                            LocalScale = Vector3.One,
                            ScaleParamKey = -1,
                            ColorParamKey = -1,
                            MaterialParamKey = -1,
                            AssetSwapParamKey = -1,
                            VisibilityParamKey = -1,
                        }
                    }
                },
                DefaultLifetime = -1f
            });

            RegisterAbility(1004, new AbilityIndicatorConfig
            {
                Shape = TargetShape.Circle,
                Range = 800f,
                Radius = 240f,
                ValidColor = new Vector4(0.2f, 0.8f, 0.5f, 0.35f),
                InvalidColor = new Vector4(0.95f, 0.25f, 0.2f, 0.35f),
                Preview = new AbilityIndicatorPreviewConfig
                {
                    PerformerId = "test.preview",
                    ScaleX = 3.5f,
                    ScaleY = 1.1f,
                    ScaleZ = 2.8f,
                    OffsetY = 0.4f,
                }
            });
            var actor = CreateActor(1004);

            _bridge.UpdateAiming(
                actor,
                new InputOrderMapping
                {
                    ActionId = "SkillQ",
                    SelectionType = OrderSelectionType.Position,
                    ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
                },
                hasCursorWorldCm: true,
                cursorWorldCm: new Vector3(250f, 0f, 300f),
                hoveredEntity: Entity.Null);

            Assert.That(_performers.ActiveCount, Is.EqualTo(1));
            Entity previewEntity = Entity.Null;
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity e, ref PerformerState s) => { previewEntity = e; });
            Assert.That(previewEntity, Is.Not.EqualTo(Entity.Null));
            ref readonly var preview = ref _world.Get<PerformerState>(previewEntity);
            Assert.That(preview.DefId, Is.EqualTo(_performerDefinitions.GetId("test.preview")));
            Assert.That(_world.Get<PerformerWorldPosition>(previewEntity).Value.X, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(_world.Get<PerformerWorldPosition>(previewEntity).Value.Y, Is.EqualTo(0.43f).Within(0.001f));
            Assert.That(_performers.ResolveFloat(previewEntity, WellKnownPerformerParamKeys.MarkerScaleX, -1f), Is.Not.EqualTo(-1f));
            float scaleX = _performers.ResolveFloat(previewEntity, WellKnownPerformerParamKeys.MarkerScaleX, -1f);
            Assert.That(scaleX, Is.EqualTo(3.5f).Within(0.001f));
            float alpha = _performers.ResolveFloat(previewEntity, WellKnownPerformerParamKeys.MarkerColorA, -1f);
            Assert.That(alpha, Is.GreaterThanOrEqualTo(0.3f));

            _bridge.ClearPreview();

            Assert.That(_performers.ActiveCount, Is.EqualTo(0));
        }

        private Entity CreateActor(int abilityId)
        {
            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);
            return _world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = 0f },
                abilities);
        }

        private void RegisterAbility(int abilityId, AbilityIndicatorConfig indicator)
        {
            _abilities.Register(abilityId, new AbilityDefinition
            {
                HasIndicator = true,
                Indicator = indicator
            });
        }
    }
}
