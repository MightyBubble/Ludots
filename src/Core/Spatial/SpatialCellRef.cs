namespace Ludots.Core.Spatial
{
    public enum SpatialMembershipState : byte
    {
        Uninitialized = 0,
        Active = 1,
        Deactivated = 2,
    }

    public struct SpatialCellRef
    {
        public int CellX;
        public int CellY;
        public SpatialMembershipState State;
    }
}
