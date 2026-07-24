using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Ragdoll;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class RagdollTests
{
    private static readonly Physics3DMaterial Material = new(0.8f, 200f, 30f, 1f);
    private static readonly Physics3DSpringSettings JointSpring = new(24f, 2f);
    private static readonly Physics3DSpringSettings ActiveSpring = new(18f, 2f);
    private static readonly Physics3DServoSettings ActiveServo = new(20f, 0f, 500_000f);

    [Test]
    public void Recipes_WithChainAndBranchTopologies_CreateWithoutHumanoidAssumptions()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(16, 0, shapeCapacity: 16, constraintCapacity: 64);
        using var ragdolls = CreateRagdollWorld(physics, recipeCapacity: 2, recipeBoneCapacity: 8, instanceCapacity: 2, maximumBones: 4);
        using World ecs = World.Create();
        RagdollRecipeId chain = ragdolls.RegisterRecipe(CreateChainRecipe(101));
        RagdollRecipeId branch = ragdolls.RegisterRecipe(CreateBranchRecipe(202));
        RagdollBoneHandoff[] chainHandoff = CreateAuthoredHandoff(ecs, CreateChainRecipe(101), new Vector3(-200f, 500f, 0f));
        RagdollBoneHandoff[] branchHandoff = CreateAuthoredHandoff(ecs, CreateBranchRecipe(202), new Vector3(200f, 500f, 0f));
        var chainBodies = new Physics3DBodyId[3];
        var branchBodies = new Physics3DBodyId[4];

        RagdollInstanceId chainInstance = ragdolls.TransitionFromAnimation(
            chain, 0, CreateActivation(1, activePose: false), chainHandoff, chainBodies);
        RagdollInstanceId branchInstance = ragdolls.TransitionFromAnimation(
            branch, 0, CreateActivation(2, activePose: true), branchHandoff, branchBodies);

        Assert.Multiple(() =>
        {
            Assert.That(ragdolls.GetInstanceState(chainInstance).BoneCount, Is.EqualTo(3));
            Assert.That(ragdolls.GetInstanceState(branchInstance).BoneCount, Is.EqualTo(4));
            Assert.That(ragdolls.ActiveBoneCount, Is.EqualTo(7));
            Assert.That(ragdolls.ActiveConstraintCount, Is.EqualTo((2 * 3) + (3 * 4)));
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(7));
        });
    }

    [Test]
    public void CollisionSubgroups_BlockAdjacentBonesAndHonorNonAdjacentMasks()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(3, 0, shapeCapacity: 4, constraintCapacity: 16);
        using var ragdolls = CreateRagdollWorld(physics, 1, 3, 1, 3);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateCollisionRecipe();
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, Vector3.Zero);
        handoff[0] = WithPosition(handoff[0], new Vector3(0f, 200f, 0f));
        handoff[1] = WithPosition(handoff[1], new Vector3(120f, 200f, 0f));
        handoff[2] = WithPosition(handoff[2], new Vector3(10f, 200f, 0f));
        var bodies = new Physics3DBodyId[3];
        ragdolls.TransitionFromAnimation(recipe, 0, CreateActivation(77, activePose: false), handoff, bodies);

        Physics3DCollisionSubgroup root = physics.GetBodyCollisionSubgroup(bodies[0]);
        Physics3DCollisionSubgroup child = physics.GetBodyCollisionSubgroup(bodies[1]);
        Physics3DCollisionSubgroup grandchild = physics.GetBodyCollisionSubgroup(bodies[2]);
        Assert.Multiple(() =>
        {
            Assert.That(root.CollidesWithSubgroups & child.SubgroupBit, Is.Zero);
            Assert.That(child.CollidesWithSubgroups & root.SubgroupBit, Is.Zero);
            Assert.That(child.CollidesWithSubgroups & grandchild.SubgroupBit, Is.Zero);
            Assert.That(grandchild.CollidesWithSubgroups & child.SubgroupBit, Is.Zero);
            Assert.That(root.CollidesWithSubgroups & grandchild.SubgroupBit, Is.Not.Zero);
            Assert.That(grandchild.CollidesWithSubgroups & root.SubgroupBit, Is.Not.Zero);
        });

        Step(ragdolls, physics, 0);
        var contacts = new Physics3DContactPair[8];
        int contactCount = physics.CopyContactPairs(contacts);
        Assert.That(ContainsPair(contacts.AsSpan(0, contactCount), bodies[0], bodies[2]), Is.True,
            "The authored non-adjacent pair should remain collidable while adjacent pairs are filtered.");
        Assert.That(ContainsPair(contacts.AsSpan(0, contactCount), bodies[0], bodies[1]), Is.False);
        Assert.That(ContainsPair(contacts.AsSpan(0, contactCount), bodies[1], bodies[2]), Is.False);
    }

    [Test]
    public void SwingLimit_ContainsAnInitiallyOverRotatedBone()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(2, 0, shapeCapacity: 4, constraintCapacity: 8);
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, 1, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(301, maximumSwing: 0.25f, activePoseForce: 500_000f);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, new Vector3(0f, 500f, 0f));
        handoff[1] = WithOrientation(handoff[1], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f));
        var bodies = new Physics3DBodyId[2];
        ragdolls.TransitionFromAnimation(recipe, 0, CreateActivation(3, activePose: false), handoff, bodies);

        for (int tick = 0; tick < 90; tick++)
        {
            Step(ragdolls, physics, tick);
        }

        float swing = AxisAngle(
            physics.GetBodyState(bodies[0]).Orientation,
            physics.GetBodyState(bodies[1]).Orientation);
        Assert.That(swing, Is.LessThan(0.5f), "The joint must converge toward the authored swing range.");
    }

    [Test]
    public void ActivePose_SubmitsTargetsEveryTickAndCanBeToggledAtBoundary()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(2, 0, shapeCapacity: 4, constraintCapacity: 8);
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, 1, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(302, maximumSwing: MathF.PI, activePoseForce: 1_000_000f);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, new Vector3(0f, 500f, 0f));
        handoff[1] = WithOrientation(handoff[1], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f));
        var bodies = new Physics3DBodyId[2];
        RagdollInstanceId instance = ragdolls.TransitionFromAnimation(
            recipe, 0, CreateActivation(4, activePose: false), handoff, bodies);
        Assert.That(ragdolls.ActiveConstraintCount, Is.EqualTo(3));

        ragdolls.SetActivePoseEnabled(instance, 0, true);
        Quaternion[] target = { Quaternion.Identity, Quaternion.Identity };
        for (int tick = 0; tick < 45; tick++)
        {
            ragdolls.SubmitActivePose(instance, tick, target);
            Step(ragdolls, physics, tick);
        }

        float angle = QuaternionAngle(
            physics.GetBodyState(bodies[0]).Orientation,
            physics.GetBodyState(bodies[1]).Orientation);
        ragdolls.SetActivePoseEnabled(instance, 44, false);
        Assert.Multiple(() =>
        {
            Assert.That(angle, Is.LessThan(0.35f));
            Assert.That(ragdolls.GetInstanceState(instance).ActivePoseEnabled, Is.False);
            Assert.That(ragdolls.ActiveConstraintCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void AnimationHandoff_PreservesWorldPoseAndBothVelocitiesAtTickBoundary()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(2, 0, shapeCapacity: 4, constraintCapacity: 8);
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, 1, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(303);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, new Vector3(80f, 700f, -40f));
        handoff[0] = new RagdollBoneHandoff(
            handoff[0].Entity,
            handoff[0].PositionCm,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f),
            new Vector3(120f, 30f, -50f),
            new Vector3(0.2f, 0.7f, -0.1f));
        var bodies = new Physics3DBodyId[2];
        ragdolls.TransitionFromAnimation(recipe, 50, CreateActivation(5, activePose: false), handoff, bodies);

        Physics3DBodyState root = physics.GetBodyState(bodies[0]);
        Assert.Multiple(() =>
        {
            Assert.That(root.PositionCm, Is.EqualTo(handoff[0].PositionCm));
            Assert.That(root.Orientation, Is.EqualTo(handoff[0].Orientation));
            Assert.That(root.LinearVelocityCmPerSecond, Is.EqualTo(handoff[0].LinearVelocityCmPerSecond));
            Assert.That(root.AngularVelocityRadiansPerSecond, Is.EqualTo(handoff[0].AngularVelocityRadiansPerSecond));
        });
    }

    [Test]
    public void Recovery_BlockedClearanceLeavesRagdollAndClearRetryProducesCharacterHandoff()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(2, 1, shapeCapacity: 5, constraintCapacity: 8);
        Physics3DShapeId blockerShape = physics.RegisterBoxShape(new Vector3(100f));
        Physics3DBodyId blocker = physics.CreateBody(CreateStaticBody(blockerShape, new Vector3(0f, 560f, 0f)));
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, 1, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(304);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, new Vector3(0f, 500f, 0f));
        var bodies = new Physics3DBodyId[2];
        RagdollInstanceId instance = ragdolls.TransitionFromAnimation(
            recipe, 0, CreateActivation(6, activePose: false), handoff, bodies);
        Step(ragdolls, physics, 0);
        var geometry = new Character3DGeometry(default, 25f, 80f, LayerMask.All);
        var candidatePoses = new RagdollBonePose[2];
        candidatePoses[0] = new RagdollBonePose(999, new Vector3(9f), Quaternion.Identity);

        bool blocked = ragdolls.TryBuildRecoveryCandidate(instance, 0, geometry, candidatePoses, out RagdollRecoveryCandidate blockedCandidate);
        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.False);
            Assert.That(blockedCandidate.BlockerCount, Is.GreaterThan(0));
            Assert.That(ragdolls.GetInstanceState(instance).RecoveryState, Is.EqualTo(RagdollRecoveryState.Blocked));
            Assert.That(ragdolls.ContainsInstance(instance), Is.True);
            Assert.That(candidatePoses[0].StableId, Is.EqualTo(999), "A failed clearance check must not publish a partial pose.");
        });

        physics.DestroyBody(blocker);
        bool clear = ragdolls.TryBuildRecoveryCandidate(instance, 0, geometry, candidatePoses, out RagdollRecoveryCandidate clearCandidate);
        RagdollRecoveryCandidate committed = ragdolls.CommitRecovery(instance, 0);
        Assert.Multiple(() =>
        {
            Assert.That(clear, Is.True);
            Assert.That(clearCandidate.IsClear, Is.True);
            Assert.That(candidatePoses[0].StableId, Is.EqualTo(definition.Bones[0].StableId));
            Assert.That(committed.CharacterCenterCm, Is.EqualTo(clearCandidate.CharacterCenterCm));
            Assert.That(ragdolls.ContainsInstance(instance), Is.False);
            Assert.That(physics.ActiveMobileBodyCount, Is.Zero);
        });
    }

    [Test]
    public void CapacityFailure_IsAtomicForInstancesAndRecipeBones()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(8, 0, shapeCapacity: 8, constraintCapacity: 24);
        using var ragdolls = CreateRagdollWorld(physics, 2, 3, 1, 3);
        using World ecs = World.Create();
        RagdollRecipeDefinition chain = CreateChainRecipe(401);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(chain);
        int shapesBeforeRejectedRecipe = physics.RegisteredShapeCount;
        Assert.Throws<RagdollCapacityExceededException>(() => ragdolls.RegisterRecipe(CreateTwoBoneRecipe(402)));
        Assert.That(physics.RegisteredShapeCount, Is.EqualTo(shapesBeforeRejectedRecipe));

        RagdollBoneHandoff[] firstHandoff = CreateAuthoredHandoff(ecs, chain, new Vector3(-100f, 500f, 0f));
        RagdollBoneHandoff[] secondHandoff = CreateAuthoredHandoff(ecs, chain, new Vector3(100f, 500f, 0f));
        var firstBodies = new Physics3DBodyId[3];
        var rejectedBodies = new[] { new Physics3DBodyId(99, 99), new Physics3DBodyId(99, 99), new Physics3DBodyId(99, 99) };
        ragdolls.TransitionFromAnimation(recipe, 0, CreateActivation(7, false), firstHandoff, firstBodies);
        int bodyCountBefore = physics.ActiveMobileBodyCount;
        int constraintCountBefore = physics.ActiveConstraintCount;

        Assert.Throws<RagdollCapacityExceededException>(() => ragdolls.TransitionFromAnimation(
            recipe, 0, CreateActivation(8, false), secondHandoff, rejectedBodies));
        Assert.Multiple(() =>
        {
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(bodyCountBefore));
            Assert.That(physics.ActiveConstraintCount, Is.EqualTo(constraintCountBefore));
            Assert.That(ragdolls.ActiveInstanceCount, Is.EqualTo(1));
            Assert.That(rejectedBodies[0], Is.EqualTo(new Physics3DBodyId(99, 99)));
        });
    }

    [Test]
    public void MissingRuntimeBody_FailsWithInstanceBoneAndTick()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(2, 0, shapeCapacity: 4, constraintCapacity: 8);
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, 1, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(501);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(ecs, definition, new Vector3(0f, 500f, 0f));
        var bodies = new Physics3DBodyId[2];
        RagdollInstanceId instance = ragdolls.TransitionFromAnimation(
            recipe, 0, CreateActivation(9, false), handoff, bodies);
        physics.DestroyBody(bodies[1]);

        RagdollStateException? exception = Assert.Throws<RagdollStateException>(() => ragdolls.PrepareFixedStep(0));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Instance, Is.EqualTo(instance));
            Assert.That(exception.BoneIndex, Is.EqualTo(1));
            Assert.That(exception.Tick, Is.EqualTo(0));
        });
    }

    [Test]
    public void HundredMixedAndThreeHundredIndependentRagdolls_RunAtThirtyHertz()
    {
        RunPressureScenario(instanceCount: 100, useMixedTopologies: true, activePoseStride: 2);
        RunPressureScenario(instanceCount: 300, useMixedTopologies: false, activePoseStride: 1);
    }

    [Test]
    public void WarmedHundredRagdolls_HaveZeroManagedAllocationsOnMainAndWorkers()
    {
        const int instanceCount = 100;
        const int bodyCount = instanceCount * 2;
        Physics3DWorldConfig physicsConfig = CreatePhysicsConfig(bodyCount, 0, 4, workerCount: 2, constraintCapacity: instanceCount * 4);
        using var dispatcher = new TrackingThreadDispatcher(2);
        using var physics = new Physics3DWorld(physicsConfig, dispatcher);
        using var ragdolls = CreateRagdollWorld(physics, 1, 2, instanceCount, 2);
        using World ecs = World.Create();
        RagdollRecipeDefinition definition = CreateTwoBoneRecipe(601, maximumSwing: MathF.PI);
        RagdollRecipeId recipe = ragdolls.RegisterRecipe(definition);
        var instances = new RagdollInstanceId[instanceCount];
        var targets = new Quaternion[instanceCount * 2];
        var createdBodies = new Physics3DBodyId[2];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = Quaternion.Identity;
        }

        for (int i = 0; i < instanceCount; i++)
        {
            RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(
                ecs,
                definition,
                new Vector3((i % 20) * 300f, 1_000f, (i / 20) * 300f));
            instances[i] = ragdolls.TransitionFromAnimation(
                recipe,
                0,
                CreateActivation((uint)(10_000 + i), activePose: true),
                handoff,
                createdBodies);
        }

        for (int tick = 0; tick < 60; tick++)
        {
            SubmitTargetsAndStep(ragdolls, physics, instances, targets, tick);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long mainBefore = GC.GetAllocatedBytesForCurrentThread();
        long workersBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int tick = 60; tick < 180; tick++)
        {
            SubmitTargetsAndStep(ragdolls, physics, instances, targets, tick);
        }

        long mainAllocated = GC.GetAllocatedBytesForCurrentThread() - mainBefore;
        long workerAllocated = dispatcher.BackgroundWorkerAllocatedBytes - workersBefore;
        Assert.Multiple(() =>
        {
            Assert.That(mainAllocated, Is.Zero, $"Ragdoll fixed-step path allocated {mainAllocated} managed bytes after warmup.");
            Assert.That(workerAllocated, Is.Zero, $"Ragdoll Physics3D workers allocated {workerAllocated} managed bytes after warmup.");
        });
    }

    private static void RunPressureScenario(int instanceCount, bool useMixedTopologies, int activePoseStride)
    {
        int maximumBones = useMixedTopologies ? 4 : 2;
        int mobileCapacity = instanceCount * maximumBones;
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity,
            0,
            shapeCapacity: 12,
            constraintCapacity: mobileCapacity * 4,
            workerCount: 2);
        using var ragdolls = CreateRagdollWorld(
            physics,
            useMixedTopologies ? 2 : 1,
            useMixedTopologies ? 7 : 2,
            instanceCount,
            maximumBones);
        using World ecs = World.Create();
        RagdollRecipeDefinition chain = useMixedTopologies ? CreateChainRecipe(701) : CreateTwoBoneRecipe(703);
        RagdollRecipeDefinition? branch = useMixedTopologies ? CreateBranchRecipe(702) : null;
        RagdollRecipeId chainId = ragdolls.RegisterRecipe(chain);
        RagdollRecipeId branchId = branch == null ? default : ragdolls.RegisterRecipe(branch);
        int expectedBones = 0;
        int expectedConstraints = 0;
        for (int i = 0; i < instanceCount; i++)
        {
            bool useBranch = branch != null && (i & 1) != 0;
            RagdollRecipeDefinition definition = useBranch ? branch! : chain;
            RagdollRecipeId recipe = useBranch ? branchId : chainId;
            RagdollBoneHandoff[] handoff = CreateAuthoredHandoff(
                ecs,
                definition,
                new Vector3((i % 25) * 350f, 1_000f, (i / 25) * 350f));
            var bodies = new Physics3DBodyId[definition.Bones.Length];
            bool activePose = i % activePoseStride == 0;
            ragdolls.TransitionFromAnimation(
                recipe,
                0,
                CreateActivation((uint)(20_000 + i), activePose),
                handoff,
                bodies);
            expectedBones += definition.Bones.Length;
            expectedConstraints += (definition.Bones.Length - 1) * (activePose ? 4 : 3);
        }

        for (int tick = 0; tick < 4; tick++)
        {
            Step(ragdolls, physics, tick);
        }

        Assert.Multiple(() =>
        {
            Assert.That(ragdolls.ActiveInstanceCount, Is.EqualTo(instanceCount));
            Assert.That(ragdolls.ActiveBoneCount, Is.EqualTo(expectedBones));
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(expectedBones));
            Assert.That(ragdolls.ActiveConstraintCount, Is.EqualTo(expectedConstraints));
            Assert.That(physics.FixedDeltaSeconds, Is.EqualTo(1f / 30f));
        });
    }

    private static void SubmitTargetsAndStep(
        RagdollWorld ragdolls,
        Physics3DWorld physics,
        RagdollInstanceId[] instances,
        Quaternion[] targets,
        int tick)
    {
        for (int i = 0; i < instances.Length; i++)
        {
            ragdolls.SubmitActivePose(instances[i], tick, targets.AsSpan(i * 2, 2));
        }

        Step(ragdolls, physics, tick);
    }

    private static void Step(RagdollWorld ragdolls, Physics3DWorld physics, int tick)
    {
        ragdolls.PrepareFixedStep(tick);
        physics.Step();
        ragdolls.ObserveFixedStep(tick);
    }

    private static RagdollWorld CreateRagdollWorld(
        IPhysics3DWorld physics,
        int recipeCapacity,
        int recipeBoneCapacity,
        int instanceCapacity,
        int maximumBones)
    {
        return new RagdollWorld(physics, new RagdollConfig
        {
            RecipeCapacity = recipeCapacity,
            RecipeBoneCapacity = recipeBoneCapacity,
            InstanceCapacity = instanceCapacity,
            MaximumBonesPerInstance = maximumBones,
            RecoveryOverlapHitCapacity = 32,
            FixedStepHz = 30
        });
    }

    private static Physics3DWorld CreatePhysicsWorld(
        int mobileCapacity,
        int staticCapacity,
        int shapeCapacity,
        int constraintCapacity,
        int workerCount = 1)
    {
        return new Physics3DWorld(CreatePhysicsConfig(
            mobileCapacity,
            staticCapacity,
            shapeCapacity,
            workerCount,
            constraintCapacity));
    }

    private static Physics3DWorldConfig CreatePhysicsConfig(
        int mobileCapacity,
        int staticCapacity,
        int shapeCapacity,
        int workerCount,
        int constraintCapacity)
    {
        return new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticCapacity,
            ShapeCapacity = shapeCapacity,
            InactiveIslandCapacity = Math.Max(1, mobileCapacity),
            ConstraintCapacity = constraintCapacity,
            ConstraintsPerTypeBatchCapacity = Math.Max(1, constraintCapacity),
            ConstraintCountPerBodyEstimate = 8,
            ContactPairCapacityPerWorker = Math.Max(64, mobileCapacity * 4),
            ActuationCommandCapacity = Math.Max(1, mobileCapacity),
            WorkerCount = workerCount,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = Vector3.Zero,
            LinearDamping = 0f,
            AngularDamping = 0f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0f,
            MinimumTimestepCountUnderSleepThreshold = byte.MaxValue,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        };
    }

    private static RagdollActivationDescription CreateActivation(uint assemblyId, bool activePose)
        => new(
            assemblyId,
            80f,
            LayerMask.All,
            Material,
            Physics3DContinuousDetectionMode.Passive,
            activePose);

    private static RagdollRecipeDefinition CreateTwoBoneRecipe(
        int stableId,
        float maximumSwing = 0.8f,
        float activePoseForce = 500_000f)
    {
        return new RagdollRecipeDefinition
        {
            StableId = stableId,
            Recovery = new RagdollRecoverySettings(
                RagdollRecoveryStrategy.PreserveRootYaw,
                new Vector3(0f, 60f, 0f),
                300f),
            Bones = new[]
            {
                CreateBone(1, -1, Vector3.Zero, RagdollShapeDefinition.Box(new Vector3(30f, 40f, 20f)), 0.6f, 0, uint.MaxValue),
                CreateBone(2, 0, new Vector3(0f, 45f, 0f), RagdollShapeDefinition.Capsule(10f, 25f), 0.4f, 1, uint.MaxValue, maximumSwing, activePoseForce)
            }
        };
    }

    private static RagdollRecipeDefinition CreateChainRecipe(int stableId)
    {
        return new RagdollRecipeDefinition
        {
            StableId = stableId,
            Recovery = new RagdollRecoverySettings(RagdollRecoveryStrategy.PreserveRootYaw, new Vector3(0f, 65f, 0f), 350f),
            Bones = new[]
            {
                CreateBone(10, -1, Vector3.Zero, RagdollShapeDefinition.Box(new Vector3(36f, 44f, 24f)), 0.4f, 0, uint.MaxValue),
                CreateBone(11, 0, new Vector3(0f, 45f, 0f), RagdollShapeDefinition.Sphere(12f), 0.3f, 1, uint.MaxValue),
                CreateBone(12, 1, new Vector3(0f, 32f, 0f), RagdollShapeDefinition.Capsule(9f, 24f), 0.3f, 2, uint.MaxValue)
            }
        };
    }

    private static RagdollRecipeDefinition CreateBranchRecipe(int stableId)
    {
        return new RagdollRecipeDefinition
        {
            StableId = stableId,
            Recovery = new RagdollRecoverySettings(RagdollRecoveryStrategy.FaceWorldForward, new Vector3(0f, 70f, 0f), 250f),
            Bones = new[]
            {
                CreateBone(20, -1, Vector3.Zero, RagdollShapeDefinition.Box(new Vector3(40f, 50f, 25f)), 0.4f, 0, uint.MaxValue),
                CreateBone(21, 0, new Vector3(-35f, 15f, 0f), RagdollShapeDefinition.Capsule(8f, 35f), 0.2f, 1, uint.MaxValue),
                CreateBone(22, 0, new Vector3(35f, 15f, 0f), RagdollShapeDefinition.Capsule(8f, 35f), 0.2f, 2, uint.MaxValue),
                CreateBone(23, 0, new Vector3(0f, 45f, 0f), RagdollShapeDefinition.Sphere(13f), 0.2f, 3, uint.MaxValue)
            }
        };
    }

    private static RagdollRecipeDefinition CreateCollisionRecipe()
    {
        return new RagdollRecipeDefinition
        {
            StableId = 250,
            Recovery = new RagdollRecoverySettings(RagdollRecoveryStrategy.FaceWorldForward, new Vector3(0f, 50f, 0f), 100f),
            Bones = new[]
            {
                CreateBone(30, -1, Vector3.Zero, RagdollShapeDefinition.Sphere(25f), 0.34f, 0, uint.MaxValue),
                CreateBone(31, 0, new Vector3(0f, 60f, 0f), RagdollShapeDefinition.Sphere(25f), 0.33f, 1, uint.MaxValue),
                CreateBone(32, 1, new Vector3(0f, 60f, 0f), RagdollShapeDefinition.Sphere(25f), 0.33f, 2, uint.MaxValue)
            }
        };
    }

    private static RagdollBoneDefinition CreateBone(
        int stableId,
        int parentIndex,
        Vector3 localPosition,
        RagdollShapeDefinition shape,
        float massRatio,
        int subgroupIndex,
        uint collisionMask,
        float maximumSwing = 0.8f,
        float activePoseForce = 500_000f)
    {
        return new RagdollBoneDefinition
        {
            StableId = stableId,
            ParentIndex = parentIndex,
            LocalPositionCm = localPosition,
            LocalOrientation = Quaternion.Identity,
            Shape = shape,
            MassRatio = massRatio,
            ParentAnchorLocalCm = parentIndex < 0 ? Vector3.Zero : localPosition * 0.5f,
            BoneAnchorLocalCm = parentIndex < 0 ? Vector3.Zero : -localPosition * 0.5f,
            JointFrameLocalParent = Quaternion.Identity,
            JointFrameLocalBone = Quaternion.Identity,
            MaximumSwingAngleRadians = maximumSwing,
            MinimumTwistAngleRadians = -0.6f,
            MaximumTwistAngleRadians = 0.6f,
            JointSpring = JointSpring,
            CollisionSubgroupIndex = subgroupIndex,
            CollidesWithSubgroupsMask = collisionMask,
            ActivePoseServo = new Physics3DServoSettings(ActiveServo.MaximumSpeed, ActiveServo.BaseSpeed, activePoseForce),
            ActivePoseSpring = ActiveSpring
        };
    }

    private static RagdollBoneHandoff[] CreateAuthoredHandoff(
        World ecs,
        RagdollRecipeDefinition definition,
        Vector3 rootPosition)
    {
        var result = new RagdollBoneHandoff[definition.Bones.Length];
        var positions = new Vector3[definition.Bones.Length];
        var orientations = new Quaternion[definition.Bones.Length];
        for (int i = 0; i < definition.Bones.Length; i++)
        {
            RagdollBoneDefinition bone = definition.Bones[i];
            if (bone.ParentIndex < 0)
            {
                positions[i] = rootPosition;
                orientations[i] = Quaternion.Identity;
            }
            else
            {
                positions[i] = positions[bone.ParentIndex] + Vector3.Transform(bone.LocalPositionCm, orientations[bone.ParentIndex]);
                orientations[i] = Quaternion.Normalize(Quaternion.Concatenate(bone.LocalOrientation, orientations[bone.ParentIndex]));
            }

            Entity entity = ecs.Create(new RagdollTestEntityTag { BoneStableId = bone.StableId });
            result[i] = new RagdollBoneHandoff(entity, positions[i], orientations[i], Vector3.Zero, Vector3.Zero);
        }

        return result;
    }

    private static RagdollBoneHandoff WithPosition(in RagdollBoneHandoff source, Vector3 position)
        => new(source.Entity, position, source.Orientation, source.LinearVelocityCmPerSecond, source.AngularVelocityRadiansPerSecond);

    private static RagdollBoneHandoff WithOrientation(in RagdollBoneHandoff source, Quaternion orientation)
        => new(source.Entity, source.PositionCm, orientation, source.LinearVelocityCmPerSecond, source.AngularVelocityRadiansPerSecond);

    private static Physics3DBodyDescription CreateStaticBody(Physics3DShapeId shape, Vector3 position)
        => new(
            Entity.Null,
            Physics3DBodyKind.Static,
            shape,
            position,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            0f,
            LayerMask.All,
            Material,
            Physics3DContinuousDetectionMode.Discrete);

    private static bool ContainsPair(ReadOnlySpan<Physics3DContactPair> contacts, Physics3DBodyId a, Physics3DBodyId b)
    {
        for (int i = 0; i < contacts.Length; i++)
        {
            if ((contacts[i].BodyA == a && contacts[i].BodyB == b) ||
                (contacts[i].BodyA == b && contacts[i].BodyB == a))
            {
                return true;
            }
        }

        return false;
    }

    private static float AxisAngle(Quaternion a, Quaternion b)
    {
        Vector3 axisA = Vector3.Transform(Vector3.UnitY, a);
        Vector3 axisB = Vector3.Transform(Vector3.UnitY, b);
        return MathF.Acos(Math.Clamp(Vector3.Dot(axisA, axisB), -1f, 1f));
    }

    private static float QuaternionAngle(Quaternion a, Quaternion b)
        => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));

    private struct RagdollTestEntityTag
    {
        public int BoneStableId;
    }
}
