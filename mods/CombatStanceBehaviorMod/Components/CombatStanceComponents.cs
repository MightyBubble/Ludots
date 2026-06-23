using Arch.Core;

namespace CombatStanceBehaviorMod.Components;

public static class CombatStances
{
    public const int HoldFire = 0;
    public const int ReturnFire = 1;
    public const int Defend = 2;
    public const int AttackAnything = 3;

    public static bool IsDefined(int stance)
    {
        return stance >= HoldFire && stance <= AttackAnything;
    }
}

public static class StanceOrderKeys
{
    public const string AttackMove = "attackMove";
    public const string AssaultMove = "assaultMove";
    public const string Guard = "guard";
    public const string SetCombatStance = "setCombatStance";
    public const string Scatter = "scatter";
    public const string MoveTo = "moveTo";
    public const string AttackTarget = "attackTarget";
}

public struct CombatStanceState
{
    public int Stance;
    public int LeashRadiusCm;
    public int RetaliationTtlSteps;
}

public struct RetaliationMemory
{
    public Entity LastAttacker;
    public int LastAttackerStep;
}

public struct AttackMoveRuntime
{
    public int DestinationX;
    public int DestinationY;
    public int LeashRadiusCm;
    public Entity EngagedTarget;
    public byte Assault;
}

public struct GuardRuntime
{
    public Entity Guarded;
    public int RadiusCm;
    public int LeashRadiusCm;
    public Entity EngagedTarget;
}
