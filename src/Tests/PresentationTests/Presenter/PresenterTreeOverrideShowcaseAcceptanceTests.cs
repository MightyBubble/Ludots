using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Ludots.Tests.TestCommon;
using PresenterTreeOverrideShowcaseMod;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// 表现树覆盖演示的逐案例验收：每条用例对应演示地图里玩家能看到的一处现象，
    /// 断言读的是表现实例的世界位姿和本帧提交给渲染的图元，不读中间配置。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterTreeOverrideShowcaseAcceptanceTests
    {
        private const string MapId = "presenter_tree_override_showcase";
        private const float PositionTolerance = 0.02f;

        private static readonly string[] Mods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "RaylibPlatformMeshesMod",
            "PresenterTreeOverrideShowcaseMod",
        };

        private sealed record PresenterView(
            Entity Entity,
            string Definition,
            string Owner,
            Vector3 Position,
            Quaternion Rotation,
            Vector3 Scale,
            Entity Parent);

        private static GameEngine CreateLoadedEngine(int frames = 12)
        {
            _ = typeof(PresenterTreeOverrideShowcaseModEntry).Assembly;
            GameEngine engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine(Mods);
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, MapId, frames);
            return engine;
        }

        private static List<PresenterView> SnapshotPresenters(GameEngine engine)
        {
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            World world = engine.World;
            var result = new List<PresenterView>();
            var query = new QueryDescription().WithAll<PresenterState, PresenterWorldPosition>();
            world.Query(in query, (Entity entity, ref PresenterState state, ref PresenterWorldPosition position) =>
            {
                string owner = world.IsAlive(state.OwnerEntity) && world.Has<PlacedInstanceId>(state.OwnerEntity)
                    ? world.Get<PlacedInstanceId>(state.OwnerEntity).Value
                    : string.Empty;
                result.Add(new PresenterView(
                    entity,
                    defs.GetName(state.DefId),
                    owner,
                    position.Value,
                    world.Has<PresenterWorldRotation>(entity) ? world.Get<PresenterWorldRotation>(entity).Value : Quaternion.Identity,
                    world.Has<PresenterWorldScale>(entity) ? world.Get<PresenterWorldScale>(entity).Value : Vector3.One,
                    world.Has<PresenterParent>(entity) ? world.Get<PresenterParent>(entity).Parent : Entity.Null));
            });
            return result;
        }

        private static PresenterView Single(List<PresenterView> presenters, string definition, string owner)
        {
            PresenterView[] matches = presenters.Where(p => p.Definition == definition && p.Owner == owner).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), $"Expected exactly one '{definition}' presenter owned by '{owner}'.");
            return matches[0];
        }

        private static PresenterView[] ByX(List<PresenterView> presenters, string definition, string owner, int expectedCount)
        {
            PresenterView[] matches = presenters
                .Where(p => p.Definition == definition && p.Owner == owner)
                .OrderBy(p => p.Position.X)
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(expectedCount), $"'{definition}' presenter count under '{owner}'.");
            return matches;
        }

        private static Vector4 SubmittedColor(GameEngine engine, PresenterView presenter)
        {
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PrimitiveDrawBuffer missing.");
            int defId = defs.GetId(presenter.Definition);
            var anchor = new Vector2(presenter.Position.X, presenter.Position.Z);
            float best = float.MaxValue;
            Vector4 color = default;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                float distance = Vector2.Distance(new Vector2(item.Position.X, item.Position.Z), anchor);
                if (item.TemplateId == defId && distance < best)
                {
                    best = distance;
                    color = item.Color;
                }
            }

            Assert.That(best, Is.LessThan(0.5f), $"No '{presenter.Definition}' primitive submitted at {presenter.Position}.");
            return color;
        }

        private static Entity FindPlaced(GameEngine engine, string placedId)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<PlacedInstanceId>();
            engine.World.Query(in query, (Entity entity, ref PlacedInstanceId id) =>
            {
                if (id.Value == placedId)
                {
                    found = entity;
                }
            });
            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Placed entity '{placedId}' not found.");
            return found;
        }

        private static Vector2 LogicPositionCm(GameEngine engine, Entity entity)
        {
            var position = engine.World.Get<WorldPositionCm>(entity).Value;
            return new Vector2(position.X.ToFloat(), position.Y.ToFloat());
        }

        private static void AssertNear(Vector3 actual, Vector3 expected, string because)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(PositionTolerance),
                $"{because}: expected {expected}, got {actual}.");
        }

        private static void AssertColor(Vector4 actual, Vector4 expected, string because)
        {
            Assert.That(Vector4.Distance(actual, expected), Is.LessThan(0.01f),
                $"{because}: expected {expected}, got {actual}.");
        }

        [Test]
        public void A1_WholeTree_EveryPartLandsAtItsAuthoredSpot()
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView root = Single(p, "ptree.a1.root", "ptree.a1");

            AssertNear(Single(p, "ptree.a1.house", "ptree.a1").Position, root.Position, "house sits on the station anchor");
            PresenterView furnace = Single(p, "ptree.a1.furnace", "ptree.a1");
            AssertNear(furnace.Position, root.Position + new Vector3(2.4f, 0f, 0.6f), "furnace override moves it beside the house");
            AssertNear(furnace.Scale, new Vector3(0.6f), "furnace override shrinks it");
            AssertNear(Single(p, "ptree.a1.banner", "ptree.a1").Position, root.Position + new Vector3(-2.4f, 0f, 0f), "banner override");
            Assert.That(p.Count(x => x.Definition == "ptree.a1.banner_cloth" && x.Owner == "ptree.a1"), Is.EqualTo(1),
                "banner brings its own grandchild cloth");
        }

        [Test]
        public void A2_SameLamppost_RightCopyIsMovedAndScaled()
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView root = Single(p, "ptree.a2.root", "ptree.a2");
            PresenterView[] posts = ByX(p, "ptree.a2.lamppost", "ptree.a2", 2);

            AssertNear(posts[0].Position, root.Position + new Vector3(-1.4f, 0f, 0f), "left lamppost");
            AssertNear(posts[0].Scale, new Vector3(0.7f), "left lamppost scale");
            AssertNear(posts[1].Position, root.Position + new Vector3(1.8f, 0f, 0f), "right lamppost");
            AssertNear(posts[1].Scale, new Vector3(1.4f), "right lamppost scale");
        }

        [Test]
        public void A3_SameCrystal_RightCopyIsRecoloredByParamOverride()
        {
            using GameEngine engine = CreateLoadedEngine();
            PresenterView[] crystals = ByX(SnapshotPresenters(engine), "ptree.a3.crystal", "ptree.a3", 2);

            AssertColor(SubmittedColor(engine, crystals[0]), new Vector4(0.32f, 0.6f, 1f, 1f), "left crystal keeps the default blue");
            AssertColor(SubmittedColor(engine, crystals[1]), new Vector4(1f, 0.28f, 0.22f, 1f), "right crystal param override");
        }

        [Test]
        public void A4_SameTower_RightCopyReplacesItsTop()
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView[] towers = ByX(p, "ptree.a4.tower", "ptree.a4", 2);
            PresenterView orb = Single(p, "ptree.a4.top_orb", "ptree.a4");
            PresenterView flag = Single(p, "ptree.a4.top_flag", "ptree.a4");

            Assert.That(orb.Parent, Is.EqualTo(towers[0].Entity), "left tower keeps the definition's orb top");
            Assert.That(flag.Parent, Is.EqualTo(towers[1].Entity), "right tower's instanceChildren replace the orb with a flag");
        }

        [Test]
        public void A5_SameOrb_RightCopyGetsAnInstanceAttachment()
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView root = Single(p, "ptree.a5.root", "ptree.a5");
            PresenterView[] orbs = ByX(p, "ptree.a5.orb", "ptree.a5", 2);

            AssertNear(orbs[0].Position, root.Position + new Vector3(-1.4f, 0f, 0f), "left orb rests on its base");
            AssertNear(orbs[1].Position, root.Position + new Vector3(1.4f, 1.5f, 0f), "right orb floats by its instance attachment");
        }

        [TestCase("ptree.a1")]
        [TestCase("ptree.a3")]
        [TestCase("ptree.camp.north")]
        [TestCase("ptree.beacon.red")]
        [TestCase("ptree.knight")]
        public void Label_OnStaticOwner_FloatsAtItsAttachmentOffset(string owner)
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView label = Single(p, "ptree.label.high", owner);
            PresenterView root = p.Single(x => x.Owner == owner && x.Parent == Entity.Null);

            AssertNear(label.Position, root.Position + new Vector3(0f, 3.6f, 0f), $"title of static '{owner}' hovers 3.6 m above it");
        }

        [Test]
        public void Label_OnMovingOwner_FloatsAtItsAttachmentOffset()
        {
            using GameEngine engine = CreateLoadedEngine(frames: 90);
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView cart = Single(p, "ptree.cart.root", "ptree.cart.outer");

            AssertNear(Single(p, "ptree.label.high", "ptree.cart.outer").Position, cart.Position + new Vector3(0f, 3.6f, 0f),
                "title of a moving cart hovers 3.6 m above it");
        }

        [Test]
        public void B1_RoofLamp_RidesTheMovingCartAtItsAttachmentOffset()
        {
            using GameEngine engine = CreateLoadedEngine(frames: 90);
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView cart = Single(p, "ptree.cart.root", "ptree.cart.inner");
            PresenterView roof = p.Single(x => x.Definition == "ptree.cart.lamp" && x.Owner == "ptree.cart.inner" &&
                                               !engine.World.Has<PresenterInstanceTransformOverride>(x.Entity));

            AssertNear(roof.Position, cart.Position + Vector3.Transform(new Vector3(0f, 1.55f, 0f), cart.Rotation), "roof lamp");
            AssertColor(SubmittedColor(engine, roof), new Vector4(1f, 0.86f, 0.3f, 1f), "roof lamp keeps the default amber");
        }

        [Test]
        public void B3_RearLamp_StacksItsOverrideOnTopOfTheAttachment()
        {
            using GameEngine engine = CreateLoadedEngine(frames: 90);
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView cart = Single(p, "ptree.cart.root", "ptree.cart.inner");
            PresenterView rear = p.Single(x => x.Definition == "ptree.cart.lamp" && x.Owner == "ptree.cart.inner" &&
                                               engine.World.Has<PresenterInstanceTransformOverride>(x.Entity));

            AssertNear(rear.Position, cart.Position + Vector3.Transform(new Vector3(-1.2f, 1.55f, 0f), cart.Rotation),
                "rear lamp = attachment offset, then its own override along the cart's facing");
            AssertColor(SubmittedColor(engine, rear), new Vector4(1f, 0.28f, 0.22f, 1f), "rear lamp param override");
        }

        [Test]
        public void C1_CartFlags_KeepTheirOverridesWhileTheCartMoves()
        {
            using GameEngine engine = CreateLoadedEngine();
            Entity cartEntity = FindPlaced(engine, "ptree.cart.inner");
            Vector2 before = LogicPositionCm(engine, cartEntity);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 120);
            Assert.That(Vector2.Distance(LogicPositionCm(engine, cartEntity), before), Is.GreaterThan(50f), "the cart must actually move");

            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView cart = Single(p, "ptree.cart.root", "ptree.cart.inner");
            Vector3 left = cart.Position + Vector3.Transform(new Vector3(-0.6f, 0f, 0.85f), cart.Rotation);
            Vector3 right = cart.Position + Vector3.Transform(new Vector3(-0.6f, 0f, -0.85f), cart.Rotation);
            PresenterView[] flags = p.Where(x => x.Definition == "ptree.cart.flag" && x.Owner == "ptree.cart.inner").ToArray();

            Assert.That(flags, Has.Length.EqualTo(2));
            Assert.That(flags.Any(f => Vector3.Distance(f.Position, left) < PositionTolerance), Is.True,
                $"one flag on the left rear corner {left}; got {string.Join(", ", flags.Select(f => f.Position))}");
            Assert.That(flags.Any(f => Vector3.Distance(f.Position, right) < PositionTolerance), Is.True,
                $"one flag on the right rear corner {right}; got {string.Join(", ", flags.Select(f => f.Position))}");
            foreach (PresenterView flag in flags)
            {
                AssertNear(flag.Scale, new Vector3(0.8f), "flag override scale survives movement");
            }
        }

        [Test]
        public void B2_Sword_IsDeclaredOnTheKnightsHandBone()
        {
            using GameEngine engine = CreateLoadedEngine();
            List<PresenterView> p = SnapshotPresenters(engine);
            PresenterView knight = Single(p, "ptree.knight.root", "ptree.knight");
            PresenterView sword = Single(p, "ptree.knight.sword", "ptree.knight");

            Assert.That(sword.Parent, Is.EqualTo(knight.Entity), "sword hangs under the knight");
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            PresenterDefinition swordDef = defs.Get(defs.GetId("ptree.knight.sword"));
            BehaviorSlot[] attachments = swordDef.Behaviors.Where(b => b.Kind == BehaviorKind.Attachment).ToArray();
            Assert.That(attachments, Has.Length.EqualTo(1));
            AttachmentConfig attachment = attachments[0].Attachment;
            Assert.That(attachment.Target, Is.EqualTo(AttachmentTarget.Bone));
            Assert.That(attachment.BoneId, Is.EqualTo(13), "Knight.glb skin joint 13 is handslot.r");

            int stableId = engine.World.Get<PresenterState>(knight.Entity).StableId;
            var hand = new HandBoneStub(stableId, boneId: 13);
            engine.SetService(CoreServiceKeys.BoneTransformProvider, (IBoneTransformProvider)hand);
            hand.Position = new Vector3(4f, 5f, 6f);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 2);
            sword = Single(SnapshotPresenters(engine), "ptree.knight.sword", "ptree.knight");
            AssertNear(sword.Position, hand.Position, "sword presenter lands on the hand bone the provider reports");

            hand.Position = new Vector3(-2f, 1.5f, 3f);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 2);
            sword = Single(SnapshotPresenters(engine), "ptree.knight.sword", "ptree.knight");
            AssertNear(sword.Position, hand.Position, "sword presenter follows a later hand bone pose");
        }

        [Test]
        public void D1_SecondCart_ReplacesTheWholeOrbitComponent()
        {
            using GameEngine engine = CreateLoadedEngine();
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 60);
            var center = new Vector2(2000f, 2600f);

            Assert.That(Vector2.Distance(LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.inner")), center), Is.EqualTo(450f).Within(2f),
                "inner cart keeps the template orbit radius");
            Assert.That(Vector2.Distance(LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.outer")), center), Is.EqualTo(850f).Within(2f),
                "outer cart's placement override swaps in a wider orbit");

            Vector2 innerBefore = LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.inner"));
            Vector2 outerBefore = LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.outer"));
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 60);
            float innerSweep = SweepRadians(center, innerBefore, LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.inner")));
            float outerSweep = SweepRadians(center, outerBefore, LogicPositionCm(engine, FindPlaced(engine, "ptree.cart.outer")));
            Assert.That(outerSweep, Is.GreaterThan(innerSweep * 1.2f),
                "the outer cart's 10 second lap sweeps farther in the same second than the inner cart's 14 second lap");
        }

        [Test]
        public void D2_SameCampTemplate_EachTentCarriesItsOwnTitle()
        {
            using GameEngine engine = CreateLoadedEngine();
            Entity northTent = FindPlaced(engine, "ptree.camp.north.hq");
            Entity harborTent = FindPlaced(engine, "ptree.camp.harbor.hq");

            Assert.That(engine.World.Get<EntityInfoTitleToken>(northTent).Value, Is.EqualTo("ptree.title.tent_north"));
            Assert.That(engine.World.Get<EntityInfoTitleToken>(harborTent).Value, Is.EqualTo("ptree.title.tent_harbor"));
            List<PresenterView> p = SnapshotPresenters(engine);
            Assert.That(p.Count(x => x.Definition == "ptree.camp.tent.root"), Is.EqualTo(2), "both template-child tents get a visual");
            Assert.That(engine.World.Has<VisualTransform>(northTent), Is.True, "a template child without its own WorldPositionCm still gets a visual transform from its attachment");
            Assert.That(Vector2.Distance(LogicPositionCm(engine, northTent), LogicPositionCm(engine, FindPlaced(engine, "ptree.camp.north"))), Is.EqualTo(60f).Within(2f),
                "the tent sits on the template local pose, 60 cm south of the camp");
            Assert.That(p.Any(x => x.Definition == "ptree.label.mid" && x.Owner == "ptree.camp.north.hq"), Is.True,
                "the north tent carries its own title presenter");
        }

        [TestCase("ptree.beacon.red", 1f, 0.25f, 0.2f, 3600f)]
        [TestCase("ptree.beacon.green", 0.3f, 0.95f, 0.4f, 4400f)]
        [TestCase("ptree.beacon.blue", 0.3f, 0.55f, 1f, 5200f)]
        public void D4_BatchPlacedBeacons_EachLightReadsItsPlacementParam(string owner, float r, float g, float b, float placementXCm)
        {
            using GameEngine engine = CreateLoadedEngine();
            PresenterView light = Single(SnapshotPresenters(engine), "ptree.beacon.light", owner);

            AssertColor(SubmittedColor(engine, light), new Vector4(r, g, b, 1f), $"{owner} light colour");
            Vector2 position = LogicPositionCm(engine, FindPlaced(engine, owner));
            Assert.That(position.X, Is.EqualTo(placementXCm).Within(0.5f), $"{owner} batch placement honours PositionXCm");
            Assert.That(position.Y, Is.EqualTo(4300f).Within(0.5f), $"{owner} batch placement honours PositionYCm");
        }

        [Test]
        public void D6_Rider_BoardsTheCartThenStepsOffAroundIt()
        {
            using GameEngine engine = CreateLoadedEngine();
            Entity rider = FindPlaced(engine, "ptree.rider");
            Entity cart = FindPlaced(engine, "ptree.cart.inner");

            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 180);
            Assert.That(engine.World.Has<ChildOf>(rider), Is.True, "rider is aboard at t≈3s");
            Assert.That(engine.World.Get<ChildOf>(rider).Parent, Is.EqualTo(cart));
            Assert.That(Vector2.Distance(LogicPositionCm(engine, rider), LogicPositionCm(engine, cart)), Is.EqualTo(20f).Within(1f),
                "rider sits on the seat offset");

            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 300);
            Assert.That(engine.World.Has<ChildOf>(rider), Is.False, "rider has stepped off at t=8s");
            Assert.That(Vector2.Distance(LogicPositionCm(engine, rider), LogicPositionCm(engine, cart)), Is.EqualTo(220f).Within(40f),
                "rider steps off on the 220 cm ring around the cart");
            Vector2 parked = LogicPositionCm(engine, rider);
            Vector2 cartAtAlight = LogicPositionCm(engine, cart);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 60);
            Assert.That(Vector2.Distance(LogicPositionCm(engine, rider), parked), Is.LessThan(1f),
                "after stepping off, the rider stays put");
            Assert.That(Vector2.Distance(LogicPositionCm(engine, cart), cartAtAlight), Is.GreaterThan(100f),
                "the cart keeps circling after the rider has stepped off");
        }

        [Test]
        public void CameraTour_VisitsOverviewThenEveryCaseStation()
        {
            using GameEngine engine = CreateLoadedEngine(frames: 1);
            var targets = new List<Vector2>();
            for (int frame = 0; frame < 60 * 52; frame++)
            {
                PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                Vector2 target = engine.AuthorityCamera().State.TargetCm;
                if (targets.Count == 0 || Vector2.Distance(targets[^1], target) > 1f)
                {
                    targets.Add(target);
                }
            }

            Assert.That(targets, Has.Count.GreaterThanOrEqualTo(11), "overview + 9 stops, then back to overview");
            Assert.That(targets[0], Is.EqualTo(new Vector2(3100f, 2700f)), "tour starts on the overview");
            Assert.That(targets[1], Is.EqualTo(new Vector2(900f, 1000f)), "first stop is A1");
            Assert.That(targets[10], Is.EqualTo(new Vector2(3100f, 2700f)), "tour loops back to the overview");
        }

        private static float SweepRadians(Vector2 center, Vector2 from, Vector2 to)
        {
            float start = MathF.Atan2(from.Y - center.Y, from.X - center.X);
            float end = MathF.Atan2(to.Y - center.Y, to.X - center.X);
            float delta = end - start;
            if (delta > MathF.PI)
            {
                delta -= MathF.PI * 2f;
            }
            else if (delta < -MathF.PI)
            {
                delta += MathF.PI * 2f;
            }

            return MathF.Abs(delta);
        }

        private sealed class HandBoneStub : IBoneTransformProvider
        {
            private readonly int _stableId;
            private readonly int _boneId;

            public HandBoneStub(int stableId, int boneId)
            {
                _stableId = stableId;
                _boneId = boneId;
            }

            public Vector3 Position { get; set; }

            public bool TryGetBoneWorldTransform(int stableId, int boneId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
            {
                position = Position;
                rotation = Quaternion.Identity;
                scale = Vector3.One;
                return stableId == _stableId && boneId == _boneId;
            }
        }
    }
}
