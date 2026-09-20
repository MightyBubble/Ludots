using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterCreationControlVariableTests
    {
        private const int EntityCount = 30_000;
        private const string RootDefinitionKey = "blacksmith_root";

        [Test]
        public void Benchmark_30kCreation_ControlVariables_WritesReport()
        {
            EntityOnlyResult entityOnlyNaive = MeasureEntityOnlyCreation(CreateOwnersNaive);
            EntityOnlyResult entityOnlyBulkAllocate = MeasureEntityOnlyCreation(CreateOwnersBulkAllocateOnly);
            EntityOnlyResult entityOnlyBulkSet = MeasureEntityOnlyCreation(CreateOwnersBulkAndSetComponents);
            EntityOnlyResult entityOnlyBulkPayload = MeasureEntityOnlyCreation(CreateOwnersBulkWithPayload);
            EntityPlusPresenterResult entityPlusPresenter = MeasureEntityPlusPresenterCreation();

            string repoRoot = PresenterBlacksmithShowcaseTestHarness.FindRepoRoot();
            string artifactDir = Path.Combine(
                repoRoot,
                "artifacts",
                "benchmarks",
                "presenter-creation-control-variables");
            Directory.CreateDirectory(artifactDir);

            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            File.WriteAllText(reportPath, BuildReport(entityOnlyNaive, entityOnlyBulkAllocate, entityOnlyBulkSet, entityOnlyBulkPayload, entityPlusPresenter));
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.That(entityOnlyNaive.CreatedCount, Is.EqualTo(EntityCount));
            Assert.That(entityOnlyBulkAllocate.CreatedCount, Is.EqualTo(EntityCount));
            Assert.That(entityOnlyBulkSet.CreatedCount, Is.EqualTo(EntityCount));
            Assert.That(entityOnlyBulkPayload.CreatedCount, Is.EqualTo(EntityCount));
            Assert.That(entityPlusPresenter.CreatedOwners, Is.EqualTo(EntityCount));
            Assert.That(entityPlusPresenter.CreatedPresenters, Is.EqualTo(EntityCount));
            Assert.That(entityPlusPresenter.PresenterActiveCount, Is.EqualTo(EntityCount));
        }

        private static EntityOnlyResult MeasureEntityOnlyCreation(Func<World, int, Entity[]> createOwners)
        {
            using var world = World.Create();
            long start = Stopwatch.GetTimestamp();
            Entity[] owners = createOwners(world, EntityCount);
            double elapsedMs = ElapsedMs(start);

            return new EntityOnlyResult(
                owners.Length,
                elapsedMs,
                elapsedMs / Math.Max(1, owners.Length));
        }

        private static EntityPlusPresenterResult MeasureEntityPlusPresenterCreation()
        {
            using var world = World.Create();
            Entity[] owners = CreateOwnersBulkAndSetComponents(world, EntityCount);

            var presenters = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int definitionId = definitions.Register(RootDefinitionKey, new PresenterDefinition
            {
                Key = RootDefinitionKey,
                Behaviors = Array.Empty<BehaviorSlot>(),
                Rules = Array.Empty<PresenterRule>(),
                Bindings = Array.Empty<PresenterParamBinding>(),
                ParamDefaults = Array.Empty<ParamDefault>(),
                Children = Array.Empty<ChildPresenterRef>(),
            });

            var commandBuffer = new PresenterCommandBuffer(EntityCount + 16);
            var eventStream = new PresentationEventStream(EntityCount + 16);
            var markers = new TransientMarkerBuffer(16);
            var requestBuffer = new PresentationRequestBuffer(16);
            var stableIds = new PresentationStableIdAllocator();
            var runtimeSystem = new PresenterRuntimeSystem(
                world,
                commandBuffer,
                eventStream,
                markers,
                requestBuffer,
                presenters,
                stableIds,
                definitions);

            for (int i = 0; i < owners.Length; i++)
            {
                PresenterCommand command = new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    PresenterDefinitionId = definitionId,
                    Source = owners[i],
                    Target = owners[i],
                    AnchorKind = PresentationAnchorKind.Entity,
                    Position = Vector3.Zero,
                    ScopeTag = i + 1,
                    ScopeSource = PresenterCommandScopeSource.Fixed,
                };

                if (!commandBuffer.TryAdd(in command))
                {
                    throw new InvalidOperationException("PresenterCommandBuffer overflowed during control-variable benchmark.");
                }
            }

            long start = Stopwatch.GetTimestamp();
            runtimeSystem.Update(0f);
            double elapsedMs = ElapsedMs(start);

            return new EntityPlusPresenterResult(
                owners.Length,
                presenters.ActiveCount,
                presenters.ActiveCount,
                elapsedMs,
                elapsedMs / Math.Max(1, owners.Length));
        }

        private static Entity[] CreateOwnersNaive(World world, int count)
        {
            var owners = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                int column = i % 300;
                int row = i / 300;
                owners[i] = world.Create(
                    new Name { Value = "blacksmith_building" },
                    new WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(column * 220, row * 220) },
                    new PreviousWorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(column * 220, row * 220) },
                    new FacingDirection { AngleRad = 0f },
                    VisualTransform.Default,
                    new CullState { IsVisible = false, LOD = LODLevel.Low },
                    default(AttributeBuffer),
                    default(GameplayTagContainer),
                    default(TagCountContainer),
                    new PresentationStableId { Value = i + 1 });
            }

            return owners;
        }

        private static Entity[] CreateOwnersBulkAllocateOnly(World world, int count)
        {
            var owners = new Entity[count];
            Signature signature = OwnerSignature();
            world.Create(owners.AsSpan(), in signature, count);
            return owners;
        }

        private static Entity[] CreateOwnersBulkAndSetComponents(World world, int count)
        {
            Entity[] owners = CreateOwnersBulkAllocateOnly(world, count);
            for (int i = 0; i < count; i++)
            {
                SetOwnerComponents(world, owners[i], i);
            }

            return owners;
        }

        private static Entity[] CreateOwnersBulkWithPayload(World world, int count)
        {
            world.Create(
                count,
                new Name { Value = "blacksmith_building" },
                default(WorldPositionCm),
                default(PreviousWorldPositionCm),
                new FacingDirection { AngleRad = 0f },
                VisualTransform.Default,
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                default(AttributeBuffer),
                default(GameplayTagContainer),
                default(TagCountContainer),
                default(PresentationStableId));

            var query = new QueryDescription().WithAll<
                Name,
                WorldPositionCm,
                PreviousWorldPositionCm,
                FacingDirection,
                VisualTransform,
                CullState,
                AttributeBuffer,
                GameplayTagContainer,
                TagCountContainer,
                PresentationStableId>();
            var owners = new Entity[count];
            world.GetEntities(in query, owners);
            for (int i = 0; i < owners.Length; i++)
            {
                int column = i % 300;
                int row = i / 300;
                var position = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(column * 220, row * 220);
                world.Set(owners[i], new WorldPositionCm { Value = position });
                world.Set(owners[i], new PreviousWorldPositionCm { Value = position });
                world.Set(owners[i], new PresentationStableId { Value = i + 1 });
            }

            return owners;
        }

        private static Signature OwnerSignature()
        {
            return
                Component<Name>.Signature +
                Component<WorldPositionCm>.Signature +
                Component<PreviousWorldPositionCm>.Signature +
                Component<FacingDirection>.Signature +
                Component<VisualTransform>.Signature +
                Component<CullState>.Signature +
                Component<AttributeBuffer>.Signature +
                Component<GameplayTagContainer>.Signature +
                Component<TagCountContainer>.Signature +
                Component<PresentationStableId>.Signature;
        }

        private static void SetOwnerComponents(World world, Entity owner, int index)
        {
            int column = index % 300;
            int row = index / 300;
            var position = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(column * 220, row * 220);
            world.Set(
                owner,
                new Name { Value = "blacksmith_building" },
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                new FacingDirection { AngleRad = 0f },
                VisualTransform.Default,
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                default(AttributeBuffer),
                default(GameplayTagContainer),
                default(TagCountContainer),
                new PresentationStableId { Value = index + 1 });
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private static string BuildReport(
            EntityOnlyResult entityOnlyNaive,
            EntityOnlyResult entityOnlyBulkAllocate,
            EntityOnlyResult entityOnlyBulkSet,
            EntityOnlyResult entityOnlyBulkPayload,
            EntityPlusPresenterResult entityPlusPresenter)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Presenter Creation Control Variables");
            sb.AppendLine();
            sb.AppendLine($"- sample size: `{EntityCount}` entities");
            sb.AppendLine("- excludes: mesh emit, HUD projection, culling, skia, raylib");
            sb.AppendLine("- goal: isolate pure creation cost before touching render-path optimization");
            sb.AppendLine();
            sb.AppendLine("## 30K Entity Only - Naive Per Entity Create");
            sb.AppendLine();
            sb.AppendLine($"- created entities: `{entityOnlyNaive.CreatedCount}`");
            sb.AppendLine($"- total create time: `{entityOnlyNaive.ElapsedMs:F4} ms`");
            sb.AppendLine($"- per entity: `{entityOnlyNaive.MsPerEntity:F6} ms`");
            sb.AppendLine();
            sb.AppendLine("## 30K Entity Only - Bulk Allocate Only");
            sb.AppendLine();
            sb.AppendLine($"- created entities: `{entityOnlyBulkAllocate.CreatedCount}`");
            sb.AppendLine($"- total create time: `{entityOnlyBulkAllocate.ElapsedMs:F4} ms`");
            sb.AppendLine($"- per entity: `{entityOnlyBulkAllocate.MsPerEntity:F6} ms`");
            sb.AppendLine();
            sb.AppendLine("## 30K Entity Only - Bulk Allocate + Component Set");
            sb.AppendLine();
            sb.AppendLine($"- created entities: `{entityOnlyBulkSet.CreatedCount}`");
            sb.AppendLine($"- total create time: `{entityOnlyBulkSet.ElapsedMs:F4} ms`");
            sb.AppendLine($"- per entity: `{entityOnlyBulkSet.MsPerEntity:F6} ms`");
            sb.AppendLine();
            sb.AppendLine("## 30K Entity Only - Bulk Create With Shared Payload");
            sb.AppendLine();
            sb.AppendLine($"- created entities: `{entityOnlyBulkPayload.CreatedCount}`");
            sb.AppendLine($"- total create time: `{entityOnlyBulkPayload.ElapsedMs:F4} ms`");
            sb.AppendLine($"- per entity: `{entityOnlyBulkPayload.MsPerEntity:F6} ms`");
            sb.AppendLine("- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads");
            sb.AppendLine();
            sb.AppendLine("## 30K Entity + Presenter (No Mesh)");
            sb.AppendLine();
            sb.AppendLine("- owners are created with the bulk allocate + component set path before timing starts");
            sb.AppendLine($"- created owners: `{entityPlusPresenter.CreatedOwners}`");
            sb.AppendLine($"- created presenters: `{entityPlusPresenter.CreatedPresenters}`");
            sb.AppendLine($"- presenter active count: `{entityPlusPresenter.PresenterActiveCount}`");
            sb.AppendLine($"- total create time: `{entityPlusPresenter.ElapsedMs:F4} ms`");
            sb.AppendLine($"- per owner: `{entityPlusPresenter.MsPerOwner:F6} ms`");
            sb.AppendLine();
            sb.AppendLine("## Delta");
            sb.AppendLine();
            sb.AppendLine($"- saved by bulk allocation before component writes: `{(entityOnlyNaive.ElapsedMs - entityOnlyBulkAllocate.ElapsedMs):F4} ms`");
            sb.AppendLine($"- component write cost after bulk allocation: `{(entityOnlyBulkSet.ElapsedMs - entityOnlyBulkAllocate.ElapsedMs):F4} ms`");
            sb.AppendLine($"- saved by shared payload bulk create vs naive per-entity create: `{(entityOnlyNaive.ElapsedMs - entityOnlyBulkPayload.ElapsedMs):F4} ms`");
            sb.AppendLine($"- presenter creation only, after owners already exist: `{entityPlusPresenter.ElapsedMs:F4} ms`");
            return sb.ToString();
        }

        private readonly record struct EntityOnlyResult(
            int CreatedCount,
            double ElapsedMs,
            double MsPerEntity);

        private readonly record struct EntityPlusPresenterResult(
            int CreatedOwners,
            int CreatedPresenters,
            int PresenterActiveCount,
            double ElapsedMs,
            double MsPerOwner);
    }
}
