using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private static readonly Vector4 FloorColor = new(0.16f, 0.20f, 0.25f, 1f);
    private static readonly Vector4 StaticColor = new(0.40f, 0.45f, 0.52f, 1f);
    private static readonly Vector4 DynamicBlue = new(0.20f, 0.60f, 0.95f, 1f);
    private static readonly Vector4 DynamicGold = new(0.96f, 0.68f, 0.18f, 1f);
    private static readonly Vector4 DynamicGreen = new(0.25f, 0.78f, 0.48f, 1f);
    private static readonly Vector4 DynamicRed = new(0.94f, 0.28f, 0.25f, 1f);
    private static readonly Vector4 KinematicColor = new(0.70f, 0.38f, 0.96f, 1f);
    private static readonly Vector4 QueryTargetColor = new(0.28f, 0.46f, 0.62f, 1f);

    private void BuildSelectedScene()
    {
        ClearOwnedScene();
        ResetSceneDiagnostics();
        switch (_scene)
        {
            case Physics3DShowcaseScene.Bodies:
                BuildBodiesScene();
                break;
            case Physics3DShowcaseScene.Shapes:
                BuildShapesScene();
                break;
            case Physics3DShowcaseScene.Stacking:
                BuildStackingScene();
                break;
            case Physics3DShowcaseScene.Continuous:
                BuildContinuousScene();
                break;
            case Physics3DShowcaseScene.Queries:
                BuildQueriesScene();
                break;
            case Physics3DShowcaseScene.ContactEvents:
                BuildContactEventsScene();
                break;
            case Physics3DShowcaseScene.Joints:
                BuildJointsScene();
                break;
            case Physics3DShowcaseScene.Determinism:
                BuildDeterminismScene();
                break;
            case Physics3DShowcaseScene.Benchmark:
                BuildBenchmarkScene(_benchmarkBodies);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Physics3D showcase scene '{_scene}'.");
        }

        _sceneRevision++;
        _lastAction = $"Loaded {SceneTitle(_scene)}. {SceneDescription(_scene)}";
    }

    private void ResetSceneDiagnostics()
    {
        _sceneStep = 0;
        _contactBeginCount = 0;
        _contactStayCount = 0;
        _contactEndCount = 0;
        _replayCursor = 0;
        _replayExpectedHash = 0;
        _replayActualHash = 0;
        _replayStatus = Physics3DShowcaseReplayStatus.NotRunning;
        Array.Clear(_queryHitCounts, 0, _queryHitCounts.Length);
        Array.Clear(_queryHasFirstHit, 0, _queryHasFirstHit.Length);
        Array.Clear(_queryFirstHitPositionsCm, 0, _queryFirstHitPositionsCm.Length);
        Array.Clear(_queryOriginsCm, 0, _queryOriginsCm.Length);
        Array.Clear(_queryDirections, 0, _queryDirections.Length);
        Array.Clear(_querySizesCm, 0, _querySizesCm.Length);
        Array.Clear(_queryDistancesCm, 0, _queryDistancesCm.Length);
    }

    private void BuildBodiesScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _boxShape,
            Physics3DShapeKind.Box,
            new Vector3(config.BodySizeCm),
            0f,
            new Vector3(-700f, 900f, 0f),
            Quaternion.CreateFromYawPitchRoll(0.35f, 0.2f, 0.1f),
            Vector3.Zero,
            new Vector3(0.4f, 0.8f, 0.2f),
            Physics3DContinuousDetectionMode.Passive,
            DynamicBlue);
        _kinematicBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            _plankShape,
            Physics3DShapeKind.Box,
            PlankVisualSize(config),
            0f,
            new Vector3(0f, 380f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _boxShape,
            Physics3DShapeKind.Box,
            new Vector3(config.BodySizeCm),
            0f,
            new Vector3(750f, config.BodySizeCm * 0.5f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
    }

    private void BuildShapesScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float capsuleCylinder = size * 1.25f;
        for (int i = 0; i < 5; i++)
        {
            float height = 180f + (i * (size + 18f));
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                new Vector3(-700f, height, 0f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 0.12f),
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicBlue);
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _sphereShape,
                Physics3DShapeKind.Sphere,
                new Vector3(size),
                0f,
                new Vector3(0f, height, 0f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGold);
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _capsuleShape,
                Physics3DShapeKind.Capsule,
                CapsuleVisualSize(config),
                capsuleCylinder,
                new Vector3(700f, height + (capsuleCylinder * 0.5f), 0f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, i * 0.08f),
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
        }
    }

    private void BuildStackingScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float boxSpacing = size + config.PyramidGapCm;
        for (int row = 0; row < config.PyramidRows; row++)
        {
            int count = config.PyramidRows - row;
            float startX = config.PyramidCenterXCm - ((count - 1) * boxSpacing * 0.5f);
            float startZ = config.PyramidCenterZCm - ((count - 1) * boxSpacing * 0.5f);
            float y = (size * 0.5f) + (row * boxSpacing);
            for (int z = 0; z < count; z++)
            {
                for (int x = 0; x < count; x++)
                {
                    AddOwnedBody(
                        Physics3DBodyKind.Dynamic,
                        _boxShape,
                        Physics3DShapeKind.Box,
                        new Vector3(size),
                        0f,
                        new Vector3(startX + (x * boxSpacing), y, startZ + (z * boxSpacing)),
                        Quaternion.Identity,
                        Vector3.Zero,
                        Vector3.Zero,
                        Physics3DContinuousDetectionMode.Passive,
                        DynamicBlue);
                }
            }
        }

        float sphereSpacing = config.SpherePyramidSpacingCm;
        float sphereLayerSpacing = MathF.Sqrt((size * size) - ((sphereSpacing * sphereSpacing) * 0.5f));
        for (int layer = 0; layer < config.SpherePyramidRows; layer++)
        {
            int width = config.SpherePyramidRows - layer;
            float startX = config.SpherePyramidCenterXCm - ((width - 1) * sphereSpacing * 0.5f);
            float startZ = config.SpherePyramidCenterZCm - ((width - 1) * sphereSpacing * 0.5f);
            float y = (size * 0.5f) + (layer * sphereLayerSpacing);
            for (int z = 0; z < width; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    AddOwnedBody(
                        Physics3DBodyKind.Dynamic,
                        _sphereShape,
                        Physics3DShapeKind.Sphere,
                        new Vector3(size),
                        0f,
                        new Vector3(startX + (x * sphereSpacing), y, startZ + (z * sphereSpacing)),
                        Quaternion.Identity,
                        Vector3.Zero,
                        Vector3.Zero,
                        Physics3DContinuousDetectionMode.Passive,
                        DynamicGold);
                }
            }
        }
        AddSpherePyramidRails();

        float capsuleCylinder = size * 1.25f;
        float capsuleSpacing = config.CapsulePyramidSpacingCm;
        float capsuleLayerSpacing = MathF.Sqrt((size * size) - ((capsuleSpacing * capsuleSpacing) * 0.25f));
        Quaternion capsuleOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f);
        for (int row = 0; row < config.CapsulePyramidRows; row++)
        {
            int count = config.CapsulePyramidBaseColumns - row;
            float startX = config.CapsulePyramidCenterXCm - ((count - 1) * capsuleSpacing * 0.5f);
            float y = (size * 0.5f) + (row * capsuleLayerSpacing);
            for (int column = 0; column < count; column++)
            {
                AddOwnedBody(
                    Physics3DBodyKind.Dynamic,
                    _capsuleShape,
                    Physics3DShapeKind.Capsule,
                    CapsuleVisualSize(config),
                    capsuleCylinder,
                    new Vector3(startX + (column * capsuleSpacing), y, config.CapsulePyramidCenterZCm),
                    capsuleOrientation,
                    Vector3.Zero,
                    Vector3.Zero,
                    Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
            }
        }
        AddCapsulePyramidRails(capsuleCylinder);
    }

    private void AddSpherePyramidRails()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        float radius = config.BodySizeCm * 0.5f;
        float halfCenterSpan = (config.SpherePyramidRows - 1) * config.SpherePyramidSpacingCm * 0.5f;
        float edgeOffset = halfCenterSpan + radius + config.StackingRailClearanceCm +
                           (config.StackingRailThicknessCm * 0.5f);
        float railY = config.StackingRailHeightCm * 0.5f;
        Vector3 railXSize = new(
            ((config.SpherePyramidRows - 1) * config.SpherePyramidSpacingCm) + config.BodySizeCm +
            (2f * (config.StackingRailThicknessCm + config.StackingRailClearanceCm)),
            config.StackingRailHeightCm,
            config.StackingRailThicknessCm);
        Vector3 railZSize = new(railXSize.Z, railXSize.Y, railXSize.X);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _sphereRailXShape,
            Physics3DShapeKind.Box,
            railXSize,
            0f,
            new Vector3(config.SpherePyramidCenterXCm, railY, config.SpherePyramidCenterZCm - edgeOffset),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _sphereRailXShape,
            Physics3DShapeKind.Box,
            railXSize,
            0f,
            new Vector3(config.SpherePyramidCenterXCm, railY, config.SpherePyramidCenterZCm + edgeOffset),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _sphereRailZShape,
            Physics3DShapeKind.Box,
            railZSize,
            0f,
            new Vector3(config.SpherePyramidCenterXCm - edgeOffset, railY, config.SpherePyramidCenterZCm),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _sphereRailZShape,
            Physics3DShapeKind.Box,
            railZSize,
            0f,
            new Vector3(config.SpherePyramidCenterXCm + edgeOffset, railY, config.SpherePyramidCenterZCm),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
    }

    private void AddCapsulePyramidRails(float capsuleCylinderLengthCm)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        float radius = config.BodySizeCm * 0.5f;
        float halfCenterSpan = (config.CapsulePyramidBaseColumns - 1) * config.CapsulePyramidSpacingCm * 0.5f;
        float edgeOffset = halfCenterSpan + radius + config.StackingRailClearanceCm +
                           (config.StackingRailThicknessCm * 0.5f);
        Vector3 railSize = new(
            config.StackingRailThicknessCm,
            config.StackingRailHeightCm,
            config.BodySizeCm + capsuleCylinderLengthCm + (2f * config.StackingRailClearanceCm));
        float railY = config.StackingRailHeightCm * 0.5f;
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _capsuleRailShape,
            Physics3DShapeKind.Box,
            railSize,
            0f,
            new Vector3(config.CapsulePyramidCenterXCm - edgeOffset, railY, config.CapsulePyramidCenterZCm),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _capsuleRailShape,
            Physics3DShapeKind.Box,
            railSize,
            0f,
            new Vector3(config.CapsulePyramidCenterXCm + edgeOffset, railY, config.CapsulePyramidCenterZCm),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
    }

    private void BuildContinuousScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _thinWallShape,
            Physics3DShapeKind.Box,
            new Vector3(config.BodySizeCm * 0.2f, config.BodySizeCm * 10f, config.BodySizeCm * 25f),
            0f,
            new Vector3(0f, config.BodySizeCm * 5f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        _continuousFirstBodyIndex = _bodyCount;
        Physics3DContinuousDetectionMode[] modes =
        {
            Physics3DContinuousDetectionMode.Discrete,
            Physics3DContinuousDetectionMode.Passive,
            Physics3DContinuousDetectionMode.Continuous
        };
        Vector4[] colors = { DynamicRed, DynamicGold, DynamicGreen };
        for (int i = 0; i < modes.Length; i++)
        {
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _projectileShape,
                Physics3DShapeKind.Sphere,
                new Vector3(config.BodySizeCm * 0.4f),
                0f,
                ContinuousStartPosition(i),
                Quaternion.Identity,
                new Vector3(config.CcdSpeedCmPerSecond, 0f, 0f),
                Vector3.Zero,
                modes[i],
                colors[i],
                mass: 0.25f);
        }
    }

    private void BuildQueriesScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float firstZ = -2400f;
        for (int lane = 0; lane < QueryKindCount; lane++)
        {
            float z = firstZ + (lane * 800f);
            _queryOriginsCm[lane] = lane < 4
                ? new Vector3(-2200f, 300f, z)
                : new Vector3(400f, 300f, z);
            _queryDirections[lane] = Vector3.UnitX;
            _queryDistancesCm[lane] = lane < 4 ? config.QueryDistanceCm : 0f;
            for (int target = 0; target < 3; target++)
            {
                AddOwnedBody(
                    Physics3DBodyKind.Static,
                    _boxShape,
                    Physics3DShapeKind.Box,
                    new Vector3(size),
                    0f,
                    new Vector3(-700f + (target * 1100f), 300f, z),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, target * 0.18f),
                    Vector3.Zero,
                    Vector3.Zero,
                    Physics3DContinuousDetectionMode.Discrete,
                    QueryTargetColor);
            }
        }

        _querySizesCm[0] = Vector3.Zero;
        _querySizesCm[1] = new Vector3(size * 0.65f);
        _querySizesCm[2] = new Vector3(size * 0.7f);
        _querySizesCm[3] = new Vector3(size * 0.6f, size * 1.6f, size * 0.6f);
        _querySizesCm[4] = new Vector3(size * 2.4f, size * 1.2f, size * 1.2f);
        _querySizesCm[5] = new Vector3(size * 1.25f);
        _querySizesCm[6] = new Vector3(size, size * 2.5f, size);
        ExecuteQueries();
    }

    private void BuildContactEventsScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        _contactBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _sphereShape,
            Physics3DShapeKind.Sphere,
            new Vector3(config.BodySizeCm),
            0f,
            new Vector3(0f, config.BodySizeCm * 0.5f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            DynamicGold);
    }

    private void BuildJointsScene()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float spacing = size * 1.25f;
        Physics3DSpringSettings spring = CreateSpring();

        int previous = -1;
        for (int i = 0; i < config.ChainLinkCount; i++)
        {
            int index = _bodyCount;
            AddOwnedBody(
                i == 0 ? Physics3DBodyKind.Kinematic : Physics3DBodyKind.Dynamic,
                _sphereShape,
                Physics3DShapeKind.Sphere,
                new Vector3(size),
                0f,
                new Vector3(-2800f + (i * spacing), 1900f, -900f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                i == 0 ? KinematicColor : DynamicBlue);
            if (previous >= 0)
            {
                AddOwnedConstraint(RequirePhysicsWorld().CreateBallSocketConstraint(
                    _bodyIds[previous],
                    _bodyIds[index],
                    new Vector3(spacing * 0.5f, 0f, 0f),
                    new Vector3(-spacing * 0.5f, 0f, 0f),
                    spring));
            }

            previous = index;
        }

        previous = -1;
        for (int i = 0; i < config.ChainLinkCount; i++)
        {
            int index = _bodyCount;
            AddOwnedBody(
                i == 0 ? Physics3DBodyKind.Kinematic : Physics3DBodyKind.Dynamic,
                _plankShape,
                Physics3DShapeKind.Box,
                PlankVisualSize(config),
                0f,
                new Vector3(-2800f + (i * spacing), 1250f, 200f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                i == 0 ? KinematicColor : DynamicGold);
            if (previous >= 0)
            {
                AddOwnedConstraint(RequirePhysicsWorld().CreateHingeConstraint(
                    _bodyIds[previous],
                    _bodyIds[index],
                    new Vector3(spacing * 0.5f, 0f, 0f),
                    Vector3.UnitZ,
                    new Vector3(-spacing * 0.5f, 0f, 0f),
                    Vector3.UnitZ,
                    spring));
            }

            previous = index;
        }

        int weldPairCount = Math.Max(2, config.ChainLinkCount / 2);
        for (int pair = 0; pair < weldPairCount; pair++)
        {
            float x = -2200f + (pair * spacing * 2.2f);
            int bodyAIndex = _bodyCount;
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                new Vector3(x, 650f, 1100f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
            int bodyBIndex = _bodyCount;
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                new Vector3(x + spacing, 650f, 1100f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                DynamicGreen);
            AddOwnedConstraint(RequirePhysicsWorld().CreateWeldConstraint(
                _bodyIds[bodyAIndex],
                _bodyIds[bodyBIndex],
                new Vector3(spacing, 0f, 0f),
                Quaternion.Identity,
                spring));
        }

        for (int i = 0; i < _constraintCount; i++)
        {
            if (!RequirePhysicsWorld().ContainsConstraint(_constraintIds[i]))
            {
                throw new InvalidOperationException($"Joints scene created invalid constraint '{_constraintIds[i]}'.");
            }
        }
    }

    private void BuildDeterminismScene()
    {
        BuildDeterminismLayout();
        _replayStatus = Physics3DShowcaseReplayStatus.Recording;
        _replayCursor = 0;
    }

    private void BuildDeterminismLayout()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        float spacing = config.ReplayBodySpacingCm;
        int gridSize = config.ReplayGridSize;
        float halfGrid = (gridSize - 1) * 0.5f;
        _determinismFirstBodyIndex = _bodyCount;
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                float velocityX = ((x & 1) == 0 ? 1f : -1f) * (80f + (y * 7f));
                float velocityZ = ((y & 1) == 0 ? 1f : -1f) * (55f + (x * 5f));
                AddOwnedBody(
                    Physics3DBodyKind.Dynamic,
                    _boxShape,
                    Physics3DShapeKind.Box,
                    new Vector3(size),
                    0f,
                    new Vector3(
                        config.ReplayCenterXCm + ((x - halfGrid) * spacing),
                        config.ReplayBaseHeightCm + (y * spacing),
                        (y - halfGrid) * spacing),
                    Quaternion.CreateFromYawPitchRoll(x * 0.03f, y * 0.025f, (x + y) * 0.01f),
                    new Vector3(velocityX, 0f, velocityZ),
                    new Vector3(0.15f + (x * 0.01f), 0.10f + (y * 0.01f), 0.08f),
                    Physics3DContinuousDetectionMode.Passive,
                    ((x + y) & 1) == 0 ? DynamicBlue : DynamicGold);
            }
        }

        _determinismBodyCount = checked(gridSize * gridSize);
        for (int i = 0; i < _determinismBodyCount; i++)
        {
            _replayInitialStates[i] = RequirePhysicsWorld().GetBodyState(_bodyIds[_determinismFirstBodyIndex + i]);
        }
    }

    private void BuildBenchmarkScene(int bodyCount)
    {
        ValidateBenchmarkBodyCount(bodyCount);
        Physics3DShowcaseConfig config = ActiveConfig;
        AddFloor();
        float size = config.BodySizeCm;
        int perLayer = checked(config.BenchmarkColumns * config.BenchmarkDepth);
        for (int i = 0; i < bodyCount; i++)
        {
            int layer = i / perLayer;
            Vector4 color = (layer % 3) switch
            {
                0 => DynamicBlue,
                1 => DynamicGold,
                _ => DynamicGreen
            };
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(size),
                0f,
                BenchmarkPosition(i),
                Quaternion.Identity,
                BenchmarkVelocity(i),
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                color);
        }

        _benchmarkBodies = bodyCount;
    }

    private void AddFloor()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        AddOwnedBody(
            Physics3DBodyKind.Static,
            _floorShape,
            Physics3DShapeKind.Box,
            new Vector3(config.FloorSizeCm, config.FloorThicknessCm, config.FloorSizeCm),
            0f,
            new Vector3(0f, -config.FloorThicknessCm * 0.5f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            FloorColor);
    }

    private void PrepareSceneForPhysicsStep()
    {
        switch (_scene)
        {
            case Physics3DShowcaseScene.Bodies:
                AnimateKinematicPlatform();
                break;
            case Physics3DShowcaseScene.Continuous:
                ResetContinuousProjectilesIfNeeded();
                break;
            case Physics3DShowcaseScene.ContactEvents:
                PrepareContactEventActor();
                break;
            case Physics3DShowcaseScene.Benchmark:
                KeepBenchmarkBodiesInMotion();
                break;
        }
    }

    private void ObserveSceneAfterPhysicsStep()
    {
        switch (_scene)
        {
            case Physics3DShowcaseScene.Queries:
                ExecuteQueries();
                break;
            case Physics3DShowcaseScene.ContactEvents:
                CollectContactEvents();
                break;
            case Physics3DShowcaseScene.Determinism:
                ObserveDeterminismStep();
                break;
        }
    }

    private void AnimateKinematicPlatform()
    {
        if (_kinematicBodyIndex < 0)
        {
            return;
        }

        float phase = (_sceneStep + 1) * 0.075f;
        Physics3DBodyId body = _bodyIds[_kinematicBodyIndex];
        Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(body);
        state.PositionCm = new Vector3(MathF.Sin(phase) * 900f, 380f + (MathF.Cos(phase * 0.5f) * 80f), 0f);
        state.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Sin(phase * 0.7f) * 0.28f);
        state.LinearVelocityCmPerSecond = Vector3.Zero;
        state.AngularVelocityRadiansPerSecond = Vector3.Zero;
        state.Awake = true;
        SetBodyStateAndPose(_kinematicBodyIndex, in state);
    }

    private void ResetContinuousProjectilesIfNeeded()
    {
        if (_continuousFirstBodyIndex < 0)
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            int index = _continuousFirstBodyIndex + i;
            Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[index]);
            if (state.PositionCm.X < 2400f)
            {
                continue;
            }

            state.PositionCm = ContinuousStartPosition(i);
            state.Orientation = Quaternion.Identity;
            state.LinearVelocityCmPerSecond = new Vector3(ActiveConfig.CcdSpeedCmPerSecond, 0f, 0f);
            state.AngularVelocityRadiansPerSecond = Vector3.Zero;
            state.Awake = true;
            SetBodyStateAndPose(index, in state);
        }
    }

    private void PrepareContactEventActor()
    {
        if (_contactBodyIndex < 0)
        {
            return;
        }

        if (_sceneStep == 45)
        {
            Physics3DBodyState separated = RequirePhysicsWorld().GetBodyState(_bodyIds[_contactBodyIndex]);
            separated.PositionCm = new Vector3(0f, 500f, 0f);
            separated.LinearVelocityCmPerSecond = Vector3.Zero;
            separated.AngularVelocityRadiansPerSecond = Vector3.Zero;
            separated.Awake = true;
            SetBodyStateAndPose(_contactBodyIndex, in separated);
        }
        else if (_sceneStep == 75)
        {
            Physics3DBodyState returned = RequirePhysicsWorld().GetBodyState(_bodyIds[_contactBodyIndex]);
            returned.PositionCm = new Vector3(0f, ActiveConfig.BodySizeCm * 0.5f, 0f);
            returned.LinearVelocityCmPerSecond = Vector3.Zero;
            returned.AngularVelocityRadiansPerSecond = Vector3.Zero;
            returned.Awake = true;
            SetBodyStateAndPose(_contactBodyIndex, in returned);
        }
    }

    private void KeepBenchmarkBodiesInMotion()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        for (int i = 1; i < _bodyCount; i++)
        {
            int dynamicIndex = i - 1;
            Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[i]);
            Vector3 authoredPosition = BenchmarkPosition(dynamicIndex);
            Vector3 authoredVelocity = BenchmarkVelocity(dynamicIndex);
            bool recycled = false;
            if (state.PositionCm.Y < config.BenchmarkRecycleHeightCm)
            {
                state.PositionCm.Y = authoredPosition.Y;
                state.PositionCm.Z = authoredPosition.Z;
                recycled = true;
            }

            if (authoredVelocity.X > 0f && state.PositionCm.X > config.BenchmarkTravelHalfWidthCm)
            {
                state.PositionCm.X = -config.BenchmarkTravelHalfWidthCm;
                recycled = true;
            }
            else if (authoredVelocity.X < 0f && state.PositionCm.X < -config.BenchmarkTravelHalfWidthCm)
            {
                state.PositionCm.X = config.BenchmarkTravelHalfWidthCm;
                recycled = true;
            }

            state.LinearVelocityCmPerSecond = new Vector3(
                authoredVelocity.X,
                recycled ? 0f : state.LinearVelocityCmPerSecond.Y,
                0f);
            state.AngularVelocityRadiansPerSecond = Vector3.Zero;
            state.Awake = true;
            if (recycled)
            {
                state.Orientation = Quaternion.Identity;
                SetBodyStateAndPose(i, in state);
            }
            else
            {
                RequirePhysicsWorld().SetBodyVelocity(
                    _bodyIds[i],
                    state.LinearVelocityCmPerSecond,
                    state.AngularVelocityRadiansPerSecond);
            }
        }
    }

    private Vector3 BenchmarkVelocity(int dynamicIndex)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        int perLayer = checked(config.BenchmarkColumns * config.BenchmarkDepth);
        int layer = dynamicIndex / perLayer;
        float direction = (layer & 1) == 0 ? 1f : -1f;
        return new Vector3(direction * config.BenchmarkSpeedCmPerSecond, 0f, 0f);
    }

    private Vector3 BenchmarkPosition(int dynamicIndex)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        int perLayer = checked(config.BenchmarkColumns * config.BenchmarkDepth);
        int layer = dynamicIndex / perLayer;
        int withinLayer = dynamicIndex - (layer * perLayer);
        int x = withinLayer % config.BenchmarkColumns;
        int z = withinLayer / config.BenchmarkColumns;
        float spacing = config.BenchmarkSpacingCm;
        float px = (x - ((config.BenchmarkColumns - 1) * 0.5f)) * spacing;
        float pz = (z - ((config.BenchmarkDepth - 1) * 0.5f)) * spacing;
        float py = config.BenchmarkBaseHeightCm + (layer * spacing);
        return new Vector3(px, py, pz);
    }

    private void ExecuteQueries()
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        Array.Clear(_queryHitCounts, 0, _queryHitCounts.Length);
        Array.Clear(_queryHasFirstHit, 0, _queryHasFirstHit.Length);

        _queryHitCounts[0] = world.Raycast(
            _queryOriginsCm[0],
            _queryDirections[0],
            _queryDistancesCm[0],
            LayerMask.All,
            _rayHits);
        if (_queryHitCounts[0] > 0)
        {
            _queryHasFirstHit[0] = 1;
            _queryFirstHitPositionsCm[0] = _rayHits[0].PositionCm;
        }

        _queryHitCounts[1] = world.BoxCast(
            _queryOriginsCm[1],
            _querySizesCm[1],
            Quaternion.Identity,
            _queryDirections[1],
            _queryDistancesCm[1],
            LayerMask.All,
            _shapeCastHits);
        CaptureFirstShapeCastHit(1);

        _queryHitCounts[2] = world.SphereCast(
            _queryOriginsCm[2],
            _querySizesCm[2].X * 0.5f,
            _queryDirections[2],
            _queryDistancesCm[2],
            LayerMask.All,
            _shapeCastHits);
        CaptureFirstShapeCastHit(2);

        float capsuleDiameter = _querySizesCm[3].X;
        _queryHitCounts[3] = world.CapsuleCast(
            _queryOriginsCm[3],
            capsuleDiameter * 0.5f,
            _querySizesCm[3].Y - capsuleDiameter,
            Quaternion.Identity,
            _queryDirections[3],
            _queryDistancesCm[3],
            LayerMask.All,
            _shapeCastHits);
        CaptureFirstShapeCastHit(3);

        _queryHitCounts[4] = world.OverlapBox(
            _queryOriginsCm[4],
            _querySizesCm[4],
            Quaternion.Identity,
            LayerMask.All,
            _overlapHits);
        CaptureFirstOverlapHit(4);

        _queryHitCounts[5] = world.OverlapSphere(
            _queryOriginsCm[5],
            _querySizesCm[5].X * 0.5f,
            LayerMask.All,
            _overlapHits);
        CaptureFirstOverlapHit(5);

        float overlapCapsuleDiameter = _querySizesCm[6].X;
        _queryHitCounts[6] = world.OverlapCapsule(
            _queryOriginsCm[6],
            overlapCapsuleDiameter * 0.5f,
            _querySizesCm[6].Y - overlapCapsuleDiameter,
            Quaternion.Identity,
            LayerMask.All,
            _overlapHits);
        CaptureFirstOverlapHit(6);
    }

    private void CaptureFirstShapeCastHit(int queryIndex)
    {
        if (_queryHitCounts[queryIndex] <= 0)
        {
            return;
        }

        _queryHasFirstHit[queryIndex] = 1;
        _queryFirstHitPositionsCm[queryIndex] = _shapeCastHits[0].PositionCm;
    }

    private void CaptureFirstOverlapHit(int queryIndex)
    {
        if (_queryHitCounts[queryIndex] <= 0)
        {
            return;
        }

        _queryHasFirstHit[queryIndex] = 1;
        _queryFirstHitPositionsCm[queryIndex] = RequirePhysicsWorld().GetBodyState(_overlapHits[0].Body).PositionCm;
    }

    private void CollectContactEvents()
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        if (world.ContactEventCount > _contactEvents.Length)
        {
            throw new InvalidOperationException(
                $"Contact event count {world.ContactEventCount} exceeded showcase capacity {_contactEvents.Length}.");
        }

        int count = world.CopyContactEvents(_contactEvents);
        for (int i = 0; i < count; i++)
        {
            switch (_contactEvents[i].Kind)
            {
                case Physics3DContactEventKind.Begin:
                    _contactBeginCount++;
                    break;
                case Physics3DContactEventKind.Stay:
                    _contactStayCount++;
                    break;
                case Physics3DContactEventKind.End:
                    _contactEndCount++;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Physics3D contact event '{_contactEvents[i].Kind}'.");
            }
        }
    }

    private void ObserveDeterminismStep()
    {
        ulong hash = ComputeOwnedStateHash(_determinismFirstBodyIndex, _determinismBodyCount);
        if (_replayStatus == Physics3DShowcaseReplayStatus.Recording)
        {
            int recordedStep = _replayCursor;
            _replayHashes[recordedStep] = hash;
            CaptureRecordedReplayStates(recordedStep);
            _replayCursor++;
            if (_replayCursor == _replayHashes.Length)
            {
                RebuildDeterminismForReplay();
            }

            return;
        }

        if (_replayStatus != Physics3DShowcaseReplayStatus.Replaying)
        {
            return;
        }

        ulong expected = _replayHashes[_replayCursor];
        if (hash != expected)
        {
            _replayExpectedHash = expected;
            _replayActualHash = hash;
            _replayStatus = Physics3DShowcaseReplayStatus.Failed;
            RequireSimulation().Enabled = false;
            _lastAction = $"Determinism failed at replay step {_replayCursor + 1}.";
            return;
        }

        _replayCursor++;
        if (_replayCursor == _replayHashes.Length)
        {
            _replayStatus = Physics3DShowcaseReplayStatus.Passed;
            RequireSimulation().Enabled = false;
            _lastAction = $"Determinism passed: {_replayHashes.Length} recorded steps matched after rebuild.";
        }
    }

    private void CaptureRecordedReplayStates(int recordedStep)
    {
        int destinationOffset = checked(recordedStep * _determinismBodyCount);
        for (int i = 0; i < _determinismBodyCount; i++)
        {
            _replayRecordedStates[destinationOffset + i] = RequirePhysicsWorld().GetBodyState(
                _bodyIds[_determinismFirstBodyIndex + i]);
        }
    }

    private void RebuildDeterminismForReplay()
    {
        ClearOwnedScene();
        _sceneStep = 0;
        BuildDeterminismLayout();
        _replayCursor = 0;
        _replayStatus = Physics3DShowcaseReplayStatus.ReadyToReplay;
        RequireSimulation().Enabled = false;
        _lastAction = "Recording complete. The rebuilt scene is paused and ready for side-by-side comparison.";
    }

    private void StartReplayComparison()
    {
        if (_scene != Physics3DShowcaseScene.Determinism ||
            _replayStatus != Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            throw new InvalidOperationException("Replay comparison can only start from the rebuilt ready frame.");
        }

        _replayCursor = 0;
        _replayStatus = Physics3DShowcaseReplayStatus.Replaying;
        RequireSimulation().Enabled = true;
        _lastAction = "Comparing the gold live replay against the blue recorded run, step by step.";
    }

    private ulong ComputeOwnedStateHash(int firstBodyIndex, int count)
    {
        if (firstBodyIndex < 0 || count <= 0 || firstBodyIndex + count > _bodyCount)
        {
            throw new InvalidOperationException("Physics3D determinism hash range is invalid.");
        }

        ulong hash = 1469598103934665603UL;
        for (int i = 0; i < count; i++)
        {
            int index = firstBodyIndex + i;
            Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[index]);
            hash = Mix(hash, (uint)_bodyKinds[index]);
            hash = Mix(hash, (uint)_bodyShapeKinds[index]);
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.PositionCm.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.Orientation.W)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.LinearVelocityCmPerSecond.Z)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.X)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.Y)));
            hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(state.AngularVelocityRadiansPerSecond.Z)));
            hash = Mix(hash, state.Awake ? 1u : 0u);
        }

        return hash;
    }

    private void ApplyImpact()
    {
        if (_scene == Physics3DShowcaseScene.Continuous)
        {
            for (int i = 0; i < 3; i++)
            {
                int index = _continuousFirstBodyIndex + i;
                Physics3DBodyState projectile = RequirePhysicsWorld().GetBodyState(_bodyIds[index]);
                projectile.PositionCm = ContinuousStartPosition(i);
                projectile.LinearVelocityCmPerSecond = new Vector3(ActiveConfig.CcdSpeedCmPerSecond, 0f, 0f);
                projectile.AngularVelocityRadiansPerSecond = Vector3.Zero;
                projectile.Awake = true;
                SetBodyStateAndPose(index, in projectile);
            }

            _lastAction = "Fired all three continuous-collision comparison projectiles.";
            return;
        }

        int affected = 0;
        for (int i = 0; i < _bodyCount; i++)
        {
            if (_bodyKinds[i] != Physics3DBodyKind.Dynamic)
            {
                continue;
            }

            Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[i]);
            Vector3 direction = new(state.PositionCm.X, MathF.Max(150f, state.PositionCm.Y * 0.25f), state.PositionCm.Z);
            if (direction.LengthSquared() < 1f)
            {
                direction = new Vector3(1f, 0.4f, 0f);
            }

            direction = Vector3.Normalize(direction);
            RequirePhysicsWorld().SetBodyVelocity(
                _bodyIds[i],
                direction * ActiveConfig.ImpactSpeedCmPerSecond,
                new Vector3(1.4f, 0.8f, 1.1f));
            affected++;
        }

        if (_scene == Physics3DShowcaseScene.Determinism && affected > 0)
        {
            for (int i = 0; i < _determinismBodyCount; i++)
            {
                _replayInitialStates[i] = RequirePhysicsWorld().GetBodyState(_bodyIds[_determinismFirstBodyIndex + i]);
            }

            _replayCursor = 0;
            _replayStatus = Physics3DShowcaseReplayStatus.Recording;
            RequireSimulation().Enabled = true;
        }

        _lastAction = affected > 0
            ? $"Impact launched {affected} dynamic bodies without changing scene ownership."
            : "This scene has no dynamic body to impact.";
    }

    private void SetBodyStateAndPose(int index, in Physics3DBodyState state)
    {
        RequirePhysicsWorld().SetBodyState(_bodyIds[index], state);
        Entity entity = _bodyEntities[index];
        if (!RequireEcsWorld().IsAlive(entity))
        {
            throw new InvalidOperationException($"Physics3D showcase lost ECS entity for body index {index}.");
        }

        ref Physics3DPoseCm pose = ref RequireEcsWorld().Get<Physics3DPoseCm>(entity);
        pose.Position = state.PositionCm;
        pose.Orientation = state.Orientation;
        pose.LinearVelocity = state.LinearVelocityCmPerSecond;
        pose.AngularVelocity = state.AngularVelocityRadiansPerSecond;
    }

    private Vector3 ContinuousStartPosition(int lane)
    {
        return new Vector3(-2600f, 220f + (lane * 260f), -520f + (lane * 520f));
    }

    private static Vector3 PlankVisualSize(Physics3DShowcaseConfig config)
    {
        float size = config.BodySizeCm;
        return new Vector3(size * 1.5f, size * 0.35f, size * 0.6f);
    }

    private static Vector3 CapsuleVisualSize(Physics3DShowcaseConfig config)
    {
        float diameter = config.BodySizeCm;
        return new Vector3(diameter, diameter + (diameter * 1.25f), diameter);
    }

    private static ulong Mix(ulong hash, uint value)
    {
        hash ^= value;
        return hash * 1099511628211UL;
    }
}
