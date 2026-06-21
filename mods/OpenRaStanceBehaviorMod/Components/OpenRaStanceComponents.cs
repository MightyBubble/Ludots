using Arch.Core;

namespace OpenRaStanceBehaviorMod.Components;

public static class OpenRaCombatStances
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

public static class OpenRaOrderKeys
{
    public const string AttackMove = "attackMove";
    public const string AssaultMove = "assaultMove";
    public const string Guard = "guard";
    public const string SetCombatStance = "setCombatStance";
    public const string Scatter = "scatter";
    public const string MoveTo = "moveTo";
    public const string AttackTarget = "attackTarget";
}

public struct OpenRaCombatStanceState
{
    public int Stance;
    public int LeashRadiusCm;
    public int RetaliationTtlSteps;
}

public struct OpenRaRetaliationMemory
{
    public Entity LastAttacker;
    public int LastAttackerStep;
}

public struct OpenRaAttackMoveRuntime
{
    public int DestinationX;
    public int DestinationY;
    public int LeashRadiusCm;
    public Entity EngagedTarget;
    public byte Assault;
}

public struct OpenRaGuardRuntime
{
    public Entity Guarded;
    public int RadiusCm;
    public int LeashRadiusCm;
    public Entity EngagedTarget;
}
