namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Opt-in bridge for locally-controlled channel / aim-follow executions.
    /// When the configured slot is actively executing, input-side systems may
    /// continuously refresh the execution target point from the current pointer.
    /// </summary>
    public struct AbilityExecAimSync
    {
        public int AbilitySlot;
        public byte SyncFacing;
        public byte HasLastPointerScreen;
        public int LastPointerScreenX;
        public int LastPointerScreenY;
        public int LastSyncedOrderId;
    }
}
