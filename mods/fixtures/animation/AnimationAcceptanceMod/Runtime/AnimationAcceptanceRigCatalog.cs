using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace AnimationAcceptanceMod.Runtime
{
    internal enum AnimationAcceptanceRigId : byte
    {
        Tank = 0,
        Humanoid = 1,
    }

    internal sealed record AnimationAcceptanceParameterDefinition(string Label, int Index, string Description);

    internal sealed record AnimationAcceptanceExampleProfile(
        string Id,
        string Label,
        string Summary,
        float Speed,
        bool MoveEnabled,
        float FacingYawRad,
        float AimYawRad,
        float OverlayWeight01,
        float LowerBodyPhase01,
        float OverlayNormalizedTime01,
        bool FireImmediately);

    internal sealed class AnimationAcceptanceRigDefinition
    {
        public AnimationAcceptanceRigDefinition(
            AnimationAcceptanceRigId rigId,
            string displayName,
            string summary,
            string layerSummary,
            string controllerKey,
            AnimatorAuxLayerMode layerMode,
            Vector2 manualAnchorCm,
            int speedParameterIndex,
            int locomotionBoolParameterIndex,
            int fireTriggerParameterIndex,
            int idleOverlayStateIndex,
            int fireOverlayStateIndex,
            float fireOverlayDurationSeconds,
            string[] stateLabels,
            string[] stateDescriptions,
            string[] overlayStateLabels,
            string[] transitionDescriptions,
            AnimationAcceptanceParameterDefinition[] floatParameters,
            AnimationAcceptanceParameterDefinition[] boolParameters,
            AnimationAcceptanceParameterDefinition[] triggerParameters,
            AnimationAcceptanceExampleProfile[] profiles,
            AnimatorControllerDefinition controllerDefinition)
        {
            RigId = rigId;
            DisplayName = displayName;
            Summary = summary;
            LayerSummary = layerSummary;
            ControllerKey = controllerKey;
            LayerMode = layerMode;
            ManualAnchorCm = manualAnchorCm;
            SpeedParameterIndex = speedParameterIndex;
            LocomotionBoolParameterIndex = locomotionBoolParameterIndex;
            FireTriggerParameterIndex = fireTriggerParameterIndex;
            IdleOverlayStateIndex = idleOverlayStateIndex;
            FireOverlayStateIndex = fireOverlayStateIndex;
            FireOverlayDurationSeconds = fireOverlayDurationSeconds;
            StateLabels = stateLabels ?? Array.Empty<string>();
            StateDescriptions = stateDescriptions ?? Array.Empty<string>();
            OverlayStateLabels = overlayStateLabels ?? Array.Empty<string>();
            TransitionDescriptions = transitionDescriptions ?? Array.Empty<string>();
            FloatParameters = floatParameters ?? Array.Empty<AnimationAcceptanceParameterDefinition>();
            BoolParameters = boolParameters ?? Array.Empty<AnimationAcceptanceParameterDefinition>();
            TriggerParameters = triggerParameters ?? Array.Empty<AnimationAcceptanceParameterDefinition>();
            Profiles = profiles ?? Array.Empty<AnimationAcceptanceExampleProfile>();
            ControllerDefinition = controllerDefinition ?? throw new ArgumentNullException(nameof(controllerDefinition));
        }

        public AnimationAcceptanceRigId RigId { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public string LayerSummary { get; }
        public string ControllerKey { get; }
        public AnimatorAuxLayerMode LayerMode { get; }
        public Vector2 ManualAnchorCm { get; }
        public int SpeedParameterIndex { get; }
        public int LocomotionBoolParameterIndex { get; }
        public int FireTriggerParameterIndex { get; }
        public int IdleOverlayStateIndex { get; }
        public int FireOverlayStateIndex { get; }
        public float FireOverlayDurationSeconds { get; }
        public string[] StateLabels { get; }
        public string[] StateDescriptions { get; }
        public string[] OverlayStateLabels { get; }
        public string[] TransitionDescriptions { get; }
        public AnimationAcceptanceParameterDefinition[] FloatParameters { get; }
        public AnimationAcceptanceParameterDefinition[] BoolParameters { get; }
        public AnimationAcceptanceParameterDefinition[] TriggerParameters { get; }
        public AnimationAcceptanceExampleProfile[] Profiles { get; }
        public AnimatorControllerDefinition ControllerDefinition { get; }
    }

    internal static class AnimationAcceptanceRigCatalog
    {
        public static readonly AnimationAcceptanceRigDefinition Tank = BuildTankDefinition();
        public static readonly AnimationAcceptanceRigDefinition Humanoid = BuildHumanoidDefinition();
        public static readonly AnimationAcceptanceRigDefinition[] All = [Tank, Humanoid];

        public static AnimationAcceptanceRigDefinition Get(AnimationAcceptanceRigId rigId)
        {
            return rigId switch
            {
                AnimationAcceptanceRigId.Tank => Tank,
                AnimationAcceptanceRigId.Humanoid => Humanoid,
                _ => throw new ArgumentOutOfRangeException(nameof(rigId)),
            };
        }

        public static AnimationAcceptanceRigDefinition? TryGetByControllerKey(string controllerKey)
        {
            if (string.IsNullOrWhiteSpace(controllerKey))
            {
                return null;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].ControllerKey, controllerKey, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            return null;
        }

        private static AnimationAcceptanceRigDefinition BuildTankDefinition()
        {
            return new AnimationAcceptanceRigDefinition(
                rigId: AnimationAcceptanceRigId.Tank,
                displayName: "Layered Tank",
                summary: "Lower-body chassis locomotion runs through the Core animator state machine. Turret yaw and recoil stay in the Raylib adapter prototype layer.",
                layerSummary: "Case A: moving hull + independently aiming turret + recoil overlay.",
                controllerKey: AnimationAcceptanceIds.TankControllerKey,
                layerMode: AnimatorAuxLayerMode.TankTurret,
                manualAnchorCm: new Vector2(1600f, 1500f),
                speedParameterIndex: 0,
                locomotionBoolParameterIndex: 1,
                fireTriggerParameterIndex: 2,
                idleOverlayStateIndex: 1,
                fireOverlayStateIndex: 2,
                fireOverlayDurationSeconds: 0.34f,
                stateLabels:
                [
                    "HullIdle",
                    "HullCruise",
                    "HullFireRecover",
                ],
                stateDescriptions:
                [
                    "Packed 31. Idle chassis pose for clear baseline verification.",
                    "Packed 32. Rolling chassis clip used whenever speed stays above the move threshold.",
                    "Packed 33. Short fire recovery clip triggered by the fire parameter.",
                ],
                overlayStateLabels:
                [
                    "OverlayNone",
                    "TurretTrack",
                    "TurretRecoil",
                ],
                transitionDescriptions:
                [
                    "speed >= 0.25 : HullIdle -> HullCruise",
                    "speed <= 0.15 : HullCruise -> HullIdle",
                    "fire trigger : HullIdle/HullCruise -> HullFireRecover",
                    "state time >= 92% : HullFireRecover -> HullIdle, then speed is re-evaluated next tick",
                ],
                floatParameters:
                [
                    new AnimationAcceptanceParameterDefinition("speed", 0, "Normalized locomotion speed for the lower-body chassis state machine."),
                ],
                boolParameters:
                [
                    new AnimationAcceptanceParameterDefinition("move_enabled", 1, "Manual locomotion intent flag used by the inspector and acceptance profiles."),
                ],
                triggerParameters:
                [
                    new AnimationAcceptanceParameterDefinition("fire", 2, "One-shot trigger that forces the fire recovery state and turret recoil overlay."),
                ],
                profiles:
                [
                    new AnimationAcceptanceExampleProfile("tank_parked", "Parked Aim", "Idle hull with turret tracking only.", 0f, false, 0f, 0.35f, 1f, 0f, 0f, false),
                    new AnimationAcceptanceExampleProfile("tank_patrol", "Patrol Roll", "Cruising hull while the turret scans off-axis.", 0.7f, true, 0.3f, 0.75f, 1f, 0.18f, 0.1f, false),
                    new AnimationAcceptanceExampleProfile("tank_burst", "Burst Shot", "Move-state shot to validate trigger-driven fire recovery and recoil.", 0.55f, true, -0.15f, -0.9f, 1f, 0.42f, 0f, true),
                ],
                controllerDefinition: new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 31, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 32, DurationSeconds = 0.55f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 33, DurationSeconds = 0.32f, PlaybackSpeed = 1f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.FloatGreaterOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.25f,
                            DurationSeconds = 0.12f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 0,
                            ConditionKind = AnimatorConditionKind.FloatLessOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.15f,
                            DurationSeconds = 0.15f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 2,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 2,
                            DurationSeconds = 0.03f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 2,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 2,
                            DurationSeconds = 0.02f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 2,
                            ToStateIndex = 0,
                            ConditionKind = AnimatorConditionKind.AutoOnNormalizedTime,
                            Threshold = 0.92f,
                            DurationSeconds = 0f,
                        },
                    ],
                });
        }

        private static AnimationAcceptanceRigDefinition BuildHumanoidDefinition()
        {
            return new AnimationAcceptanceRigDefinition(
                rigId: AnimationAcceptanceRigId.Humanoid,
                displayName: "Upper/Lower Mix Humanoid",
                summary: "Lower-body locomotion states stay in Core while upper-body aim and burst timing are driven as adapter-side overlay channels.",
                layerSummary: "Case B: walk/run lower body + independently aiming, firing upper body.",
                controllerKey: AnimationAcceptanceIds.HumanoidControllerKey,
                layerMode: AnimatorAuxLayerMode.HumanoidUpperBody,
                manualAnchorCm: new Vector2(3000f, 1800f),
                speedParameterIndex: 0,
                locomotionBoolParameterIndex: 3,
                fireTriggerParameterIndex: 4,
                idleOverlayStateIndex: 2,
                fireOverlayStateIndex: 3,
                fireOverlayDurationSeconds: 0.28f,
                stateLabels:
                [
                    "LowerIdle",
                    "LowerWalk",
                    "LowerRun",
                    "LowerFireRecover",
                ],
                stateDescriptions:
                [
                    "Packed 41. Neutral lower-body loop.",
                    "Packed 42. Walk cadence driven by the speed parameter.",
                    "Packed 43. Run cadence to prove multi-threshold transitions.",
                    "Packed 44. Brief fire recovery used to visualize trigger ownership.",
                ],
                overlayStateLabels:
                [
                    "OverlayNone",
                    "UpperRelaxed",
                    "UpperAimHold",
                    "UpperBurstFire",
                ],
                transitionDescriptions:
                [
                    "speed >= 0.20 : LowerIdle -> LowerWalk",
                    "speed <= 0.10 : LowerWalk -> LowerIdle",
                    "speed >= 0.75 : LowerWalk -> LowerRun",
                    "speed <= 0.55 : LowerRun -> LowerWalk",
                    "fire trigger : any locomotion state -> LowerFireRecover",
                    "state time >= 92% : LowerFireRecover -> LowerIdle, then locomotion re-evaluates next tick",
                ],
                floatParameters:
                [
                    new AnimationAcceptanceParameterDefinition("speed", 0, "Normalized lower-body locomotion speed."),
                ],
                boolParameters:
                [
                    new AnimationAcceptanceParameterDefinition("upper_body_armed", 3, "Manual aim/fire readiness bit for inspector-driven acceptance scenarios."),
                ],
                triggerParameters:
                [
                    new AnimationAcceptanceParameterDefinition("fire", 4, "Burst fire trigger for the locomotion state machine and upper-body overlay."),
                ],
                profiles:
                [
                    new AnimationAcceptanceExampleProfile("humanoid_ready", "Ready Stance", "Idle lower body with upper-body aim hold.", 0f, false, 0.1f, 0.35f, 0.45f, 0.05f, 0.12f, false),
                    new AnimationAcceptanceExampleProfile("humanoid_strafe", "Strafe Aim", "Walking lower body while upper body keeps target lock.", 0.45f, true, 0.2f, 0.75f, 0.65f, 0.38f, 0.3f, false),
                    new AnimationAcceptanceExampleProfile("humanoid_burst", "Burst Fire", "Run-speed lower body with a one-shot fire trigger and upper-body recoil.", 0.92f, true, -0.05f, -1.0f, 1f, 0.62f, 0f, true),
                ],
                controllerDefinition: new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 41, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 42, DurationSeconds = 0.58f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 43, DurationSeconds = 0.42f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 44, DurationSeconds = 0.28f, PlaybackSpeed = 1f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.FloatGreaterOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.20f,
                            DurationSeconds = 0.08f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 0,
                            ConditionKind = AnimatorConditionKind.FloatLessOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.10f,
                            DurationSeconds = 0.08f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 2,
                            ConditionKind = AnimatorConditionKind.FloatGreaterOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.75f,
                            DurationSeconds = 0.06f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 2,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.FloatLessOrEqual,
                            ParameterIndex = 0,
                            Threshold = 0.55f,
                            DurationSeconds = 0.06f,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 3,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 4,
                            DurationSeconds = 0.02f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 3,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 4,
                            DurationSeconds = 0.02f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 2,
                            ToStateIndex = 3,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 4,
                            DurationSeconds = 0.02f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 3,
                            ToStateIndex = 0,
                            ConditionKind = AnimatorConditionKind.AutoOnNormalizedTime,
                            Threshold = 0.92f,
                            DurationSeconds = 0f,
                        },
                    ],
                });
        }
    }
}
