using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Tests.TestCommon;
using Ludots.Core.Association;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class InteractionSelectionConvergenceTests
    {
        [Test]
        public void CommandSourcePointerHitResolver_UsesWorldPositionCm_NotVisualTransformOrCull()
        {
            using var world = World.Create();
            world.Create(
                new Ludots.Core.Presentation.Components.PresentationFrameState { Enabled = true, InterpolationAlpha = 1f },
                new Ludots.Core.Presentation.Components.PresentationFrameStateTag());
            var local = world.Create();
            var actor = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(80f, 0f, 80f) },
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                new CommandSourceSelectableTag());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
            };

            Entity hit = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                world,
                globals,
                local,
                new Vector2(1600f, 1200f),
                radiusPixels: 16f);

            That(hit, Is.EqualTo(actor));
        }

















        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "Command", Name = "Command", Type = InputActionType.Button },
                    new() { Id = "Stop", Name = "Stop", Type = InputActionType.Button },
                    new() { Id = "Confirm", Name = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Select.Begin", Name = "Command Source Acquire", Type = InputActionType.Button },
                    new() { Id = CommandSourceModifierActionIds.Additive, Name = CommandSourceModifierActionIds.Additive, Type = InputActionType.Button },
                    new() { Id = CommandSourceModifierActionIds.Toggle, Name = CommandSourceModifierActionIds.Toggle, Type = InputActionType.Button },
                    new() { Id = "PointerPos", Name = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = AuthoritativeGroundPointerHelper.ActionId, Name = AuthoritativeGroundPointerHelper.ActionId, Type = InputActionType.Axis3D },
                },
                Contexts = new List<InputContextDef>
                {
                    new() { Id = "Test", Name = "Test", Priority = 1 },
                },
            };
        }



        private static void SetAuthoritativeGroundPoint(PlayerInputHandler input, in WorldCmInt2 worldCm)
        {
            input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(worldCm.X, 0f, worldCm.Y));
        }

        private static void AssertCommandSource(Dictionary<string, object> globals, Entity owner, params Entity[] expected)
        {
            var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
            That(collections.TryGet(owner, "collection.command.source", out EntityCollectionHandle handle), Is.True);
            That(collections.TryGetView(handle, out EntityCollectionView view), Is.True);
            That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.UiAcquisition));
            That(view.Role, Is.EqualTo(EntityCollectionRoleKind.CommandSource));
            That(view.PrimaryEntity, Is.EqualTo(expected.Length > 0 ? expected[0] : Entity.Null));
            That(view.Count, Is.EqualTo(expected.Length));

            Entity[] actual = new Entity[expected.Length];
            int written = collections.CopyEntities(handle, 0, actual);
            That(written, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                That(actual[i], Is.EqualTo(expected[i]));
            }
        }

        private static WorldSizeSpec CreateWorldSizeSpec()
        {
            return new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100);
        }

        private static SpatialFootprint2D CreateRectFootprint(int centerXCm, int centerZCm, int widthCm, int depthCm)
        {
            int halfWidth = widthCm / 2;
            int halfDepth = depthCm / 2;
            var footprint = new SpatialFootprint2D();
            footprint.SetPolygonVertexCount(0, 4);
            footprint.SetVertex(0, 0, new WorldCmInt2(centerXCm - halfWidth, centerZCm - halfDepth));
            footprint.SetVertex(0, 1, new WorldCmInt2(centerXCm + halfWidth, centerZCm - halfDepth));
            footprint.SetVertex(0, 2, new WorldCmInt2(centerXCm + halfWidth, centerZCm + halfDepth));
            footprint.SetVertex(0, 3, new WorldCmInt2(centerXCm - halfWidth, centerZCm + halfDepth));
            return footprint;
        }

        private static IContinuousHeightmap CreateFlatHeightmap()
        {
            return new ContinuousHeightmapRuntime(
                ContinuousHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(-10_000, -10_000, 20_000, 20_000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 0,
                        0, 0,
                    }));
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class ConstantScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(0f, 10f, 0f), new Vector3(0f, -1f, 0f));
            }
        }

        private sealed class AnchoredScreenRayProvider : IScreenRayProvider
        {
            private readonly Vector3 _origin;

            public AnchoredScreenRayProvider(Vector3 origin)
            {
                _origin = origin;
            }

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(_origin, new Vector3(0f, -1f, 0f));
            }
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }

    }
}
