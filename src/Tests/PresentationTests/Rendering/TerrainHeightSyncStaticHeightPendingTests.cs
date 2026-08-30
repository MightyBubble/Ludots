using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Regression coverage for issue #639: static Once height pending may resolve flat y=0
    /// only when the focused map does not declare a visual heightmap. Declared maps keep
    /// PresentationStaticHeightPending until a finite ContinuousHeightmap sample succeeds.
    /// MapLoadStatus alone is neither necessary nor sufficient for that invariant.
    /// </summary>
    [TestFixture]
    public sealed class TerrainHeightSyncStaticHeightPendingTests
    {
        [Test]
        public void TerrainHeightSync_UndeclaredMap_MissingHeightmap_FinalizesStaticPendingToZero()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("flat_undeclared"),
                new MapConfig { Id = "flat_undeclared" });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.ImmediateSuccess,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.False);
        }

        [Test]
        public void TerrainHeightSync_DeclaredMapHeightmap_MissingService_DoesNotFinalizeStaticPendingToZero()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("declared_missing_heightmap"),
                new MapConfig
                {
                    Id = "declared_missing_heightmap",
                    ContinuousHeightmapAsset = "assets/terrain/declared.height",
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.ImmediateSuccess,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(9f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.True);
        }

        [Test]
        public void TerrainHeightSync_DeclaredBoardHeightmap_PendingLoad_DoesNotFinalizeStaticPendingToZero()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("board_declared_pending"),
                new MapConfig
                {
                    Id = "board_declared_pending",
                    Boards =
                    {
                        new BoardConfig
                        {
                            Name = "default",
                            ContinuousHeightmapAsset = "assets/terrain/board_declared.height",
                        },
                    },
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.DeferredPending,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(9f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.True);
        }

        [Test]
        public void TerrainHeightSync_DeclaredMapHeightmap_NonSampleable_DoesNotFinalizeStaticPendingToZero()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("declared_nonsampleable"),
                new MapConfig
                {
                    Id = "declared_nonsampleable",
                    ContinuousHeightmapAsset = "assets/terrain/declared.height",
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.DeferredSuccess,
                [CoreServiceKeys.ContinuousHeightmap.Name] = new NonSampleableHeightmap(),
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(9f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.True);
        }

        [Test]
        public void TerrainHeightSync_DeclaredMapHeightmap_NonFiniteSample_DoesNotFinalizeStaticPendingToZero()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("declared_nonfinite"),
                new MapConfig
                {
                    Id = "declared_nonfinite",
                    ContinuousHeightmapAsset = "assets/terrain/declared.height",
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.DeferredSuccess,
                [CoreServiceKeys.ContinuousHeightmap.Name] = new NonFiniteHeightmap(),
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(9f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.True);
        }

        [Test]
        public void TerrainHeightSync_DeclaredMapHeightmap_PendingLoad_WithSampleable_ResolvesWhenFiniteSampleSucceeds()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("declared_pending_with_heightmap"),
                new MapConfig
                {
                    Id = "declared_pending_with_heightmap",
                    ContinuousHeightmapAsset = "assets/terrain/declared.height",
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.DeferredPending,
                [CoreServiceKeys.ContinuousHeightmap.Name] = new FixedHeightmap(heightCm: 250f),
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            // MapLoadStatus is neither necessary nor sufficient: a finite sample resolves static pending.
            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.False);
        }

        [Test]
        public void TerrainHeightSync_DeclaredMapHeightmap_Ready_FiniteHeightmap_ResolvesStaticPendingToSampledHeight()
        {
            using var world = World.Create();
            Entity entity = CreateStaticHeightPendingEntity(world, initialY: 9f);

            var session = new MapSession(
                new MapId("declared_ready_finite"),
                new MapConfig
                {
                    Id = "declared_ready_finite",
                    ContinuousHeightmapAsset = "assets/terrain/declared.height",
                });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.MapSession.Name] = session,
                [CoreServiceKeys.MapLoadStatus.Name] = MapLoadStatus.DeferredSuccess,
                [CoreServiceKeys.ContinuousHeightmap.Name] = new FixedHeightmap(heightCm: 250f),
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.False);
        }

        private static Entity CreateStaticHeightPendingEntity(World world, float initialY)
        {
            world.Create(
                new PresentationFrameState { Enabled = true, InterpolationAlpha = 1f, FrameId = 1 },
                new PresentationFrameStateTag());

            return world.Create(
                WorldPositionCm.FromCmFloat(100f, 200f),
                new PreviousWorldPositionCm { Value = WorldPositionCm.FromCmFloat(100f, 200f).Value },
                new VisualTransform
                {
                    Position = new Vector3(1f, initialY, 2f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new PresentationStaticTransform(),
                new PresentationStaticHeightPending());
        }

        private sealed class FixedHeightmap : IContinuousHeightmap
        {
            private readonly float _heightCm;

            public FixedHeightmap(float heightCm)
            {
                _heightCm = heightCm;
            }

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                heightCm = _heightCm;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                for (int i = 0; i < outHeightCm.Length; i++)
                {
                    outHeightCm[i] = _heightCm;
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                hit = new VisualGroundHit(ray.Origin.X * 100f, ray.Origin.Z * 100f, _heightCm, layerIndex, 0f, Vector3.UnitY);
                return true;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = 0)
            {
                for (int i = 0; i < outHeightCm.Length; i++)
                {
                    outWorldXCm[i] = originXMeters[i] * 100f;
                    outWorldYCm[i] = originZMeters[i] * 100f;
                    outHeightCm[i] = _heightCm;
                    outDistanceMeters[i] = 0f;
                    outNormalX[i] = 0f;
                    outNormalY[i] = 1f;
                    outNormalZ[i] = 0f;
                    outLayerIndex[i] = layerIndex;
                    outHitMask[i] = 1;
                }

                return true;
            }
        }

        private sealed class NonSampleableHeightmap : IContinuousHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                heightCm = 0f;
                return false;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                return false;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                hit = default;
                return false;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = 0)
            {
                return false;
            }
        }

        private sealed class NonFiniteHeightmap : IContinuousHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                heightCm = float.NaN;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                for (int i = 0; i < outHeightCm.Length; i++)
                {
                    outHeightCm[i] = float.PositiveInfinity;
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                hit = default;
                return false;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = 0)
            {
                return false;
            }
        }
    }
}
