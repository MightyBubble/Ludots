using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Components
{
    public enum NavPhysicsMode : byte
    {
        NavOnly = 0,
        NavCrowdResolve = 1,
        FullPhysics2D = 2,
    }

    public enum NavSolverMode : byte
    {
        PreciseOrca = 0,
        CrowdFlow = 1,
        Hybrid = 2,
    }

    public enum NavPushClass : byte
    {
        Cooperative = 0,
        Blocking = 1,
        Dominant = 2,
    }

    public struct NavActor
    {
        public byte IsEnabled;
        public byte PhysicsModeValue;
        public byte DefaultSolverModeValue;

        public readonly bool Enabled => IsEnabled != 0;

        public NavPhysicsMode PhysicsMode
        {
            readonly get => (NavPhysicsMode)PhysicsModeValue;
            set => PhysicsModeValue = (byte)value;
        }

        public NavSolverMode DefaultSolverMode
        {
            readonly get => (NavSolverMode)DefaultSolverModeValue;
            set => DefaultSolverModeValue = (byte)value;
        }

    }

    public struct NavProfileRef
    {
        public int ProfileId;
    }

    public struct NavCrowdProfileRef
    {
        public int ProfileId;
    }

    public struct NavKnockbackPolicyRef
    {
        public int PolicyId;
    }

    public struct NavCrowdAgent2D
    {
        public Fix64 GeometryRadiusCm;
        public Fix64 NavMass;
        public Fix64 YieldWeight;
        public byte PushClassValue;
        public byte PreferredSolverModeValue;
        public int RetryLimit;
        public int TimeoutTicks;
        public int AbandonTicks;

        public NavPushClass PushClass
        {
            readonly get => (NavPushClass)PushClassValue;
            set => PushClassValue = (byte)value;
        }

        public NavSolverMode PreferredSolverMode
        {
            readonly get => (NavSolverMode)PreferredSolverModeValue;
            set => PreferredSolverModeValue = (byte)value;
        }
    }

    public struct NavPhysicalOverride
    {
        public byte IsActive;
        public byte SavedPhysicsModeValue;
        public int RemainingTicks;

        public readonly bool Active => IsActive != 0;

        public NavPhysicsMode SavedPhysicsMode
        {
            readonly get => (NavPhysicsMode)SavedPhysicsModeValue;
            set => SavedPhysicsModeValue = (byte)value;
        }
    }

    public struct NavSolverModeComponent
    {
        public byte Value;
        public int RuleId;

        public NavSolverMode SolverMode
        {
            readonly get => (NavSolverMode)Value;
            set => Value = (byte)value;
        }
    }

    public struct NavActorRuntimeState
    {
        public byte IsValidated;
        public byte IsMaterialized;
        public byte EffectivePhysicsModeValue;
        public byte AddedMass2D;
        public int AppliedNavProfileId;
        public int AppliedCrowdProfileId;
        public int AppliedKnockbackPolicyId;

        public NavPhysicsMode EffectivePhysicsMode
        {
            readonly get => (NavPhysicsMode)EffectivePhysicsModeValue;
            set => EffectivePhysicsModeValue = (byte)value;
        }
    }

    public struct NavGroupTag
    {
    }

    public struct NavGroupIdentity
    {
        public int GroupId;
    }

    public struct NavGroupOwner
    {
        public Entity Value;
    }

    public struct NavGroupTeam
    {
        public int TeamId;
    }

    public struct NavGroupMember
    {
        public int GroupId;
        public int SlotIndex;
    }

    public struct NavGroupTarget2D
    {
        public Fix64Vec2 TargetCm;
        public Fix64 RadiusCm;
        public int FormationSpacingCm;
        public Fix64 RotationRad;
    }

    public struct NavGroupRuntimeState
    {
        public byte SolverModeValue;
        public int ActiveRuleId;
        public int MemberCount;
        public int ArrivedMemberCount;
        public int RetryCount;
        public int TimeoutCount;
        public int AbandonCount;
        public byte IsArrived;
        public byte IsDirty;

        public NavSolverMode SolverMode
        {
            readonly get => (NavSolverMode)SolverModeValue;
            set => SolverModeValue = (byte)value;
        }
    }

    public struct NavAgentProgressState
    {
        public Fix64Vec2 LastGoalTargetCm;
        public Fix64 LastGoalRadiusCm;
        public Fix64 LastDistanceCm;
        public int StallTicks;
        public int TotalStallTicks;
        public int RetryCount;
        public byte IsAbandoned;
    }
}
