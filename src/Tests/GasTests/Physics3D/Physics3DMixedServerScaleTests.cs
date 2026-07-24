using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using Ludots.Core.Ragdoll;
using Ludots.Core.Vehicle3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DMixedServerScaleTests
{
    private static readonly MixedRagdollBoneSpec[] HumanoidRagdollBones =
    [
        new(100, -1, Vector3.Zero, RagdollShapeDefinition.Box(new Vector3(36f, 30f, 24f)), 0.12f, 0, 0.8f, 500_000f, Vector3.UnitY, 0f),
        new(101, 0, new Vector3(0f, 32f, 0f), RagdollShapeDefinition.Capsule(14f, 24f), 0.09f, 1, 0.35f, 500_000f, Vector3.UnitY, 1f),
        new(102, 1, new Vector3(0f, 40f, 0f), RagdollShapeDefinition.Box(new Vector3(50f, 40f, 26f)), 0.15f, 2, 0.4f, 500_000f, Vector3.UnitY, -0.6f),
        new(103, 2, new Vector3(0f, 32f, 0f), RagdollShapeDefinition.Capsule(7f, 12f), 0.02f, 3, 0.5f, 500_000f, Vector3.UnitY, 0.45f),
        new(104, 3, new Vector3(0f, 26f, 0f), RagdollShapeDefinition.Sphere(13f), 0.08f, 4, 0.6f, 500_000f, Vector3.UnitY, -0.3f),
        new(105, 2, new Vector3(-42f, 14f, 0f), RagdollShapeDefinition.Box(new Vector3(44f, 14f, 16f)), 0.03f, 5, 1.4f, 500_000f, Vector3.UnitZ, 1f),
        new(106, 5, new Vector3(-40f, 0f, 0f), RagdollShapeDefinition.Box(new Vector3(40f, 12f, 14f)), 0.022f, 6, 1.1f, 500_000f, Vector3.UnitZ, 0.75f),
        new(107, 6, new Vector3(-30f, 0f, 0f), RagdollShapeDefinition.Box(new Vector3(22f, 10f, 16f)), 0.008f, 7, 0.7f, 500_000f, Vector3.UnitZ, 0.35f),
        new(108, 2, new Vector3(42f, 14f, 0f), RagdollShapeDefinition.Box(new Vector3(44f, 14f, 16f)), 0.03f, 8, 1.4f, 500_000f, Vector3.UnitZ, -1f),
        new(109, 8, new Vector3(40f, 0f, 0f), RagdollShapeDefinition.Box(new Vector3(40f, 12f, 14f)), 0.022f, 9, 1.1f, 500_000f, Vector3.UnitZ, -0.75f),
        new(110, 9, new Vector3(30f, 0f, 0f), RagdollShapeDefinition.Box(new Vector3(22f, 10f, 16f)), 0.008f, 10, 0.7f, 500_000f, Vector3.UnitZ, -0.35f),
        new(111, 0, new Vector3(-18f, -40f, 0f), RagdollShapeDefinition.Capsule(11f, 44f), 0.11f, 11, 0.9f, 500_000f, Vector3.UnitX, 0.7f),
        new(112, 11, new Vector3(0f, -48f, 0f), RagdollShapeDefinition.Capsule(9f, 42f), 0.08f, 12, 0.8f, 500_000f, Vector3.UnitX, 0.5f),
        new(113, 12, new Vector3(0f, -36f, 12f), RagdollShapeDefinition.Box(new Vector3(22f, 14f, 36f)), 0.02f, 13, 0.5f, 500_000f, Vector3.UnitX, 0.25f),
        new(114, 0, new Vector3(18f, -40f, 0f), RagdollShapeDefinition.Capsule(11f, 44f), 0.11f, 14, 0.9f, 500_000f, Vector3.UnitX, -0.7f),
        new(115, 14, new Vector3(0f, -48f, 0f), RagdollShapeDefinition.Capsule(9f, 42f), 0.08f, 15, 0.8f, 500_000f, Vector3.UnitX, -0.5f),
        new(116, 15, new Vector3(0f, -36f, 12f), RagdollShapeDefinition.Box(new Vector3(22f, 14f, 36f)), 0.02f, 16, 0.5f, 500_000f, Vector3.UnitX, -0.25f)
    ];
    private static readonly int HumanoidRagdollUniqueShapeCount = CountUniqueRagdollShapes(HumanoidRagdollBones);

    private static int CountUniqueRagdollShapes(ReadOnlySpan<MixedRagdollBoneSpec> bones)
    {
        int uniqueCount = 0;
        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            RagdollShapeDefinition shape = bones[boneIndex].Shape;
            bool alreadyRegistered = false;
            for (int previousIndex = 0; previousIndex < boneIndex; previousIndex++)
            {
                RagdollShapeDefinition previous = bones[previousIndex].Shape;
                if (previous.Kind == shape.Kind && previous.DimensionsCm == shape.DimensionsCm)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                uniqueCount++;
            }
        }

        return uniqueCount;
    }

    // Feature: one authoritative 30Hz battle shared by 150 connected players.
    // Scenario: players walk, drive, and remain in ragdoll while platforms, wind,
    // friction, ordinary bodies, queries, snapshots, and AOI all advance in one tick.
    // Given every player's input for the next server tick has arrived
    // When the server prepares gameplay, advances physics once, and observes the result
    // Then only that complete tick is committed and exposed through the authoritative snapshot data plane.

    [Test]
    public void MixedAuthoritativeDataPlane_UsesOneWorldOneStepAndCommittedSnapshots()
    {
        MixedServerScaleProfile profile = MixedServerScaleProfile.Correctness;
        using var scenario = new MixedServerScenario(profile);

        for (int i = 0; i < profile.CorrectnessTickCount; i++)
        {
            scenario.ExecuteTick();
        }

        Assert.Multiple(() =>
        {
            Assert.That(scenario.Physics.StepIndex, Is.EqualTo(profile.CorrectnessTickCount));
            Assert.That(scenario.NetworkLifecycle.CommittedTick, Is.EqualTo(profile.CorrectnessTickCount));
            Assert.That(scenario.NetworkLifecycle.ExecutingTick, Is.Zero);
            Assert.That(scenario.NetworkLifecycle.SnapshotTick, Is.EqualTo(profile.ExpectedLastSnapshotTick(profile.CorrectnessTickCount)));
            Assert.That(scenario.SnapshotStore.SnapshotTick, Is.EqualTo(profile.ExpectedLastSnapshotTick(profile.CorrectnessTickCount)));
            Assert.That(scenario.SnapshotPublishCount, Is.EqualTo(profile.ExpectedSnapshotPublishCount(profile.CorrectnessTickCount)));
            Assert.That(scenario.NetworkLifecycle.SnapshotHz, Is.EqualTo(profile.SnapshotHz));
            Assert.That(scenario.NetworkLifecycle.SnapshotIntervalTicks, Is.EqualTo(profile.SnapshotIntervalTicks));
            Assert.That(scenario.CharacterCount, Is.EqualTo(profile.CharacterCount));
            Assert.That(scenario.VehicleCount, Is.EqualTo(profile.TotalVehicleCount));
            Assert.That(scenario.RagdollCount, Is.EqualTo(profile.RagdollCount));
            Assert.That(scenario.RagdollBoneCount, Is.EqualTo(profile.ExpectedRagdollBodyCount));
            Assert.That(profile.BonesPerRagdoll, Is.EqualTo(17));
            Assert.That(scenario.PlayerCount, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.SupplementalQueriesExecutedLastTick, Is.EqualTo(profile.SupplementalQueryCount));
            Assert.That(scenario.SupplementalQueryHitsLastTick, Is.GreaterThan(0));
            Assert.That(scenario.SnapshotCount, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.MinimumAoiDeltaWritesLastSnapshot, Is.GreaterThan(0));
            Assert.That(scenario.AoiClientsContainingSelfLastSnapshot, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.DistinctAoiInterestSetsObservedLastSnapshot, Is.True);
            Assert.That(scenario.BaselineMissesLastSnapshot, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.FullSnapshotSendsLastSnapshot, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.BaselineAcknowledgementsLastSnapshot, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.Physics.ActiveMobileBodyCount, Is.EqualTo(profile.ExpectedMobileBodyCount));
            Assert.That(scenario.Physics.ActiveConstraintCount, Is.EqualTo(profile.ExpectedConstraintCount));
            Assert.That(scenario.Physics.RegisteredShapeCount, Is.EqualTo(profile.ExpectedShapeCount));
            Assert.That(scenario.Physics.ActuationCommandCapacity, Is.EqualTo(profile.RequiredActuationCommandCapacity));
            Assert.That(scenario.ActivePoseTargetsSubmittedLastTick, Is.EqualTo(profile.ExpectedActivePoseTargetsPerTick));
            Assert.That(scenario.AllPlayerRepresentativesAreFinite(), Is.True);
            Assert.That(scenario.AllWalkingPlayersMovedFromSpawn(), Is.True);
            Assert.That(scenario.AllDrivingPlayersRespondedToInput(), Is.True);
            Assert.That(scenario.AllRagdollPlayersChangedPoseAndVelocity(), Is.True);
        });
    }

    [Test]
    public void AuthoritativeInputCutoff_RejectsSameTickMutationAfterExecuteBegins()
    {
        using var scenario = new MixedServerScenario(MixedServerScaleProfile.Correctness);

        Physics3DNetInputArrivalResult result = scenario.BeginTickAndSubmitSameTickAfterExecutionCutoff();

        Assert.Multiple(() =>
        {
            Assert.That(scenario.NetworkLifecycle.ExecutingTick, Is.EqualTo(1));
            Assert.That(scenario.NetworkLifecycle.CommittedTick, Is.Zero);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.RejectedAtExecutionCutoff));
            Assert.That(scenario.InputRing.ExecutionCutoffRejectionCount, Is.EqualTo(1));
            Assert.That(scenario.InputRing.ConflictCount, Is.Zero);
        });
    }

    [Test]
    [Explicit("150-player shared-world server gate: 10K isolated integration bodies, mixed gameplay, observed supplemental queries, snapshots and AOI for 600 measured 30Hz ticks.")]
    public void OneHundredFiftyPlayerMixedServer_SixHundredTicks_MeetsThirtyHertzAndZeroGcGate()
    {
        MixedServerScaleProfile profile = MixedServerScaleProfile.FullGate;
        using var scenario = new MixedServerScenario(profile);

        for (int i = 0; i < profile.WarmupTickCount; i++)
        {
            scenario.ExecuteTick();
        }

        var report = new MixedServerScaleReport(profile.SampleTickCount);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 256; i++)
        {
            _ = GC.GetAllocatedBytesForCurrentThread();
        }

        long callingThreadBefore = GC.GetAllocatedBytesForCurrentThread();
        int minimumAwakeBodies = int.MaxValue;
        int peakContacts = 0;
        int minimumConstraints = int.MaxValue;
        int minimumQueryHits = int.MaxValue;
        bool hasKernelStageBreakdown = true;
        for (int sample = 0; sample < profile.SampleTickCount; sample++)
        {
            long tickStarted = Stopwatch.GetTimestamp();
            scenario.ExecuteTick();
            long elapsedTicks = Stopwatch.GetTimestamp() - tickStarted;
            Physics3DStepMetrics metrics = scenario.Physics.LastStepMetrics;
            MixedServerTickAllocation allocation = scenario.LastTickAllocation;
            report.Record(sample, elapsedTicks, in metrics, in allocation);
            hasKernelStageBreakdown &= metrics.HasKernelStageBreakdown;
            minimumAwakeBodies = Math.Min(minimumAwakeBodies, scenario.Physics.AwakeBodyCount);
            peakContacts = Math.Max(peakContacts, scenario.Physics.ContactPairCount);
            minimumConstraints = Math.Min(minimumConstraints, scenario.Physics.ActiveConstraintCount);
            minimumQueryHits = Math.Min(minimumQueryHits, scenario.SupplementalQueryHitsLastTick);
        }

        long callingThreadAllocated = GC.GetAllocatedBytesForCurrentThread() - callingThreadBefore;
        report.Write(
            profile,
            callingThreadAllocated,
            minimumAwakeBodies,
            peakContacts,
            minimumConstraints,
            minimumQueryHits,
            scenario.Physics.ActiveMobileBodyCount);

        double fullTickP95 = report.FullTick.Percentile(0.95);
        double fullTickP99 = report.FullTick.Percentile(0.99);
        long expectedFinalTick = profile.WarmupTickCount + profile.SampleTickCount;
        Assert.Multiple(() =>
        {
            Assert.That(hasKernelStageBreakdown, Is.True, "Production Physics3D stage timing was unavailable.");
            Assert.That(scenario.PlayerCount, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.CharacterCount, Is.EqualTo(profile.CharacterCount));
            Assert.That(scenario.VehicleCount, Is.EqualTo(profile.TotalVehicleCount));
            Assert.That(scenario.RagdollCount, Is.EqualTo(profile.RagdollCount));
            Assert.That(scenario.RagdollBoneCount, Is.EqualTo(profile.ExpectedRagdollBodyCount));
            Assert.That(scenario.SupplementalQueriesExecutedLastTick, Is.EqualTo(profile.SupplementalQueryCount));
            Assert.That(scenario.Physics.ActiveMobileBodyCount, Is.EqualTo(profile.ExpectedMobileBodyCount));
            Assert.That(scenario.Physics.RegisteredShapeCount, Is.EqualTo(profile.ExpectedShapeCount));
            Assert.That(scenario.Physics.ActuationCommandCapacity, Is.EqualTo(profile.RequiredActuationCommandCapacity));
            Assert.That(scenario.ActivePoseTargetsSubmittedLastTick, Is.EqualTo(profile.ExpectedActivePoseTargetsPerTick));
            Assert.That(minimumAwakeBodies, Is.GreaterThanOrEqualTo(profile.OrdinaryActiveBodyCount));
            Assert.That(minimumConstraints, Is.EqualTo(profile.ExpectedConstraintCount));
            Assert.That(minimumQueryHits, Is.GreaterThan(0));
            Assert.That(scenario.NetworkLifecycle.CommittedTick, Is.EqualTo(expectedFinalTick));
            Assert.That(scenario.NetworkLifecycle.SnapshotTick, Is.EqualTo(profile.ExpectedLastSnapshotTick(expectedFinalTick)));
            Assert.That(scenario.SnapshotStore.SnapshotTick, Is.EqualTo(profile.ExpectedLastSnapshotTick(expectedFinalTick)));
            Assert.That(scenario.SnapshotPublishCount, Is.EqualTo(profile.ExpectedSnapshotPublishCount(expectedFinalTick)));
            Assert.That(scenario.NetworkLifecycle.SnapshotHz, Is.EqualTo(profile.SnapshotHz));
            Assert.That(scenario.NetworkLifecycle.SnapshotIntervalTicks, Is.EqualTo(profile.SnapshotIntervalTicks));
            Assert.That(scenario.SnapshotCount, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.MinimumAoiDeltaWritesLastSnapshot, Is.GreaterThan(0));
            Assert.That(scenario.AoiClientsContainingSelfLastSnapshot, Is.EqualTo(profile.PlayerCount));
            Assert.That(scenario.DistinctAoiInterestSetsObservedLastSnapshot, Is.True);
            Assert.That(scenario.BaselineMissesLastSnapshot, Is.Zero);
            Assert.That(scenario.FullSnapshotSendsLastSnapshot, Is.Zero);
            Assert.That(scenario.BaselineAcknowledgementsLastSnapshot, Is.Zero);
            Assert.That(callingThreadAllocated, Is.Zero, "The warmed full authoritative tick allocated managed memory on the calling thread.");
            Assert.That(report.PhysicsCallingThreadAllocatedBytes, Is.Zero, "Production Physics3D metrics reported calling-thread allocations.");
            Assert.That(report.PhysicsWorkerAllocatedBytes, Is.Zero, "Production Physics3D workers allocated managed memory.");
            Assert.That(fullTickP95, Is.LessThanOrEqualTo(profile.FixedTickBudgetMilliseconds));
            Assert.That(fullTickP99, Is.LessThanOrEqualTo(profile.FixedTickBudgetMilliseconds));
            Assert.That(scenario.AllPlayerRepresentativesAreFinite(), Is.True);
            Assert.That(scenario.AllWalkingPlayersMovedFromSpawn(), Is.True);
            Assert.That(scenario.AllDrivingPlayersRespondedToInput(), Is.True);
            Assert.That(scenario.AllRagdollPlayersChangedPoseAndVelocity(), Is.True);
        });
    }

    private sealed class MixedServerScenario : IDisposable
    {
        private const uint GroundCategory = 1u << 0;
        private const uint VehicleCategory = 1u << 1;
        private const uint RagdollCategory = 1u << 2;
        private const uint CharacterCategory = 1u << 3;
        private const uint OrdinaryCategory = 1u << 4;
        private const long SnapshotBaselineId = 1;

        private static readonly LayerMask GroundBodyLayer = new(
            GroundCategory,
            VehicleCategory | RagdollCategory | CharacterCategory);
        private static readonly LayerMask GroundQueryLayer = new(0u, GroundCategory);
        private static readonly LayerMask VehicleBodyLayer = new(VehicleCategory, GroundCategory);
        private static readonly LayerMask RagdollBodyLayer = new(RagdollCategory, GroundCategory);
        private static readonly LayerMask CharacterBodyLayer = new(CharacterCategory, GroundCategory);
        private static readonly LayerMask OrdinaryBodyLayer = new(OrdinaryCategory, 0u);
        private static readonly Physics3DMaterial GroundMaterial = new(0.95f, 200f, 30f, 2f);
        private static readonly Physics3DMaterial DynamicMaterial = new(0.8f, 200f, 30f, 2f);
        private static readonly Physics3DSpringSettings JointSpring = new(24f, 2f);
        private static readonly Physics3DSpringSettings ActivePoseSpring = new(18f, 2f);
        private static readonly Physics3DServoSettings ActivePoseServo = new(20f, 0f, 500_000f);

        private readonly MixedServerScaleProfile _profile;
        private readonly Character3DControllerSet _characters;
        private readonly Vehicle3DWorld _vehicles;
        private readonly RagdollWorld _ragdolls;
        private readonly Physics3DForceFieldSet _forceFields;
        private readonly Physics3DAwakeBodyBuffer _awakeBodies;
        private readonly World _ecs;
        private readonly Character3DHandle[] _characterHandles;
        private readonly Vehicle3DVehicleId[] _vehicleIds;
        private readonly RagdollInstanceId[] _ragdollIds;
        private readonly Physics3DBodyId[] _playerRepresentativeBodies;
        private readonly Physics3DNetReplicationMode[] _playerReplicationModes;
        private readonly int[] _missingPlayerSlots;
        private readonly Physics3DNetSnapshotEntityWrite[] _snapshotWrites;
        private readonly Physics3DNetAoiInterest[] _aoiInterest;
        private readonly Physics3DNetSnapshotEntityWrite[] _aoiDelta;
        private readonly int[] _aoiEntityIndices;
        private readonly int[] _firstClientAoiEntityIds;
        private readonly Physics3DNetSnapshotEntityView[] _fullSnapshotSendBuffer;
        private readonly Physics3DBodyState[] _initialPlayerStates;
        private readonly Physics3DBodyId[] _playerRagdollPoseBodies;
        private readonly Physics3DBodyState[] _initialPlayerRagdollPoseStates;
        private readonly byte[] _playerRagdollVelocityChanged;
        private readonly byte[] _playerRagdollPoseChanged;
        private readonly Quaternion[] _ragdollPoseTargets;
        private readonly Physics3DRaycastQuery[] _rayQueries;
        private readonly Physics3DBatchedRaycastClosestResult[] _rayResults;
        private readonly Physics3DSphereCastQuery[] _sphereQueries;
        private readonly Physics3DBatchedShapeCastClosestResult[] _sphereResults;
        private readonly Physics3DCapsuleCastQuery[] _capsuleQueries;
        private readonly Physics3DBatchedShapeCastClosestResult[] _capsuleResults;
        private readonly Physics3DBoxCastQuery[] _boxQueries;
        private readonly Physics3DBatchedShapeCastClosestResult[] _boxResults;
        private readonly Physics3DBodyId _movingPlatform;
        private readonly Physics3DBodyId _conveyorPlatform;
        private bool _disposed;

        public MixedServerScenario(MixedServerScaleProfile profile)
        {
            _profile = profile;
            Physics = new Physics3DWorld(CreatePhysicsConfig(profile));
            _ecs = World.Create();
            _characters = new Character3DControllerSet(Physics, profile.CharacterCount, overlapHitCapacity: 32);
            _vehicles = new Vehicle3DWorld(Physics, new Vehicle3DConfig
            {
                VehicleCapacity = profile.TotalVehicleCount,
                WheelCapacity = profile.TotalVehicleCount * profile.WheelsPerVehicle,
                QueryBatchCapacity = profile.TotalVehicleCount * profile.WheelsPerVehicle,
                FixedStepHz = profile.FixedStepHz
            });
            _ragdolls = new RagdollWorld(Physics, new RagdollConfig
            {
                RecipeCapacity = 1,
                RecipeBoneCapacity = profile.BonesPerRagdoll,
                InstanceCapacity = profile.RagdollCount,
                MaximumBonesPerInstance = profile.BonesPerRagdoll,
                RecoveryOverlapHitCapacity = 32,
                FixedStepHz = profile.FixedStepHz
            });
            _forceFields = new Physics3DForceFieldSet(fieldCapacity: 1, awakeBodyCapacity: profile.ExpectedMobileBodyCount);
            _awakeBodies = new Physics3DAwakeBodyBuffer(profile.ExpectedMobileBodyCount);
            _characterHandles = new Character3DHandle[profile.CharacterCount];
            _vehicleIds = new Vehicle3DVehicleId[profile.TotalVehicleCount];
            _ragdollIds = new RagdollInstanceId[profile.RagdollCount];
            _playerRepresentativeBodies = new Physics3DBodyId[profile.PlayerCount];
            _playerReplicationModes = new Physics3DNetReplicationMode[profile.PlayerCount];
            _missingPlayerSlots = new int[profile.PlayerCount];
            _snapshotWrites = new Physics3DNetSnapshotEntityWrite[profile.PlayerCount];
            _aoiInterest = new Physics3DNetAoiInterest[profile.AoiEntityCountPerClient];
            _aoiDelta = new Physics3DNetSnapshotEntityWrite[profile.AoiEntityCountPerClient * 2];
            _aoiEntityIndices = new int[profile.AoiEntityCountPerClient];
            _firstClientAoiEntityIds = new int[profile.AoiEntityCountPerClient];
            _fullSnapshotSendBuffer = new Physics3DNetSnapshotEntityView[profile.PlayerCount];
            _initialPlayerStates = new Physics3DBodyState[profile.PlayerCount];
            _playerRagdollPoseBodies = new Physics3DBodyId[profile.PlayerRagdollCount];
            _initialPlayerRagdollPoseStates = new Physics3DBodyState[profile.PlayerRagdollCount];
            _playerRagdollVelocityChanged = new byte[profile.PlayerRagdollCount];
            _playerRagdollPoseChanged = new byte[profile.PlayerRagdollCount];
            _ragdollPoseTargets = new Quaternion[profile.BonesPerRagdoll];
            Array.Fill(_ragdollPoseTargets, Quaternion.Identity);

            int rayCount = profile.SupplementalQueryCount / 4;
            int sphereCount = profile.SupplementalQueryCount / 4;
            int capsuleCount = profile.SupplementalQueryCount / 4;
            int boxCount = profile.SupplementalQueryCount - rayCount - sphereCount - capsuleCount;
            _rayQueries = new Physics3DRaycastQuery[rayCount];
            _rayResults = new Physics3DBatchedRaycastClosestResult[rayCount];
            _sphereQueries = new Physics3DSphereCastQuery[sphereCount];
            _sphereResults = new Physics3DBatchedShapeCastClosestResult[sphereCount];
            _capsuleQueries = new Physics3DCapsuleCastQuery[capsuleCount];
            _capsuleResults = new Physics3DBatchedShapeCastClosestResult[capsuleCount];
            _boxQueries = new Physics3DBoxCastQuery[boxCount];
            _boxResults = new Physics3DBatchedShapeCastClosestResult[boxCount];

            Physics3DShapeId floorShape = Physics.RegisterBoxShape(new Vector3(100_000f, 20f, 100_000f));
            Physics3DShapeId ordinaryShape = Physics.RegisterSphereShape(10f);
            Physics3DShapeId characterShape = Physics.RegisterCapsuleShape(30f, 100f);
            Physics3DShapeId platformShape = Physics.RegisterBoxShape(new Vector3(1_200f, 20f, 1_200f));
            Physics3DShapeId chassisShape = Physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
            Physics3DShapeId carrierShape = Physics.RegisterBoxShape(new Vector3(20f));
            Physics3DShapeId physicalWheelShape = Physics.RegisterSphereShape(20f);
            Physics3DShapeId boxWheelShape = Physics.RegisterBoxShape(new Vector3(30f, 30f, 20f));
            Physics.CreateBody(CreateBody(
                Physics3DBodyKind.Static,
                floorShape,
                new Vector3(0f, -10f, 0f),
                Vector3.Zero,
                GroundBodyLayer,
                GroundMaterial));
            _movingPlatform = Physics.CreateBody(CreateBody(
                Physics3DBodyKind.Kinematic,
                platformShape,
                new Vector3(-8_000f, 10f, -6_000f),
                Vector3.Zero,
                GroundBodyLayer,
                GroundMaterial));
            _conveyorPlatform = Physics.CreateBody(CreateBody(
                Physics3DBodyKind.Kinematic,
                platformShape,
                new Vector3(-5_000f, 10f, -6_000f),
                Vector3.Zero,
                GroundBodyLayer,
                GroundMaterial,
                Physics3DBodyContactPolicy.SurfaceVelocity(new Vector3(240f, 0f, 0f))));
            Physics.CreateBody(CreateBody(
                Physics3DBodyKind.Kinematic,
                platformShape,
                new Vector3(14_000f, 100f, -1_500f),
                Vector3.Zero,
                GroundBodyLayer,
                GroundMaterial,
                Physics3DBodyContactPolicy.Sensor()));

            CreateOrdinaryBodies(ordinaryShape);
            CreateCharacters(characterShape, ordinaryShape);
            CreateVehicles(chassisShape, carrierShape, physicalWheelShape, boxWheelShape);
            CreateRagdolls();
            ConfigureNetworking();
            ConfigureForceField();
            ConfigureSupplementalQueries();
            CaptureInitialPlayerStates();

            if (Physics.ActiveMobileBodyCount != profile.ExpectedMobileBodyCount)
            {
                throw new InvalidOperationException(
                    $"Mixed server profile expected {profile.ExpectedMobileBodyCount} mobile bodies, but created {Physics.ActiveMobileBodyCount}.");
            }

            if (Physics.ActiveConstraintCount != profile.ExpectedConstraintCount)
            {
                throw new InvalidOperationException(
                    $"Mixed server profile expected {profile.ExpectedConstraintCount} constraints, but created {Physics.ActiveConstraintCount}.");
            }
        }

        public Physics3DWorld Physics { get; }
        public Physics3DNetTickLifecycle NetworkLifecycle { get; private set; } = null!;
        public Physics3DNetInputRing InputRing { get; private set; } = null!;
        public Physics3DNetAuthoritativeSnapshotStore SnapshotStore { get; private set; } = null!;
        public Physics3DNetAoiDeltaBuilder Aoi { get; private set; } = null!;
        public int PlayerCount => _profile.PlayerCount;
        public int CharacterCount => _characters.ActiveCount;
        public int VehicleCount => _vehicles.ActiveVehicleCount;
        public int RagdollCount => _ragdolls.ActiveInstanceCount;
        public int RagdollBoneCount => _ragdolls.ActiveBoneCount;
        public int ActivePoseTargetsSubmittedLastTick { get; private set; }
        public int SupplementalQueriesExecutedLastTick { get; private set; }
        public int SupplementalQueryHitsLastTick { get; private set; }
        public int SnapshotCount => SnapshotStore.Count;
        public int SnapshotPublishCount { get; private set; }
        public int MinimumAoiDeltaWritesLastSnapshot { get; private set; }
        public int AoiClientsContainingSelfLastSnapshot { get; private set; }
        public bool DistinctAoiInterestSetsObservedLastSnapshot { get; private set; }
        public int BaselineMissesLastSnapshot { get; private set; }
        public int FullSnapshotSendsLastSnapshot { get; private set; }
        public int BaselineAcknowledgementsLastSnapshot { get; private set; }
        public MixedServerTickAllocation LastTickAllocation { get; private set; }

        public void ExecuteTick()
        {
            long tick = NetworkLifecycle.CommittedTick + 1;
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            SubmitNetworkInputs(tick);
            long afterInputSubmission = GC.GetAllocatedBytesForCurrentThread();
            if (!InputRing.TryBeginAuthoritativeExecute(tick, _missingPlayerSlots, out Physics3DNetInputExecuteGateResult gate))
            {
                throw new Physics3DNetMissingInputException(tick, gate.MissingCount);
            }
            long afterExecuteGate = GC.GetAllocatedBytesForCurrentThread();

            UpdatePlatforms(tick);
            Physics.CopyAwakeBodies(_awakeBodies);
            _forceFields.Apply(_awakeBodies, Physics);
            SubmitGameplayInputs(tick);
            ExecuteSupplementalQueries();

            _characters.PrepareFixedStep();
            _vehicles.PrepareFixedStep();
            _ragdolls.PrepareFixedStep(tick);
            long afterGameplayPreparation = GC.GetAllocatedBytesForCurrentThread();
            Physics.Step();
            long afterPhysicsStep = GC.GetAllocatedBytesForCurrentThread();
            _characters.ObserveFixedStep();
            _vehicles.ObserveFixedStep();
            _ragdolls.ObserveFixedStep(tick);
            ObserveRagdollPlayerMotion();
            if (Physics.StepIndex != tick || NetworkLifecycle.ExecutingTick != tick)
            {
                throw new InvalidOperationException(
                    $"Authoritative tick {tick} observed Physics3D step {Physics.StepIndex} while network executing tick was {NetworkLifecycle.ExecutingTick}.");
            }

            NetworkLifecycle.Commit();
            InputRing.AcknowledgeInputFramesAfterCommit(tick);
            if (NetworkLifecycle.CommittedTick != Physics.StepIndex)
            {
                throw new InvalidOperationException(
                    $"Committed network tick {NetworkLifecycle.CommittedTick} does not match Physics3D step {Physics.StepIndex}.");
            }
            long afterObservationAndCommit = GC.GetAllocatedBytesForCurrentThread();

            if (NetworkLifecycle.IsSnapshotBoundary(tick))
            {
                PublishSnapshotAndAoi(tick);
            }
            long afterSnapshotPublish = GC.GetAllocatedBytesForCurrentThread();
            LastTickAllocation = new MixedServerTickAllocation(
                afterInputSubmission - allocationStart,
                afterExecuteGate - afterInputSubmission,
                afterGameplayPreparation - afterExecuteGate,
                afterPhysicsStep - afterGameplayPreparation,
                afterObservationAndCommit - afterPhysicsStep,
                afterSnapshotPublish - afterObservationAndCommit);
        }

        public Physics3DNetInputArrivalResult BeginTickAndSubmitSameTickAfterExecutionCutoff()
        {
            const long tick = 1;
            SubmitNetworkInputs(tick);
            if (!InputRing.TryBeginAuthoritativeExecute(tick, _missingPlayerSlots, out Physics3DNetInputExecuteGateResult gate))
            {
                throw new Physics3DNetMissingInputException(tick, gate.MissingCount);
            }

            return InputRing.Submit(new Physics3DNetInputSubmit(
                tick,
                NetworkPlayerId(0),
                generation: 1,
                sequence: 2,
                new Physics3DNetQuantizedAxes2(short.MaxValue, short.MinValue),
                new Physics3DNetQuantizedAxes2(short.MaxValue, short.MinValue),
                buttons: uint.MaxValue));
        }

        public bool AllPlayerRepresentativesAreFinite()
        {
            for (int i = 0; i < _playerRepresentativeBodies.Length; i++)
            {
                Physics3DBodyState state = Physics.GetBodyState(_playerRepresentativeBodies[i]);
                if (!IsFinite(state.PositionCm) ||
                    !IsFinite(state.Orientation) ||
                    !IsFinite(state.LinearVelocityCmPerSecond) ||
                    !IsFinite(state.AngularVelocityRadiansPerSecond))
                {
                    return false;
                }
            }

            return true;
        }

        public bool AllWalkingPlayersMovedFromSpawn()
        {
            for (int player = 0; player < _profile.PlayerWalkingCount; player++)
            {
                Physics3DBodyState current = Physics.GetBodyState(_playerRepresentativeBodies[player]);
                Vector2 displacement = new(
                    current.PositionCm.X - _initialPlayerStates[player].PositionCm.X,
                    current.PositionCm.Z - _initialPlayerStates[player].PositionCm.Z);
                if (displacement.LengthSquared() <= 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        public bool AllDrivingPlayersRespondedToInput()
        {
            int firstPlayer = _profile.PlayerWalkingCount;
            int endPlayer = firstPlayer + _profile.PlayerDrivingCount;
            for (int player = firstPlayer; player < endPlayer; player++)
            {
                Physics3DBodyState current = Physics.GetBodyState(_playerRepresentativeBodies[player]);
                Vector2 horizontalVelocity = new(
                    current.LinearVelocityCmPerSecond.X,
                    current.LinearVelocityCmPerSecond.Z);
                Vector2 horizontalDisplacement = new(
                    current.PositionCm.X - _initialPlayerStates[player].PositionCm.X,
                    current.PositionCm.Z - _initialPlayerStates[player].PositionCm.Z);
                if (horizontalVelocity.LengthSquared() <= 0.01f && horizontalDisplacement.LengthSquared() <= 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        public bool AllRagdollPlayersChangedPoseAndVelocity()
        {
            for (int ragdoll = 0; ragdoll < _profile.PlayerRagdollCount; ragdoll++)
            {
                if (_playerRagdollVelocityChanged[ragdoll] == 0 || _playerRagdollPoseChanged[ragdoll] == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void ObserveRagdollPlayerMotion()
        {
            int firstPlayer = _profile.PlayerWalkingCount + _profile.PlayerDrivingCount;
            for (int ragdoll = 0; ragdoll < _profile.PlayerRagdollCount; ragdoll++)
            {
                int player = firstPlayer + ragdoll;
                Physics3DBodyState root = Physics.GetBodyState(_playerRepresentativeBodies[player]);
                Vector3 velocityChange = root.LinearVelocityCmPerSecond -
                    _initialPlayerStates[player].LinearVelocityCmPerSecond;
                if (velocityChange.LengthSquared() > 0.01f)
                {
                    _playerRagdollVelocityChanged[ragdoll] = 1;
                }

                Physics3DBodyState poseBody = Physics.GetBodyState(_playerRagdollPoseBodies[ragdoll]);
                float orientationDot = MathF.Abs(Quaternion.Dot(
                    poseBody.Orientation,
                    _initialPlayerRagdollPoseStates[ragdoll].Orientation));
                if (orientationDot < 0.999999f ||
                    poseBody.AngularVelocityRadiansPerSecond.LengthSquared() > 1e-6f)
                {
                    _playerRagdollPoseChanged[ragdoll] = 1;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _ragdolls.Dispose();
            _vehicles.Dispose();
            _ecs.Dispose();
            Physics.Dispose();
            _disposed = true;
        }

        private void CreateOrdinaryBodies(Physics3DShapeId shape)
        {
            for (int i = 0; i < _profile.OrdinaryActiveBodyCount; i++)
            {
                int column = i % 100;
                int row = i / 100;
                Physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    shape,
                    new Vector3(30_000f + column * 250f, 5_000f + (i % 11) * 10f, -12_500f + row * 250f),
                    new Vector3(0f, 0f, 20f),
                    OrdinaryBodyLayer,
                    DynamicMaterial,
                    mass: 1f));
            }
        }

        private void CreateCharacters(Physics3DShapeId characterShape, Physics3DShapeId anchorShape)
        {
            Physics3DBodyId anchor = Physics.CreateBody(CreateBody(
                Physics3DBodyKind.Kinematic,
                anchorShape,
                new Vector3(0f, -30_000f, 0f),
                Vector3.Zero,
                new LayerMask(CharacterCategory, 0u),
                DynamicMaterial));
            Character3DProfile profile = CreateCharacterProfile();
            for (int i = 0; i < _profile.CharacterCount; i++)
            {
                Vector3 position;
                if (i < Math.Min(8, _profile.CharacterCount))
                {
                    position = new Vector3(-8_350f + (i % 4) * 220f, 102f, -6_200f + (i / 4) * 220f);
                }
                else if (i < Math.Min(16, _profile.CharacterCount))
                {
                    int local = i - 8;
                    position = new Vector3(-5_350f + (local % 4) * 220f, 102f, -6_200f + (local / 4) * 220f);
                }
                else
                {
                    int local = i - 16;
                    position = new Vector3(-15_000f + (local % 20) * 180f, 82f, -2_000f + (local / 20) * 180f);
                }

                Physics3DBodyId body = Physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    characterShape,
                    position,
                    Vector3.Zero,
                    CharacterBodyLayer,
                    DynamicMaterial,
                    mass: 80f));
                _characterHandles[i] = _characters.Register(body, anchor, profile);
                if (i < _profile.PlayerWalkingCount)
                {
                    _playerRepresentativeBodies[i] = body;
                    _playerReplicationModes[i] = Physics3DNetReplicationMode.Character;
                }
            }
        }

        private void CreateVehicles(
            Physics3DShapeId chassisShape,
            Physics3DShapeId carrierShape,
            Physics3DShapeId physicalWheelShape,
            Physics3DShapeId boxWheelShape)
        {
            Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[4];
            Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[4];
            for (int vehicleIndex = 0; vehicleIndex < _profile.TotalVehicleCount; vehicleIndex++)
            {
                Vector3 chassisPosition = new(
                    2_000f + (vehicleIndex % 16) * 350f,
                    vehicleIndex < _profile.ScanningVehicleCount ? 80f : 100f,
                    -5_000f + (vehicleIndex / 16) * 450f);
                Physics3DBodyId chassis = Physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    chassisShape,
                    chassisPosition,
                    Vector3.Zero,
                    VehicleBodyLayer,
                    DynamicMaterial,
                    mass: 120f));
                if (vehicleIndex < _profile.ScanningVehicleCount)
                {
                    FillScanningWheels(descriptions, vehicleIndex);
                }
                else
                {
                    bool boxWheel = ((vehicleIndex - _profile.ScanningVehicleCount) & 1) != 0;
                    FillPhysicalWheels(
                        descriptions,
                        chassisPosition,
                        carrierShape,
                        boxWheel ? boxWheelShape : physicalWheelShape,
                        boxWheel ? Vehicle3DWheelKind.Box : Vehicle3DWheelKind.Physical);
                }

                _vehicleIds[vehicleIndex] = _vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
                if (vehicleIndex < _profile.PlayerDrivingCount)
                {
                    int playerSlot = _profile.PlayerWalkingCount + vehicleIndex;
                    _playerRepresentativeBodies[playerSlot] = chassis;
                    _playerReplicationModes[playerSlot] = Physics3DNetReplicationMode.Vehicle;
                }
            }
        }

        private void FillScanningWheels(Span<Vehicle3DWheelDescription> descriptions, int vehicleIndex)
        {
            Vehicle3DWheelQueryKind queryKind = (vehicleIndex & 1) == 0
                ? Vehicle3DWheelQueryKind.Raycast
                : Vehicle3DWheelQueryKind.SphereCast;
            descriptions[0] = CreateScanningWheel(new Vector3(-30f, 0f, -40f), queryKind);
            descriptions[1] = CreateScanningWheel(new Vector3(30f, 0f, -40f), queryKind);
            descriptions[2] = CreateScanningWheel(new Vector3(-30f, 0f, 40f), queryKind);
            descriptions[3] = CreateScanningWheel(new Vector3(30f, 0f, 40f), queryKind);
        }

        private void FillPhysicalWheels(
            Span<Vehicle3DWheelDescription> descriptions,
            Vector3 chassisPosition,
            Physics3DShapeId carrierShape,
            Physics3DShapeId wheelShape,
            Vehicle3DWheelKind kind)
        {
            Span<Vector3> mounts = stackalloc Vector3[4]
            {
                new(-45f, 0f, -55f),
                new(45f, 0f, -55f),
                new(-45f, 0f, 55f),
                new(45f, 0f, 55f)
            };
            for (int wheel = 0; wheel < mounts.Length; wheel++)
            {
                Vector3 bodyPosition = chassisPosition + mounts[wheel] + new Vector3(0f, -60f, 0f);
                Physics3DBodyId carrier = Physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    carrierShape,
                    bodyPosition,
                    Vector3.Zero,
                    VehicleBodyLayer,
                    DynamicMaterial,
                    mass: 20f));
                Physics3DBodyId wheelBody = Physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    wheelShape,
                    bodyPosition,
                    Vector3.Zero,
                    VehicleBodyLayer,
                    DynamicMaterial,
                    mass: 20f));
                descriptions[wheel] = CreatePhysicalWheel(kind, carrier, wheelBody, mounts[wheel]);
            }
        }

        private void CreateRagdolls()
        {
            RagdollRecipeDefinition definition = CreateRagdollRecipe();
            RagdollRecipeId recipe = _ragdolls.RegisterRecipe(definition);
            Span<RagdollBoneHandoff> handoff = stackalloc RagdollBoneHandoff[_profile.BonesPerRagdoll];
            Span<Physics3DBodyId> created = stackalloc Physics3DBodyId[_profile.BonesPerRagdoll];
            Span<Vector3> worldPositions = stackalloc Vector3[_profile.BonesPerRagdoll];
            Span<Quaternion> worldOrientations = stackalloc Quaternion[_profile.BonesPerRagdoll];
            for (int instance = 0; instance < _profile.RagdollCount; instance++)
            {
                Vector3 root = new(12_000f + (instance % 20) * 300f, 180f, -2_000f + (instance / 20) * 400f);
                FillRagdollHandoff(instance, root, definition.Bones, handoff, worldPositions, worldOrientations);
                bool playerDriven = instance < _profile.PlayerRagdollCount;
                _ragdollIds[instance] = _ragdolls.TransitionFromAnimation(
                    recipe,
                    tick: 0,
                    new RagdollActivationDescription(
                        collisionAssemblyId: (uint)(50_000 + instance),
                        totalMass: 80f,
                        RagdollBodyLayer,
                        DynamicMaterial,
                        Physics3DContinuousDetectionMode.Passive,
                        activePoseEnabled: playerDriven),
                    handoff,
                    created);
                if (playerDriven)
                {
                    int playerSlot = _profile.PlayerWalkingCount + _profile.PlayerDrivingCount + instance;
                    _playerRepresentativeBodies[playerSlot] = created[0];
                    _playerRagdollPoseBodies[instance] = created[1];
                    _playerReplicationModes[playerSlot] = Physics3DNetReplicationMode.Ragdoll;
                }
            }
        }

        private void FillRagdollHandoff(
            int instance,
            Vector3 rootPosition,
            RagdollBoneDefinition[] bones,
            Span<RagdollBoneHandoff> handoff,
            Span<Vector3> worldPositions,
            Span<Quaternion> worldOrientations)
        {
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                RagdollBoneDefinition bone = bones[boneIndex];
                if (bone.ParentIndex < 0)
                {
                    worldPositions[boneIndex] = rootPosition;
                    worldOrientations[boneIndex] = bone.LocalOrientation;
                }
                else
                {
                    worldPositions[boneIndex] = worldPositions[bone.ParentIndex] +
                        Vector3.Transform(bone.LocalPositionCm, worldOrientations[bone.ParentIndex]);
                    worldOrientations[boneIndex] = Quaternion.Normalize(
                        Quaternion.Concatenate(bone.LocalOrientation, worldOrientations[bone.ParentIndex]));
                }

                handoff[boneIndex] = new RagdollBoneHandoff(
                    _ecs.Create(new MixedRagdollBoneTag(instance, boneIndex)),
                    worldPositions[boneIndex],
                    worldOrientations[boneIndex],
                    Vector3.Zero,
                    Vector3.Zero);
            }
        }

        private void ConfigureNetworking()
        {
            var config = new Physics3DNetConfig
            {
                AuthoritativeHz = _profile.FixedStepHz,
                SnapshotHz = _profile.SnapshotHz,
                PlayerCapacity = _profile.PlayerCount,
                ClientCapacity = _profile.PlayerCount,
                SnapshotEntityCapacity = _profile.PlayerCount,
                AoiEntityCapacityPerClient = _profile.AoiEntityCountPerClient,
                LocalPredictionHistoryTicks = 16,
                RemoteInterpolationHistoryTicks = 8,
                ReplayEventCapacity = Math.Max(128, _profile.PlayerCount * 4),
                InputHistoryTicksPerPlayer = 16,
                MaxFutureInputTicks = 8
            };
            config.Validate();
            NetworkLifecycle = new Physics3DNetTickLifecycle(config);
            InputRing = new Physics3DNetInputRing(config, NetworkLifecycle);
            SnapshotStore = new Physics3DNetAuthoritativeSnapshotStore(config);
            Aoi = new Physics3DNetAoiDeltaBuilder(config);
            for (int player = 0; player < _profile.PlayerCount; player++)
            {
                InputRing.RegisterPlayer(NetworkPlayerId(player), generation: 1, playerSlot: player);
            }
        }

        private void CaptureInitialPlayerStates()
        {
            for (int player = 0; player < _profile.PlayerCount; player++)
            {
                _initialPlayerStates[player] = Physics.GetBodyState(_playerRepresentativeBodies[player]);
            }

            for (int ragdoll = 0; ragdoll < _profile.PlayerRagdollCount; ragdoll++)
            {
                _initialPlayerRagdollPoseStates[ragdoll] = Physics.GetBodyState(_playerRagdollPoseBodies[ragdoll]);
            }
        }

        private void ConfigureForceField()
        {
            _forceFields.Add(new Physics3DBoxGustField(
                centerCm: new Vector3(33_000f, 0f, -9_000f),
                sizeCm: new Vector3(6_000f, 1_000_000f, 6_000f),
                orientation: Quaternion.Identity,
                baseWindVelocityCmPerSecond: new Vector3(120f, 0f, 0f),
                peakWindVelocityCmPerSecond: new Vector3(600f, 0f, 120f),
                forcePerRelativeSpeed: 0.08f,
                attackTicks: 15,
                holdTicks: 30,
                releaseTicks: 15,
                calmTicks: 30));
        }

        private void ConfigureSupplementalQueries()
        {
            Physics3DQueryFilter filter = new(GroundQueryLayer);
            for (int i = 0; i < _rayQueries.Length; i++)
            {
                Vector3 origin = QueryOrigin(i);
                _rayQueries[i] = new Physics3DRaycastQuery(origin, -Vector3.UnitY, 1_000f, filter);
            }

            for (int i = 0; i < _sphereQueries.Length; i++)
            {
                Vector3 origin = QueryOrigin(i + _rayQueries.Length);
                _sphereQueries[i] = new Physics3DSphereCastQuery(origin, 5f, -Vector3.UnitY, 1_000f, filter);
            }

            for (int i = 0; i < _capsuleQueries.Length; i++)
            {
                Vector3 origin = QueryOrigin(i + _rayQueries.Length + _sphereQueries.Length);
                _capsuleQueries[i] = new Physics3DCapsuleCastQuery(
                    origin,
                    radiusCm: 5f,
                    cylinderLengthCm: 10f,
                    Quaternion.Identity,
                    -Vector3.UnitY,
                    maximumDistanceCm: 1_000f,
                    filter);
            }

            for (int i = 0; i < _boxQueries.Length; i++)
            {
                Vector3 origin = QueryOrigin(i + _rayQueries.Length + _sphereQueries.Length + _capsuleQueries.Length);
                _boxQueries[i] = new Physics3DBoxCastQuery(
                    origin,
                    new Vector3(10f),
                    Quaternion.Identity,
                    -Vector3.UnitY,
                    maximumDistanceCm: 1_000f,
                    filter);
            }
        }

        private void SubmitNetworkInputs(long tick)
        {
            for (int player = 0; player < _profile.PlayerCount; player++)
            {
                short moveX = ((tick + player) % 120) < 60 ? (short)12_000 : (short)-12_000;
                short moveY = (short)(8_000 + (player % 5) * 500);
                Physics3DNetInputArrivalResult arrival = InputRing.Submit(new Physics3DNetInputSubmit(
                    tick,
                    NetworkPlayerId(player),
                    generation: 1,
                    sequence: (uint)tick,
                    new Physics3DNetQuantizedAxes2(moveX, moveY),
                    new Physics3DNetQuantizedAxes2((short)(6_000 + player * 64), 0),
                    buttons: (tick % 90 == 0 && player < _profile.PlayerWalkingCount) ? 1u : 0u));
                if (!arrival.Accepted)
                {
                    throw new InvalidOperationException(
                        $"Authoritative input for player slot {player}, tick {tick} was rejected as {arrival.Disposition}.");
                }
            }
        }

        private void SubmitGameplayInputs(long tick)
        {
            for (int character = 0; character < _characterHandles.Length; character++)
            {
                Vector2 move;
                bool jump;
                if (character < _profile.PlayerWalkingCount)
                {
                    Physics3DNetInputFrameView input = RequirePlayerInput(character, tick);
                    move = DecodeMove(input.MoveAxes);
                    jump = (input.Buttons & 1u) != 0;
                }
                else
                {
                    move = new Vector2(0.15f, 0f);
                    jump = false;
                }

                _characters.SubmitIntent(_characterHandles[character], new Character3DIntent(move, jump));
            }

            for (int vehicle = 0; vehicle < _vehicleIds.Length; vehicle++)
            {
                Vehicle3DInput input;
                if (vehicle < _profile.PlayerDrivingCount)
                {
                    int playerSlot = _profile.PlayerWalkingCount + vehicle;
                    Physics3DNetInputFrameView frame = RequirePlayerInput(playerSlot, tick);
                    Vector2 move = DecodeMove(frame.MoveAxes);
                    input = new Vehicle3DInput(throttle: move.Y, brake: 0f, steering: move.X);
                }
                else
                {
                    input = new Vehicle3DInput(throttle: 0.2f, brake: 0f, steering: 0f);
                }

                _vehicles.SetInput(_vehicleIds[vehicle], input);
            }

            ActivePoseTargetsSubmittedLastTick = 0;
            for (int ragdoll = 0; ragdoll < _profile.PlayerRagdollCount; ragdoll++)
            {
                int playerSlot = _profile.PlayerWalkingCount + _profile.PlayerDrivingCount + ragdoll;
                Physics3DNetInputFrameView frame = RequirePlayerInput(playerSlot, tick);
                float angle = frame.LookAxes.X / (float)short.MaxValue * 0.2f;
                FillRagdollActivePoseTargets(angle);
                _ragdolls.SubmitActivePose(_ragdollIds[ragdoll], tick, _ragdollPoseTargets);
                ActivePoseTargetsSubmittedLastTick += _ragdollPoseTargets.Length;
            }
        }

        private void FillRagdollActivePoseTargets(float angle)
        {
            for (int boneIndex = 0; boneIndex < HumanoidRagdollBones.Length; boneIndex++)
            {
                MixedRagdollBoneSpec bone = HumanoidRagdollBones[boneIndex];
                _ragdollPoseTargets[boneIndex] = bone.ParentIndex < 0
                    ? Quaternion.Identity
                    : Quaternion.CreateFromAxisAngle(bone.ActivePoseAxis, angle * bone.ActivePoseScale);
            }
        }

        private Physics3DNetInputFrameView RequirePlayerInput(int playerSlot, long tick)
        {
            Physics3DNetInputLookupResult result = InputRing.TryGet(playerSlot, tick, out Physics3DNetInputFrameView frame);
            if (result != Physics3DNetInputLookupResult.Present)
            {
                throw new Physics3DNetMissingInputException(tick, 1);
            }

            return frame;
        }

        private void UpdatePlatforms(long tick)
        {
            float phase = tick * 0.035f;
            Vector3 movingPosition = new(-8_000f + MathF.Sin(phase) * 300f, 10f, -6_000f);
            Quaternion movingRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, phase * 0.35f);
            Physics.SetKinematicNextPose(_movingPlatform, movingPosition, movingRotation);
            Physics.SetKinematicNextPose(_conveyorPlatform, new Vector3(-5_000f, 10f, -6_000f), Quaternion.Identity);
        }

        private void ExecuteSupplementalQueries()
        {
            Physics.RaycastClosestBatch(_rayQueries, _rayResults);
            Physics.SphereCastClosestBatch(_sphereQueries, _sphereResults);
            Physics.CapsuleCastClosestBatch(_capsuleQueries, _capsuleResults);
            Physics.BoxCastClosestBatch(_boxQueries, _boxResults);
            int hits = 0;
            for (int i = 0; i < _rayResults.Length; i++)
            {
                hits += _rayResults[i].Hit ? 1 : 0;
            }

            for (int i = 0; i < _sphereResults.Length; i++)
            {
                hits += _sphereResults[i].Hit ? 1 : 0;
            }

            for (int i = 0; i < _capsuleResults.Length; i++)
            {
                hits += _capsuleResults[i].Hit ? 1 : 0;
            }

            for (int i = 0; i < _boxResults.Length; i++)
            {
                hits += _boxResults[i].Hit ? 1 : 0;
            }

            SupplementalQueriesExecutedLastTick =
                _rayQueries.Length + _sphereQueries.Length + _capsuleQueries.Length + _boxQueries.Length;
            SupplementalQueryHitsLastTick = hits;
        }

        private void PublishSnapshotAndAoi(long tick)
        {
            for (int player = 0; player < _profile.PlayerCount; player++)
            {
                Physics3DBodyId body = _playerRepresentativeBodies[player];
                Physics3DBodyState state = Physics.GetBodyState(body);
                _snapshotWrites[player] = new Physics3DNetSnapshotEntityWrite(
                    networkEntityId: player,
                    generation: 1,
                    Physics3DNetReplicationOp.Update,
                    SnapshotBaselineId,
                    state.PositionCm,
                    state.Orientation,
                    state.LinearVelocityCmPerSecond,
                    state.AngularVelocityRadiansPerSecond,
                    Physics.GetBodyKind(body),
                    _playerReplicationModes[player]);
            }

            SnapshotStore.ReplaceAll(tick, SnapshotBaselineId, _snapshotWrites);
            int minimumWrites = int.MaxValue;
            int clientsContainingSelf = 0;
            bool distinctInterestSets = false;
            BaselineMissesLastSnapshot = 0;
            FullSnapshotSendsLastSnapshot = 0;
            BaselineAcknowledgementsLastSnapshot = 0;
            for (int client = 0; client < _profile.PlayerCount; client++)
            {
                BuildAoiInterestForClient(client);
                if (client == 0)
                {
                    _aoiEntityIndices.CopyTo(_firstClientAoiEntityIds, 0);
                }
                else if (!distinctInterestSets)
                {
                    for (int i = 0; i < _aoiEntityIndices.Length; i++)
                    {
                        if (_aoiEntityIndices[i] != _firstClientAoiEntityIds[i])
                        {
                            distinctInterestSets = true;
                            break;
                        }
                    }
                }

                Physics3DNetAoiDeltaBuildResult result = Aoi.BuildDelta(
                    client,
                    tick,
                    SnapshotBaselineId,
                    _aoiInterest,
                    _aoiDelta);
                if (result.RequiresFullSnapshot)
                {
                    BaselineMissesLastSnapshot++;
                    int fullSnapshotCount = SnapshotStore.CopyTo(_fullSnapshotSendBuffer);
                    Physics3DNetSnapshotEntityView ownFullSnapshot = _fullSnapshotSendBuffer[client];
                    if (fullSnapshotCount != _profile.PlayerCount ||
                        ownFullSnapshot.NetworkEntityId != client ||
                        SnapshotStore.SnapshotTick != tick)
                    {
                        throw new InvalidOperationException(
                            $"Client slot {client} could not receive its authoritative full snapshot for tick {tick}.");
                    }

                    FullSnapshotSendsLastSnapshot++;
                    Aoi.AcknowledgeBaseline(client, SnapshotBaselineId);
                    BaselineAcknowledgementsLastSnapshot++;
                    result = Aoi.BuildDelta(
                        client,
                        tick,
                        SnapshotBaselineId,
                        _aoiInterest,
                        _aoiDelta);
                }

                if (result.RequiresFullSnapshot)
                {
                    throw new InvalidOperationException($"Client slot {client} still has no AOI baseline after acknowledgement.");
                }

                minimumWrites = Math.Min(minimumWrites, result.WrittenCount);
                if (Aoi.IsTracked(client, client, out int generation) && generation == 1)
                {
                    clientsContainingSelf++;
                }
            }

            MinimumAoiDeltaWritesLastSnapshot = minimumWrites;
            AoiClientsContainingSelfLastSnapshot = clientsContainingSelf;
            DistinctAoiInterestSetsObservedLastSnapshot = distinctInterestSets;
            NetworkLifecycle.PublishSnapshot(tick);
            SnapshotPublishCount++;
        }

        private void BuildAoiInterestForClient(int client)
        {
            Vector3 center = _snapshotWrites[client].PositionCm;
            int selectedCount = 0;
            for (int entity = 0; entity < _snapshotWrites.Length; entity++)
            {
                if (selectedCount < _aoiEntityIndices.Length)
                {
                    _aoiEntityIndices[selectedCount++] = entity;
                    continue;
                }

                int farthestSlot = 0;
                float farthestDistance = Vector3.DistanceSquared(
                    center,
                    _snapshotWrites[_aoiEntityIndices[0]].PositionCm);
                for (int i = 1; i < _aoiEntityIndices.Length; i++)
                {
                    int selectedEntity = _aoiEntityIndices[i];
                    float distance = Vector3.DistanceSquared(center, _snapshotWrites[selectedEntity].PositionCm);
                    if (distance > farthestDistance ||
                        (distance == farthestDistance && selectedEntity > _aoiEntityIndices[farthestSlot]))
                    {
                        farthestSlot = i;
                        farthestDistance = distance;
                    }
                }

                float candidateDistance = Vector3.DistanceSquared(center, _snapshotWrites[entity].PositionCm);
                if (candidateDistance < farthestDistance ||
                    (candidateDistance == farthestDistance && entity < _aoiEntityIndices[farthestSlot]))
                {
                    _aoiEntityIndices[farthestSlot] = entity;
                }
            }

            if (selectedCount != _aoiEntityIndices.Length)
            {
                throw new InvalidOperationException(
                    $"AOI capacity {_aoiEntityIndices.Length} exceeds the {_snapshotWrites.Length} replicated player entities.");
            }

            _aoiEntityIndices.AsSpan().Sort();
            for (int interestIndex = 0; interestIndex < _aoiEntityIndices.Length; interestIndex++)
            {
                Physics3DNetSnapshotEntityWrite snapshot = _snapshotWrites[_aoiEntityIndices[interestIndex]];
                _aoiInterest[interestIndex] = new Physics3DNetAoiInterest(
                    snapshot.NetworkEntityId,
                    snapshot.Generation,
                    snapshot.PositionCm,
                    snapshot.Orientation,
                    snapshot.LinearVelocityCmPerSecond,
                    snapshot.AngularVelocityRadiansPerSecond,
                    snapshot.BodyKind,
                    snapshot.ReplicationMode);
            }
        }

        private Character3DProfile CreateCharacterProfile()
            => new(
                radiusCm: 30f,
                cylinderLengthCm: 100f,
                maximumGroundSpeedCmPerSecond: 500f,
                maximumGroundAccelerationCmPerSecondSquared: 5_000f,
                maximumAirSpeedCmPerSecond: 400f,
                maximumAirAccelerationCmPerSecondSquared: 1_500f,
                jumpSpeedCmPerSecond: 500f,
                maximumSlopeDegrees: 50f,
                supportProbeDistanceCm: 12f,
                skinWidthCm: 2f,
                maximumStepHeightCm: 40f,
                stepForwardProbeDistanceCm: 45f,
                stepAssistSpeedCmPerSecond: 260f,
                coyoteTicks: 3,
                GroundQueryLayer,
                new Physics3DServoSettings(20f, 0f, 100_000f),
                new Physics3DSpringSettings(30f, 1f));

        private static Vehicle3DWheelDescription CreateScanningWheel(
            Vector3 localMount,
            Vehicle3DWheelQueryKind queryKind)
            => Vehicle3DWheelDescription.Scanning(
                queryKind,
                localMount,
                -Vector3.UnitY,
                Vector3.UnitZ,
                radiusCm: 20f,
                minimumLengthCm: 30f,
                restLengthCm: 60f,
                maximumLengthCm: 90f,
                maximumSteeringAngleRadians: 0.6f,
                suspensionStiffness: 1_000f,
                suspensionDamping: 80f,
                maximumSuspensionForce: 100_000f,
                longitudinalGrip: 100f,
                lateralGrip: 100f,
                maximumDriveForce: 10_000f,
                maximumBrakeForce: 20_000f,
                maximumLateralForce: 20_000f,
                maximumWheelAngularSpeedRadiansPerSecond: 50f,
                steeringScale: 1f,
                driveScale: 1f,
                brakeScale: 1f,
                GroundQueryLayer);

        private static Vehicle3DWheelDescription CreatePhysicalWheel(
            Vehicle3DWheelKind kind,
            Physics3DBodyId carrier,
            Physics3DBodyId wheel,
            Vector3 localMount)
            => Vehicle3DWheelDescription.Physical(
                kind,
                Vehicle3DWheelQueryKind.Raycast,
                carrier,
                wheel,
                localMount,
                -Vector3.UnitY,
                Vector3.UnitZ,
                radiusCm: 20f,
                minimumLengthCm: 30f,
                restLengthCm: 60f,
                maximumLengthCm: 80f,
                maximumSteeringAngleRadians: 0.6f,
                suspensionStiffness: 1_000f,
                suspensionDamping: 80f,
                maximumSuspensionForce: 100_000f,
                longitudinalGrip: 100f,
                lateralGrip: 100f,
                maximumDriveForce: 10_000f,
                maximumBrakeForce: 20_000f,
                maximumLateralForce: 20_000f,
                maximumWheelAngularSpeedRadiansPerSecond: 50f,
                steeringScale: 1f,
                driveScale: 1f,
                brakeScale: 1f,
                GroundQueryLayer,
                CreateWheelJointSettings());

        private static Vehicle3DWheelJointSettings CreateWheelJointSettings()
            => new(
                new Physics3DSpringSettings(30f, 2f),
                new Physics3DSpringSettings(12f, 2f),
                new Physics3DSpringSettings(30f, 2f),
                new Physics3DSpringSettings(20f, 2f),
                new Physics3DSpringSettings(30f, 2f),
                new Physics3DServoSettings(10_000f, 0f, 1_000_000f),
                new Physics3DServoSettings(20f, 0f, 1_000_000f),
                new Physics3DMotorSettings(1_000_000f, 0.001f));

        private static RagdollRecipeDefinition CreateRagdollRecipe()
        {
            var bones = new RagdollBoneDefinition[HumanoidRagdollBones.Length];
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                bones[boneIndex] = CreateRagdollBone(in HumanoidRagdollBones[boneIndex]);
            }

            return new RagdollRecipeDefinition
            {
                StableId = 9001,
                Recovery = new RagdollRecoverySettings(
                    RagdollRecoveryStrategy.PreserveRootYaw,
                    new Vector3(0f, 85f, 0f),
                    450f),
                Bones = bones
            };
        }

        private static RagdollBoneDefinition CreateRagdollBone(in MixedRagdollBoneSpec bone)
            => new()
            {
                StableId = bone.StableId,
                ParentIndex = bone.ParentIndex,
                LocalPositionCm = bone.LocalPositionCm,
                LocalOrientation = Quaternion.Identity,
                Shape = bone.Shape,
                MassRatio = bone.MassRatio,
                ParentAnchorLocalCm = bone.ParentIndex < 0 ? Vector3.Zero : bone.LocalPositionCm * 0.5f,
                BoneAnchorLocalCm = bone.ParentIndex < 0 ? Vector3.Zero : -bone.LocalPositionCm * 0.5f,
                JointFrameLocalParent = Quaternion.Identity,
                JointFrameLocalBone = Quaternion.Identity,
                MaximumSwingAngleRadians = bone.MaximumSwingAngleRadians,
                MinimumTwistAngleRadians = -0.6f,
                MaximumTwistAngleRadians = 0.6f,
                JointSpring = JointSpring,
                CollisionSubgroupIndex = bone.CollisionSubgroup,
                CollidesWithSubgroupsMask = uint.MaxValue,
                ActivePoseServo = new Physics3DServoSettings(
                    ActivePoseServo.MaximumSpeed,
                    ActivePoseServo.BaseSpeed,
                    bone.ActivePoseMaximumForce),
                ActivePoseSpring = ActivePoseSpring
            };

        private static Physics3DBodyDescription CreateBody(
            Physics3DBodyKind kind,
            Physics3DShapeId shape,
            Vector3 position,
            Vector3 velocity,
            in LayerMask layer,
            in Physics3DMaterial material,
            Physics3DBodyContactPolicy contactPolicy = default,
            float mass = 80f)
            => new(
                Entity.Null,
                kind,
                shape,
                position,
                Quaternion.Identity,
                velocity,
                Vector3.Zero,
                kind == Physics3DBodyKind.Dynamic ? mass : 0f,
                layer,
                material,
                Physics3DContinuousDetectionMode.Passive,
                contactPolicy);

        private static Physics3DWorldConfig CreatePhysicsConfig(MixedServerScaleProfile profile)
            => new()
            {
                MobileBodyCapacity = profile.ExpectedMobileBodyCount,
                StaticBodyCapacity = 1,
                ShapeCapacity = profile.ExpectedShapeCount,
                InactiveIslandCapacity = profile.ExpectedMobileBodyCount,
                ConstraintCapacity = profile.ExpectedConstraintCount,
                ConstraintsPerTypeBatchCapacity = profile.ExpectedConstraintCount,
                ConstraintCountPerBodyEstimate = 16,
                ContactPairCapacityPerWorker = Math.Max(256, profile.ExpectedMobileBodyCount * 4),
                ActuationCommandCapacity = profile.RequiredActuationCommandCapacity,
                WorkerCount = profile.WorkerCount,
                ThreadMemoryPoolBlockAllocationSize = 16_384,
                MemoryPoolExpectedPooledResourceCount = 256,
                FixedStepHz = profile.FixedStepHz,
                MaximumPhysicsStepsPerSourceTick = 1,
                SolverSubstepCount = 1,
                SolverVelocityIterationCount = 8,
                GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
                LinearDamping = 0f,
                AngularDamping = 0f,
                MaximumSpeculativeMarginCm = 10f,
                SleepThreshold = 0f,
                MinimumTimestepCountUnderSleepThreshold = byte.MaxValue,
                ContinuousMinimumSweepTimestep = 0.001f,
                ContinuousSweepConvergenceThreshold = 0.001f,
                MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
            };

        private static Vector2 DecodeMove(in Physics3DNetQuantizedAxes2 axes)
            => new(axes.X / (float)short.MaxValue, axes.Y / (float)short.MaxValue);

        private static int NetworkPlayerId(int playerSlot) => 100_000 + playerSlot;

        private static Vector3 QueryOrigin(int index)
            => new(-20_000f + (index % 50) * 800f, 500f, -20_000f + (index / 50) * 800f);

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private static bool IsFinite(Quaternion value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
    }

    private readonly struct MixedServerTickAllocation
    {
        public MixedServerTickAllocation(
            long inputSubmissionBytes,
            long executeGateBytes,
            long gameplayPreparationBytes,
            long physicsStepBytes,
            long observationAndCommitBytes,
            long snapshotPublishBytes)
        {
            InputSubmissionBytes = inputSubmissionBytes;
            ExecuteGateBytes = executeGateBytes;
            GameplayPreparationBytes = gameplayPreparationBytes;
            PhysicsStepBytes = physicsStepBytes;
            ObservationAndCommitBytes = observationAndCommitBytes;
            SnapshotPublishBytes = snapshotPublishBytes;
        }

        public long InputSubmissionBytes { get; }
        public long ExecuteGateBytes { get; }
        public long GameplayPreparationBytes { get; }
        public long PhysicsStepBytes { get; }
        public long ObservationAndCommitBytes { get; }
        public long SnapshotPublishBytes { get; }
    }

    private sealed class MixedServerScaleProfile
    {
        private const int SharedKinematicBodyCount = 4;
        private const int BaseSceneShapeCount = 8;
        private const int ConstraintsPerCharacter = 1;
        private const int ConstraintsPerPhysicalWheel = 7;
        private const int PassiveConstraintsPerRagdollJoint = 3;
        private const int MaximumCommandsPerVehicleWheel = 2;

        public static MixedServerScaleProfile Correctness { get; } = new(
            playerWalkingCount: 2,
            playerDrivingCount: 2,
            playerRagdollCount: 2,
            characterCount: 6,
            scanningVehicleCount: 2,
            physicalVehicleCount: 1,
            ragdollCount: 3,
            ordinaryActiveBodyCount: 64,
            estimatedCombinedQueryWorkloadPerTick: 32,
            aoiEntityCountPerClient: 4,
            workerCount: 2,
            warmupTickCount: 0,
            sampleTickCount: 0,
            correctnessTickCount: 3);

        public static MixedServerScaleProfile FullGate { get; } = new(
            playerWalkingCount: 50,
            playerDrivingCount: 64,
            playerRagdollCount: 36,
            characterCount: 150,
            scanningVehicleCount: 64,
            physicalVehicleCount: 32,
            ragdollCount: 100,
            ordinaryActiveBodyCount: 10_000,
            estimatedCombinedQueryWorkloadPerTick: 2_400,
            aoiEntityCountPerClient: 32,
            workerCount: 8,
            warmupTickCount: 120,
            sampleTickCount: 600,
            correctnessTickCount: 0);

        private MixedServerScaleProfile(
            int playerWalkingCount,
            int playerDrivingCount,
            int playerRagdollCount,
            int characterCount,
            int scanningVehicleCount,
            int physicalVehicleCount,
            int ragdollCount,
            int ordinaryActiveBodyCount,
            int estimatedCombinedQueryWorkloadPerTick,
            int aoiEntityCountPerClient,
            int workerCount,
            int warmupTickCount,
            int sampleTickCount,
            int correctnessTickCount)
        {
            PlayerWalkingCount = playerWalkingCount;
            PlayerDrivingCount = playerDrivingCount;
            PlayerRagdollCount = playerRagdollCount;
            CharacterCount = characterCount;
            ScanningVehicleCount = scanningVehicleCount;
            PhysicalVehicleCount = physicalVehicleCount;
            RagdollCount = ragdollCount;
            OrdinaryActiveBodyCount = ordinaryActiveBodyCount;
            EstimatedCombinedQueryWorkloadPerTick = estimatedCombinedQueryWorkloadPerTick;
            AoiEntityCountPerClient = aoiEntityCountPerClient;
            WorkerCount = workerCount;
            WarmupTickCount = warmupTickCount;
            SampleTickCount = sampleTickCount;
            CorrectnessTickCount = correctnessTickCount;

            if (PlayerDrivingCount > ScanningVehicleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(playerDrivingCount), "Player-driven vehicles must be present in the scanning vehicle lane.");
            }

            if (PlayerRagdollCount > RagdollCount)
            {
                throw new ArgumentOutOfRangeException(nameof(playerRagdollCount));
            }

            if (PlayerWalkingCount > CharacterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(playerWalkingCount));
            }

            if (BonesPerRagdoll is < 12 or > 20)
            {
                throw new InvalidOperationException(
                    $"The mixed server gate requires a 12-20 bone humanoid, but the authored recipe has {BonesPerRagdoll} bones.");
            }

            if (SupplementalQueryCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(estimatedCombinedQueryWorkloadPerTick),
                    "Estimated combined query workload does not cover the module-owned query estimate.");
            }
        }

        public int FixedStepHz => 30;
        public int SnapshotHz => 10;
        public int SnapshotIntervalTicks => FixedStepHz / SnapshotHz;
        public int WheelsPerVehicle => 4;
        public int BonesPerRagdoll => HumanoidRagdollBones.Length;
        public int JointsPerRagdoll => BonesPerRagdoll - 1;
        public int PlayerWalkingCount { get; }
        public int PlayerDrivingCount { get; }
        public int PlayerRagdollCount { get; }
        public int PlayerCount => PlayerWalkingCount + PlayerDrivingCount + PlayerRagdollCount;
        public int CharacterCount { get; }
        public int ScanningVehicleCount { get; }
        public int PhysicalVehicleCount { get; }
        public int TotalVehicleCount => ScanningVehicleCount + PhysicalVehicleCount;
        public int RagdollCount { get; }
        public int OrdinaryActiveBodyCount { get; }
        public int EstimatedCombinedQueryWorkloadPerTick { get; }
        public int EstimatedModuleQueryCount => CharacterCount + TotalVehicleCount * WheelsPerVehicle;
        public int SupplementalQueryCount => EstimatedCombinedQueryWorkloadPerTick - EstimatedModuleQueryCount;
        public int AoiEntityCountPerClient { get; }
        public int WorkerCount { get; }
        public int WarmupTickCount { get; }
        public int SampleTickCount { get; }
        public int CorrectnessTickCount { get; }
        public double FixedTickBudgetMilliseconds => 1_000d / FixedStepHz;
        public int ExpectedSnapshotPublishCount(long committedTick) => checked((int)(committedTick / SnapshotIntervalTicks));
        public long ExpectedLastSnapshotTick(long committedTick) =>
            committedTick - (committedTick % SnapshotIntervalTicks);
        public int ExpectedRagdollBodyCount => RagdollCount * BonesPerRagdoll;
        public int ExpectedDynamicBodyCount =>
            OrdinaryActiveBodyCount +
            CharacterCount +
            TotalVehicleCount +
            PhysicalVehicleCount * WheelsPerVehicle * 2 +
            ExpectedRagdollBodyCount;
        public int ExpectedMobileBodyCount =>
            ExpectedDynamicBodyCount +
            SharedKinematicBodyCount;
        public int ExpectedShapeCount => BaseSceneShapeCount + HumanoidRagdollUniqueShapeCount;
        public int ExpectedActivePoseTargetsPerTick => PlayerRagdollCount * BonesPerRagdoll;
        public int ExpectedConstraintCount =>
            CharacterCount * ConstraintsPerCharacter +
            PhysicalVehicleCount * WheelsPerVehicle * ConstraintsPerPhysicalWheel +
            RagdollCount * JointsPerRagdoll * PassiveConstraintsPerRagdollJoint +
            PlayerRagdollCount * JointsPerRagdoll;
        public int RequiredActuationCommandCapacity =>
            ExpectedDynamicBodyCount + // the configured gust can enqueue one force per dynamic body
            CharacterCount +
            TotalVehicleCount * WheelsPerVehicle * MaximumCommandsPerVehicleWheel;
    }

    private sealed class MixedServerScaleReport
    {
        public MixedServerScaleReport(int sampleCount)
        {
            FullTick = new DurationSamples("full authoritative tick", sampleCount, timestampTicks: true);
            PhysicsTotal = new DurationSamples("physics total", sampleCount);
            CommandReplay = new DurationSamples("physics command replay", sampleCount);
            Sleep = new DurationSamples("physics sleep", sampleCount);
            PredictBounds = new DurationSamples("physics predict bounds", sampleCount);
            CollisionDetection = new DurationSamples("physics collision detection", sampleCount);
            ContactSurface = new DurationSamples("physics contact surface", sampleCount);
            Solve = new DurationSamples("physics solve", sampleCount);
            Optimize = new DurationSamples("physics optimize", sampleCount);
            ContactFinalize = new DurationSamples("physics contact finalize", sampleCount);
            InputSubmissionAllocations = new AllocationStageSamples("input submission");
            ExecuteGateAllocations = new AllocationStageSamples("input cutoff / execute gate");
            GameplayPreparationAllocations = new AllocationStageSamples("gameplay preparation and queries");
            PhysicsStepAllocations = new AllocationStageSamples("physics step calling thread");
            ObservationAndCommitAllocations = new AllocationStageSamples("module observation and network commit");
            SnapshotPublishAllocations = new AllocationStageSamples("snapshot and per-client AOI publish");
            PhysicsWorkerAllocations = new AllocationStageSamples("physics worker aggregate");
            CommandReplayCallingAllocations = new AllocationStageSamples("physics command replay calling thread");
            SleepCallingAllocations = new AllocationStageSamples("physics sleep calling thread");
            SleepWorkerAllocations = new AllocationStageSamples("physics sleep workers");
            PredictBoundsCallingAllocations = new AllocationStageSamples("physics predict bounds calling thread");
            PredictBoundsWorkerAllocations = new AllocationStageSamples("physics predict bounds workers");
            CollisionDetectionCallingAllocations = new AllocationStageSamples("physics collision detection calling thread");
            CollisionDetectionWorkerAllocations = new AllocationStageSamples("physics collision detection workers");
            ContactSurfaceCallingAllocations = new AllocationStageSamples("physics contact surface calling thread");
            SolveCallingAllocations = new AllocationStageSamples("physics solve calling thread");
            SolveWorkerAllocations = new AllocationStageSamples("physics solve workers");
            OptimizeCallingAllocations = new AllocationStageSamples("physics optimize calling thread");
            OptimizeWorkerAllocations = new AllocationStageSamples("physics optimize workers");
            ContactFinalizeCallingAllocations = new AllocationStageSamples("physics contact finalize calling thread");
        }

        public DurationSamples FullTick { get; }
        public DurationSamples PhysicsTotal { get; }
        public DurationSamples CommandReplay { get; }
        public DurationSamples Sleep { get; }
        public DurationSamples PredictBounds { get; }
        public DurationSamples CollisionDetection { get; }
        public DurationSamples ContactSurface { get; }
        public DurationSamples Solve { get; }
        public DurationSamples Optimize { get; }
        public DurationSamples ContactFinalize { get; }
        public long PhysicsCallingThreadAllocatedBytes { get; private set; }
        public long PhysicsWorkerAllocatedBytes { get; private set; }
        public AllocationStageSamples InputSubmissionAllocations { get; }
        public AllocationStageSamples ExecuteGateAllocations { get; }
        public AllocationStageSamples GameplayPreparationAllocations { get; }
        public AllocationStageSamples PhysicsStepAllocations { get; }
        public AllocationStageSamples ObservationAndCommitAllocations { get; }
        public AllocationStageSamples SnapshotPublishAllocations { get; }
        public AllocationStageSamples PhysicsWorkerAllocations { get; }
        public AllocationStageSamples CommandReplayCallingAllocations { get; }
        public AllocationStageSamples SleepCallingAllocations { get; }
        public AllocationStageSamples SleepWorkerAllocations { get; }
        public AllocationStageSamples PredictBoundsCallingAllocations { get; }
        public AllocationStageSamples PredictBoundsWorkerAllocations { get; }
        public AllocationStageSamples CollisionDetectionCallingAllocations { get; }
        public AllocationStageSamples CollisionDetectionWorkerAllocations { get; }
        public AllocationStageSamples ContactSurfaceCallingAllocations { get; }
        public AllocationStageSamples SolveCallingAllocations { get; }
        public AllocationStageSamples SolveWorkerAllocations { get; }
        public AllocationStageSamples OptimizeCallingAllocations { get; }
        public AllocationStageSamples OptimizeWorkerAllocations { get; }
        public AllocationStageSamples ContactFinalizeCallingAllocations { get; }

        public void Record(
            int sample,
            long fullTickTimestampTicks,
            in Physics3DStepMetrics metrics,
            in MixedServerTickAllocation allocation)
        {
            FullTick.RecordTimestampTicks(sample, fullTickTimestampTicks);
            PhysicsTotal.RecordMilliseconds(sample, metrics.Total.ElapsedMilliseconds);
            CommandReplay.RecordMilliseconds(sample, metrics.CommandReplay.ElapsedMilliseconds);
            Sleep.RecordMilliseconds(sample, metrics.Sleep.ElapsedMilliseconds);
            PredictBounds.RecordMilliseconds(sample, metrics.PredictBounds.ElapsedMilliseconds);
            CollisionDetection.RecordMilliseconds(sample, metrics.CollisionDetection.ElapsedMilliseconds);
            ContactSurface.RecordMilliseconds(sample, metrics.ContactSurface.ElapsedMilliseconds);
            Solve.RecordMilliseconds(sample, metrics.Solve.ElapsedMilliseconds);
            Optimize.RecordMilliseconds(sample, metrics.Optimize.ElapsedMilliseconds);
            ContactFinalize.RecordMilliseconds(sample, metrics.ContactFinalize.ElapsedMilliseconds);
            PhysicsCallingThreadAllocatedBytes += metrics.Total.CallingThreadAllocatedBytes;
            PhysicsWorkerAllocatedBytes += metrics.Total.BackgroundWorkerAllocatedBytes;
            InputSubmissionAllocations.Record(sample, allocation.InputSubmissionBytes);
            ExecuteGateAllocations.Record(sample, allocation.ExecuteGateBytes);
            GameplayPreparationAllocations.Record(sample, allocation.GameplayPreparationBytes);
            PhysicsStepAllocations.Record(sample, allocation.PhysicsStepBytes);
            ObservationAndCommitAllocations.Record(sample, allocation.ObservationAndCommitBytes);
            SnapshotPublishAllocations.Record(sample, allocation.SnapshotPublishBytes);
            PhysicsWorkerAllocations.Record(sample, metrics.Total.BackgroundWorkerAllocatedBytes);
            CommandReplayCallingAllocations.Record(sample, metrics.CommandReplay.CallingThreadAllocatedBytes);
            SleepCallingAllocations.Record(sample, metrics.Sleep.CallingThreadAllocatedBytes);
            SleepWorkerAllocations.Record(sample, metrics.Sleep.BackgroundWorkerAllocatedBytes);
            PredictBoundsCallingAllocations.Record(sample, metrics.PredictBounds.CallingThreadAllocatedBytes);
            PredictBoundsWorkerAllocations.Record(sample, metrics.PredictBounds.BackgroundWorkerAllocatedBytes);
            CollisionDetectionCallingAllocations.Record(sample, metrics.CollisionDetection.CallingThreadAllocatedBytes);
            CollisionDetectionWorkerAllocations.Record(sample, metrics.CollisionDetection.BackgroundWorkerAllocatedBytes);
            ContactSurfaceCallingAllocations.Record(sample, metrics.ContactSurface.CallingThreadAllocatedBytes);
            SolveCallingAllocations.Record(sample, metrics.Solve.CallingThreadAllocatedBytes);
            SolveWorkerAllocations.Record(sample, metrics.Solve.BackgroundWorkerAllocatedBytes);
            OptimizeCallingAllocations.Record(sample, metrics.Optimize.CallingThreadAllocatedBytes);
            OptimizeWorkerAllocations.Record(sample, metrics.Optimize.BackgroundWorkerAllocatedBytes);
            ContactFinalizeCallingAllocations.Record(sample, metrics.ContactFinalize.CallingThreadAllocatedBytes);
        }

        public void Write(
            MixedServerScaleProfile profile,
            long fullTickCallingThreadAllocatedBytes,
            int minimumAwakeBodies,
            int peakContacts,
            int minimumConstraints,
            int minimumQueryHits,
            int activeMobileBodies)
        {
            TestContext.Out.WriteLine(
                $"Mixed server data-plane gate: players={profile.PlayerCount} " +
                $"(walking={profile.PlayerWalkingCount}, driving={profile.PlayerDrivingCount}, ragdoll={profile.PlayerRagdollCount}); " +
                $"module load [characters={profile.CharacterCount}, scanning vehicles={profile.ScanningVehicleCount}, " +
                $"physical/box vehicles={profile.PhysicalVehicleCount}, ragdolls={profile.RagdollCount} x {profile.BonesPerRagdoll} bones " +
                $"({profile.ExpectedRagdollBodyCount} bodies), registered shapes={profile.ExpectedShapeCount}, " +
                $"isolated integration bodies={profile.OrdinaryActiveBodyCount}, " +
                $"observed supplemental queries/tick={profile.SupplementalQueryCount}, " +
                $"estimated module queries/tick={profile.EstimatedModuleQueryCount} (module API exposes outcomes, not a counter)]; " +
                $"observed [mobile={activeMobileBodies}, minimum awake={minimumAwakeBodies}, " +
                $"minimum constraints={minimumConstraints}, peak contacts={peakContacts}, minimum supplemental query hits={minimumQueryHits}]; " +
                $"allocations [full calling thread={fullTickCallingThreadAllocatedBytes}, " +
                $"physics calling={PhysicsCallingThreadAllocatedBytes}, physics workers={PhysicsWorkerAllocatedBytes}].");
            TestContext.Out.WriteLine(
                "Networking scope: authoritative input ring, execute/commit lifecycle, real physics-derived snapshots and per-client AOI deltas; " +
                "no socket transport, packet codec, or full-world rollback is claimed by this gate.");
            TestContext.Out.WriteLine(
                "Load scope: the 10K ordinary rigid bodies are an isolated integration load with collision disabled; " +
                "peak contacts describe only the mixed gameplay subset and must not be read as 10K-body contact density.");
            TestContext.Out.WriteLine(
                $"Timing gate: 30Hz full-tick P95/P99 <= {profile.FixedTickBudgetMilliseconds:F3}ms; maximum is diagnostic only.");
            TestContext.Out.WriteLine("Calling-thread allocation samples by authoritative stage:");
            InputSubmissionAllocations.Write();
            ExecuteGateAllocations.Write();
            GameplayPreparationAllocations.Write();
            PhysicsStepAllocations.Write();
            ObservationAndCommitAllocations.Write();
            SnapshotPublishAllocations.Write();
            PhysicsWorkerAllocations.Write();
            CommandReplayCallingAllocations.Write();
            SleepCallingAllocations.Write();
            SleepWorkerAllocations.Write();
            PredictBoundsCallingAllocations.Write();
            PredictBoundsWorkerAllocations.Write();
            CollisionDetectionCallingAllocations.Write();
            CollisionDetectionWorkerAllocations.Write();
            ContactSurfaceCallingAllocations.Write();
            SolveCallingAllocations.Write();
            SolveWorkerAllocations.Write();
            OptimizeCallingAllocations.Write();
            OptimizeWorkerAllocations.Write();
            ContactFinalizeCallingAllocations.Write();
            FullTick.Write();
            PhysicsTotal.Write();
            CommandReplay.Write();
            Sleep.Write();
            PredictBounds.Write();
            CollisionDetection.Write();
            ContactSurface.Write();
            Solve.Write();
            Optimize.Write();
            ContactFinalize.Write();
        }
    }

    private sealed class AllocationStageSamples
    {
        private readonly string _name;

        public AllocationStageSamples(string name)
        {
            _name = name;
        }

        public long TotalBytes { get; private set; }
        public long MaximumSampleBytes { get; private set; }
        public int FirstNonZeroSample { get; private set; } = -1;

        public void Record(int sample, long bytes)
        {
            TotalBytes += bytes;
            MaximumSampleBytes = Math.Max(MaximumSampleBytes, bytes);
            if (bytes != 0 && FirstNonZeroSample < 0)
            {
                FirstNonZeroSample = sample;
            }
        }

        public void Write()
        {
            TestContext.Out.WriteLine(
                $"  {_name}: total={TotalBytes}B, max/sample={MaximumSampleBytes}B, " +
                $"first non-zero sample={(FirstNonZeroSample < 0 ? "none" : FirstNonZeroSample)}");
        }
    }

    private sealed class DurationSamples
    {
        private readonly string _name;
        private readonly double[] _milliseconds;
        private bool _sorted;

        public DurationSamples(string name, int sampleCount, bool timestampTicks = false)
        {
            _name = name;
            _milliseconds = new double[sampleCount];
            _ = timestampTicks;
        }

        public double Maximum
        {
            get
            {
                EnsureSorted();
                return _milliseconds[^1];
            }
        }

        public void RecordTimestampTicks(int sample, long ticks)
        {
            _milliseconds[sample] = ticks * (1_000d / Stopwatch.Frequency);
            _sorted = false;
        }

        public void RecordMilliseconds(int sample, double milliseconds)
        {
            _milliseconds[sample] = milliseconds;
            _sorted = false;
        }

        public double Percentile(double percentile)
        {
            EnsureSorted();
            int index = Math.Clamp((int)Math.Ceiling(_milliseconds.Length * percentile) - 1, 0, _milliseconds.Length - 1);
            return _milliseconds[index];
        }

        public void Write()
        {
            TestContext.Out.WriteLine(
                $"  {_name}: P50={Percentile(0.50):F3}ms, P95={Percentile(0.95):F3}ms, " +
                $"P99={Percentile(0.99):F3}ms, max={Maximum:F3}ms");
        }

        private void EnsureSorted()
        {
            if (_sorted)
            {
                return;
            }

            _milliseconds.AsSpan().Sort();
            _sorted = true;
        }
    }

    private readonly record struct MixedRagdollBoneTag(int Instance, int Bone);

    private readonly record struct MixedRagdollBoneSpec(
        int StableId,
        int ParentIndex,
        Vector3 LocalPositionCm,
        RagdollShapeDefinition Shape,
        float MassRatio,
        int CollisionSubgroup,
        float MaximumSwingAngleRadians,
        float ActivePoseMaximumForce,
        Vector3 ActivePoseAxis,
        float ActivePoseScale);
}
