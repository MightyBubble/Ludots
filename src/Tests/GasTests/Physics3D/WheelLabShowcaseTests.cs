using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Physics3D;
using Ludots.Core.Vehicle3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class WheelLabShowcaseTests
{
    [Test]
    public void Feature_WheelLab_Scenario_PlayerSeesACompleteStrictlyConfiguredDrivingCourse()
    {
        // Given the authored Wheel Lab configuration is loaded through the strict production parser.
        JsonObject json = LoadOfficialConfigJson();

        // When an unknown Wheel Lab field is introduced.
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        JsonObject unknown = (JsonObject)json.DeepClone();
        unknown["wheelLab"]!.AsObject()["silentWheelFallback"] = true;

        // Then the real course is accepted and the invented fallback is rejected.
        Assert.Multiple(() =>
        {
            Assert.That(config.WheelLab.InitialWheelKind, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(config.WheelLab.ScanningQueryKind, Is.EqualTo(Vehicle3DWheelQueryKind.SphereCast));
            Assert.That(config.WheelLab.BumpCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(config.WheelLab.PotholeDepthCm, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.BankAngleDegrees, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.RampAngleDegrees, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.BrakeEndZCm, Is.GreaterThan(config.WheelLab.BrakeStartZCm));
            Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(unknown));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerSwitchesThreeWheelTypesWithoutReplacingTheChassis()
    {
        // Given one physical-wheel car has settled at the start of the course.
        using var harness = new WheelLabHarness();
        for (int i = 0; i < 8; i++)
        {
            harness.Step();
        }

        Physics3DBodyId chassis = harness.Runtime.WheelLabChassisBody;
        Physics3DBodyState beforeBox = harness.Runtime.GetWheelLabChassisState();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(harness.Runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(harness.Runtime.WheelLabWheelCountValue, Is.EqualTo(4));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(8));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(28));
            Assert.That(harness.Runtime.WheelLabGroundedWheelCount, Is.EqualTo(4));
        });

        // When the player selects Box Wheels at a fixed-step boundary.
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Box);
        Physics3DBodyState afterBoxPrepare = harness.Runtime.GetWheelLabChassisState();

        // Then the chassis identity and exact state remain, while the eight wheel bodies are rebuilt.
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabChassisBody, Is.EqualTo(chassis));
            AssertBodyState(afterBoxPrepare, beforeBox);
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Box));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(8));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(28));
        });
        harness.CompletePreparedStep();

        // When the player selects Scanning Wheels.
        Physics3DBodyState beforeScanning = harness.Runtime.GetWheelLabChassisState();
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Scanning);
        Physics3DBodyState afterScanningPrepare = harness.Runtime.GetWheelLabChassisState();

        // Then the same chassis now rides on four batched sphere casts with no private wheel bodies.
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabChassisBody, Is.EqualTo(chassis));
            AssertBodyState(afterScanningPrepare, beforeScanning);
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Scanning));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.Zero);
            Assert.That(harness.Physics.ActiveConstraintCount, Is.Zero);
            Assert.That(harness.Runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(harness.Runtime.WheelLabWheelCountValue, Is.EqualTo(4));
        });
        harness.CompletePreparedStep();

        // When the player cycles once more, Then the same chassis receives physical wheels again.
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Physical);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabChassisBody, Is.EqualTo(chassis));
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(8));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(28));
        });
        harness.CompletePreparedStep();
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerDrivesBrakesAndReadsWheelContactEvidence()
    {
        // Given the player starts on physical wheels, whose ray contacts expose stable surface normals.
        using var harness = new WheelLabHarness();
        Vector3 start = harness.Runtime.GetWheelLabChassisState().PositionCm;

        // When the player holds throttle through the first part of the course.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < 120; i++)
        {
            harness.Step();
        }

        Physics3DBodyState driven = harness.Runtime.GetWheelLabChassisState();
        float speedBeforeBrake = harness.Runtime.WheelLabSpeedKph;

        // And the player releases throttle and holds the brake.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        for (int i = 0; i < 45; i++)
        {
            harness.Step();
        }

        // Then the chassis moved forward, braking reduced speed, and all debug evidence is physics-derived.
        Assert.Multiple(() =>
        {
            Assert.That(driven.PositionCm.Z, Is.GreaterThan(start.Z + 100f));
            Assert.That(speedBeforeBrake, Is.GreaterThan(1f));
            Assert.That(harness.Runtime.WheelLabSpeedKph, Is.LessThan(speedBeforeBrake));
            Assert.That(harness.Runtime.WheelLabGroundedWheelCount, Is.GreaterThan(0));
            Assert.That(harness.Runtime.CreateWheelLabSummary(), Does.Contain("Physical Wheels"));
        });

        int groundedVisuals = 0;
        int groundedNormals = 0;
        for (int i = 0; i < 4; i++)
        {
            Assert.That(harness.Runtime.TryGetWheelLabDebugVisual(i, out Physics3DWheelLabDebugVisual visual), Is.True);
            if (visual.Grounded)
            {
                groundedVisuals++;
                Assert.That(float.IsFinite(visual.CompressionCm), Is.True);
                float normalLengthSquared = visual.ContactNormal.LengthSquared();
                if (normalLengthSquared > 0f)
                {
                    groundedNormals++;
                    Assert.That(normalLengthSquared, Is.GreaterThan(0.9f));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(groundedVisuals, Is.GreaterThan(0));
            Assert.That(groundedNormals, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_WarmedFourWheelFixedStepAllocatesZeroBytes()
    {
        // Given a scanning-wheel vehicle has warmed the exact 30Hz showcase path.
        using var harness = new WheelLabHarness();
        harness.SwitchMode(Vehicle3DWheelKind.Scanning);
        for (int i = 0; i < 64; i++)
        {
            harness.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        // When 64 more authoritative fixed steps run.
        for (int i = 0; i < 64; i++)
        {
            harness.Step();
        }

        // Then the calling thread allocates no managed memory.
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed Wheel Lab fixed-step path allocated {allocated} bytes.");
    }

    private static void AssertBodyState(in Physics3DBodyState actual, in Physics3DBodyState expected)
    {
        Physics3DBodyState actualValue = actual;
        Physics3DBodyState expectedValue = expected;
        Assert.Multiple(() =>
        {
            Assert.That(actualValue.PositionCm, Is.EqualTo(expectedValue.PositionCm));
            Assert.That(actualValue.Orientation, Is.EqualTo(expectedValue.Orientation));
            Assert.That(actualValue.LinearVelocityCmPerSecond, Is.EqualTo(expectedValue.LinearVelocityCmPerSecond));
            Assert.That(actualValue.AngularVelocityRadiansPerSecond, Is.EqualTo(expectedValue.AngularVelocityRadiansPerSecond));
            Assert.That(actualValue.Awake, Is.EqualTo(expectedValue.Awake));
        });
    }

    private static JsonObject LoadOfficialConfigJson()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "mods")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private sealed class WheelLabHarness : IDisposable
    {
        public WheelLabHarness()
        {
            Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(LoadOfficialConfigJson());
            config.InitialScene = Physics3DShowcaseScene.WheelLab;
            config.MaximumBodies = 256;
            config.VisibleBodyLimit = 256;
            Ecs = World.Create();
            Physics = new Physics3DWorld(CreateWorldConfig());
            Simulation = new Physics3DSimulationSystem(Ecs, Physics, sourceFixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1);
            Runtime = new Physics3DShowcaseRuntime();
            Runtime.ActivateForTests(Ecs, Physics, Simulation, config);
        }

        public World Ecs { get; }
        public Physics3DWorld Physics { get; }
        public Physics3DSimulationSystem Simulation { get; }
        public Physics3DShowcaseRuntime Runtime { get; }

        public void SwitchMode(Vehicle3DWheelKind mode)
        {
            PrepareModeSwitch(mode);
            CompletePreparedStep();
        }

        public void PrepareModeSwitch(Vehicle3DWheelKind mode)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetWheelMode,
                (int)mode));
            Runtime.PrepareFixedStep();
        }

        public void CompletePreparedStep()
        {
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Step()
        {
            Runtime.PrepareFixedStep();
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Dispose()
        {
            Runtime.Dispose();
            Physics.Dispose();
            Ecs.Dispose();
        }

        private static Physics3DWorldConfig CreateWorldConfig()
        {
            return new Physics3DWorldConfig
            {
                MobileBodyCapacity = 128,
                StaticBodyCapacity = 128,
                ShapeCapacity = 256,
                InactiveIslandCapacity = 128,
                ConstraintCapacity = 512,
                ConstraintsPerTypeBatchCapacity = 512,
                ConstraintCountPerBodyEstimate = 16,
                ContactPairCapacityPerWorker = 4_096,
                ActuationCommandCapacity = 256,
                WorkerCount = 1,
                FixedStepHz = 30,
                MaximumPhysicsStepsPerSourceTick = 1,
                SolverSubstepCount = 1,
                SolverVelocityIterationCount = 8,
                GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
                LinearDamping = 0f,
                AngularDamping = 0.03f,
                MaximumSpeculativeMarginCm = 10f,
                SleepThreshold = 0.01f,
                MinimumTimestepCountUnderSleepThreshold = 255,
                ContinuousMinimumSweepTimestep = 0.001f,
                ContinuousSweepConvergenceThreshold = 0.001f,
                MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
            };
        }
    }
}
