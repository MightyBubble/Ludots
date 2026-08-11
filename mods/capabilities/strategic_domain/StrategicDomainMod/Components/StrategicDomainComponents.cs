namespace StrategicDomainMod.Components
{
    public enum SettlementControlState : byte
    {
        Intact = 1,
        Capturable = 2,
        Ruined = 3,
    }

    public struct SettlementIdentityCm
    {
        public int SettlementKey;
        public int FactionOwner;
    }

    public struct SettlementDefenseCm
    {
        public float WallDurability;
        public float WallDurabilityMax;
        public float GarrisonPool;
        public float GarrisonPoolMax;
        public SettlementControlState ControlState;
    }

    public struct SettlementGovernanceCm
    {
        public int GovernorHeroKey;
        public int ResidentHeroKey;
        public int CaptiveHeroKey;
        public float ProductionOutput;
        public float RelationModifier;
    }

    public struct SupplyNodeCm
    {
        public int NodeKey;
        public int SettlementKey;
        public bool ProvidesSupply;
        public bool IsHub;
        public float SupplyCapacity;
        public float DemandWeight;
    }

    public struct FieldForceCm
    {
        public int ForceKey;
        public int FactionOwner;
        public int SubnetKey;
        public float Strength;
        public bool HasSiegeCapability;
        public bool IsLogistics;
    }
}
