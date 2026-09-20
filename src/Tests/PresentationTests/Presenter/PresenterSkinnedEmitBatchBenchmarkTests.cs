using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterSkinnedEmitBatchBenchmarkTests
    {
        private const int Count = 10_000;
        private const int WarmupFrames = 20;
        private const int MeasuredFrames = 200;

        [Test]
        public void SkinnedPresenterEmit_TimingProfile()
        {
            SkinnedEmitTiming full = MeasureSkinnedEmit(includeAnimator: true);
            SkinnedEmitTiming withoutAnimator = MeasureSkinnedEmit(includeAnimator: false);

            TestContext.Out.WriteLine(BuildReport(full, withoutAnimator));

            Assert.That(full.BatchItemCount, Is.EqualTo(Count));
            Assert.That(full.OverflowDrops, Is.EqualTo(0));
            Assert.That(withoutAnimator.BatchItemCount, Is.EqualTo(Count));
        }

        private static SkinnedEmitTiming MeasureSkinnedEmit(bool includeAnimator)
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "benchmark.agent.locomotion",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 3, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                    ],
                    Transitions = [],
                });

            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register(
                "benchmark.agent.skinned_body",
                new PresenterDefinition
                {
                    AnimationProfileId = 55,
                    Behaviors = BuildBehaviors(controllerId, includeAnimator),
                });

            var instances = new PresenterEntityRuntime(world);
            var animatorStates = new PresenterAnimatorStateBuffer(Count + 8);
            var requests = new PresentationRequestBuffer();
            var batch = new SkinnedVisualBatchBuffer(Count + 64);

            instances.BindDefinitions(definitions);
            instances.BindAnimatorStates(animatorStates);

            var owners = new Entity[Count];
            var presenters = new Entity[Count];
            uint random = 0x9E3779B9u;
            for (int i = 0; i < Count; i++)
            {
                random = Next(random);
                float x = ((random >> 8) % 2000) * 0.25f - 250f;
                float z = ((random >> 20) % 2000) * 0.25f - 250f;
                owners[i] = world.Create(
                    new PresentationStableId { Value = 10_000 + i },
                    new VisualTransform
                    {
                        Position = new Vector3(x, 0f, z),
                        Rotation = Quaternion.CreateFromYawPitchRoll((random % 360) * MathF.PI / 180f, 0f, 0f),
                        Scale = Vector3.One,
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                presenters[i] = instances.Create(
                    defId,
                    owners[i],
                    scopeId: i + 1,
                    PresentationAnchorKind.Entity,
                    new Vector3(x, 0f, z),
                    stableId: 90_000 + i,
                    Entity.Null,
                    definition: null);
                world.Get<PresenterWorldRotation>(presenters[i]).Value =
                    Quaternion.CreateFromYawPitchRoll((random % 360) * MathF.PI / 180f, 0f, 0f);
            }

            using var animatorSystem = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            using var emitSystem = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                soundRequests: null,
                timingDiagnostics: null,
                stableDrawCache: null,
                skinnedVisualBatchBuffer: batch);

            const float dt = 1f / 30f;
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                if (includeAnimator)
                {
                    animatorSystem.Update(dt);
                }

                emitSystem.Update(dt);
            }

            var samples = new double[MeasuredFrames];
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                if (includeAnimator)
                {
                    animatorSystem.Update(dt);
                }

                long start = Stopwatch.GetTimestamp();
                emitSystem.Update(dt);
                samples[frame] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            }

            Array.Sort(samples);
            return new SkinnedEmitTiming(
                MedianMs: samples[samples.Length / 2],
                P95Ms: samples[(int)(samples.Length * 0.95)],
                MaxMs: samples[samples.Length - 1],
                BatchItemCount: batch.Count,
                OverflowDrops: batch.DroppedSinceClear);
        }

        private static BehaviorSlot[] BuildBehaviors(int controllerId, bool includeAnimator)
        {
            var assetSlot = new BehaviorSlot
            {
                SlotIndex = 1,
                Kind = BehaviorKind.AssetBinding,
                ActiveByDefault = true,
                Style = new BehaviorStyleConfig { Color = new Vector4(0.38f, 0.72f, 1f, 1f), HasColor = true },
                AssetBinding = new AssetBindingConfig
                {
                    AssetKind = AssetKind.SkinnedMesh,
                    AssetId = 401,
                    MaterialId = 402,
                    RenderPath = VisualRenderPath.GpuSkinnedInstance,
                    Mobility = VisualMobility.Movable,
                    LocalScale = new Vector3(0.45f, 0.45f, 0.45f),
                    AssetIdParamKey = -1,
                },
            };

            if (!includeAnimator)
            {
                assetSlot.SlotIndex = 0;
                return [assetSlot];
            }

            return
            [
                new BehaviorSlot
                {
                    SlotIndex = 0,
                    Kind = BehaviorKind.Animator,
                    ActiveByDefault = true,
                    Animator = new AnimatorConfig
                    {
                        AnimatorControllerId = controllerId,
                        AnimationProfileId = 55,
                        SpeedParamKey = -1,
                        StateParamKey = -1,
                    },
                },
                assetSlot,
            ];
        }

        private static uint Next(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static string BuildReport(SkinnedEmitTiming full, SkinnedEmitTiming withoutAnimator)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Skinned presenter emit headless micro-benchmark");
            builder.AppendLine();
            builder.AppendLine($"count={Count} frames={MeasuredFrames} warmup={WarmupFrames}");
            builder.AppendLine();
            builder.AppendLine("| scenario | median ms | p95 ms | max ms | items | drops |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
            builder.AppendLine($"| animator+gpu-skinned body | {full.MedianMs:F3} | {full.P95Ms:F3} | {full.MaxMs:F3} | {full.BatchItemCount} | {full.OverflowDrops} |");
            builder.AppendLine($"| gpu-skinned body only | {withoutAnimator.MedianMs:F3} | {withoutAnimator.P95Ms:F3} | {withoutAnimator.MaxMs:F3} | {withoutAnimator.BatchItemCount} | {withoutAnimator.OverflowDrops} |");
            return builder.ToString();
        }

        private readonly record struct SkinnedEmitTiming(
            double MedianMs,
            double P95Ms,
            double MaxMs,
            int BatchItemCount,
            int OverflowDrops);
    }
}
