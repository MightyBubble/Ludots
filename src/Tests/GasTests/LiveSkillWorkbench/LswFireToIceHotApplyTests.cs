using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.LiveSkillWorkbench
{
    [TestFixture]
    public sealed class LswFireToIceHotApplyTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        [Category("ci-gate")]
        public void HotApply_FireboltToIcebolt_SwapsImpactPresentationAndDamage()
        {
            int fireLaunch = EffectTemplateIdRegistry.Register("Effect.Champion.Ezreal.MysticShot");
            int fireHit = EffectTemplateIdRegistry.Register("Effect.Champion.Ezreal.MysticShotHit");
            int iceHit = EffectTemplateIdRegistry.Register("Effect.LSW.IceballHit");
            int icePresentation = EffectTemplateIdRegistry.Register("Effect.Champion.Ezreal.EssenceFlux");

            var hit = new EffectTemplateData { PresetType = EffectPresetType.InstantDamage };
            unsafe
            {
                Assert.That(hit.Modifiers.Add(attrId: 1, ModifierOp.Add, -15f), Is.True);
            }

            var effects = new EffectTemplateRegistry();
            effects.Register(fireLaunch, new EffectTemplateData
            {
                PresetType = EffectPresetType.LaunchProjectile,
                Projectile = new ProjectileDescriptor
                {
                    Speed = 2100,
                    Range = 840,
                    ImpactEffectTemplateId = fireHit,
                    HitEffectTemplateId = fireHit,
                    PresentationEffectTemplateId = fireLaunch,
                }
            });
            effects.Register(fireHit, hit);
            effects.Register(iceHit, new EffectTemplateData { PresetType = EffectPresetType.Buff });
            effects.Register(icePresentation, new EffectTemplateData { PresetType = EffectPresetType.LaunchProjectile });

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            var prov = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://fire-to-ice");

            Assert.That(session.TryStage(LiveDebugPatchOperation.EffectTemplateRef(
                "Effect.Champion.Ezreal.MysticShot",
                "projectile.impactEffect",
                "Effect.LSW.IceballHit",
                prov)).Succeeded, Is.True);
            Assert.That(session.TryStage(LiveDebugPatchOperation.EffectTemplateRef(
                "Effect.Champion.Ezreal.MysticShot",
                "projectile.presentationEffect",
                "Effect.Champion.Ezreal.EssenceFlux",
                prov)).Succeeded, Is.True);
            Assert.That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                "Effect.Champion.Ezreal.MysticShotHit",
                "modifiers.0.value",
                -45d,
                prov)).Succeeded, Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            Assert.That(report.CanCommitNextCast, Is.True);
            Assert.That(report.Items, Has.All.Property("Mode").EqualTo(LiveApplyMode.NextCastLiveApply));

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult commit = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            Assert.That(commit.Succeeded, Is.True);
            Assert.That(commit.AppliedCount, Is.EqualTo(3));

            Assert.That(effects.TryGet(fireLaunch, out EffectTemplateData afterLaunch), Is.True);
            Assert.That(afterLaunch.Projectile.ImpactEffectTemplateId, Is.EqualTo(iceHit));
            Assert.That(afterLaunch.Projectile.PresentationEffectTemplateId, Is.EqualTo(icePresentation));
            Assert.That(effects.TryGet(fireHit, out EffectTemplateData afterHit), Is.True);
            unsafe
            {
                Assert.That(afterHit.Modifiers.Values[0], Is.EqualTo(-45f).Within(0.001f));
            }
        }
    }
}
