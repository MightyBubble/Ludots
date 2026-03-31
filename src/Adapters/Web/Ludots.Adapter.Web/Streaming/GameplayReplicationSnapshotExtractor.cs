using Ludots.Core.Engine;
using Ludots.Core.Networking;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.Web.Streaming
{
    public sealed class GameplayReplicationSnapshotExtractor
    {
        private readonly GameEngine _engine;

        public GameplayReplicationSnapshotExtractor(GameEngine engine)
        {
            _engine = engine;
        }

        public GameplayReplicationSnapshotView Extract()
        {
            GameplayReplicationSnapshotBuffer? buffer = _engine.GetService(CoreServiceKeys.GameplayReplicationSnapshotBuffer);
            if (buffer == null)
            {
                return new GameplayReplicationSnapshotView(
                    SimTick: 0,
                    Count: 0,
                    Capacity: 0,
                    DroppedSinceClear: 0,
                    DroppedTotal: 0,
                    Entities: System.Array.Empty<GameplayReplicationSnapshotEntityView>());
            }

            var span = buffer.GetSpan();
            var entities = new GameplayReplicationSnapshotEntityView[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                entities[i] = new GameplayReplicationSnapshotEntityView(
                    ReplicationEntityId: item.ReplicationEntityId,
                    PresentationStableId: item.PresentationStableId,
                    TeamId: item.TeamId,
                    PlayerId: item.PlayerId,
                    PositionXRaw: item.PositionXRaw,
                    PositionYRaw: item.PositionYRaw,
                    FacingAngleRad: item.FacingAngleRad,
                    Flags: item.Flags);
            }

            return new GameplayReplicationSnapshotView(
                SimTick: buffer.SimTick,
                Count: buffer.Count,
                Capacity: buffer.Capacity,
                DroppedSinceClear: buffer.DroppedSinceClear,
                DroppedTotal: buffer.DroppedTotal,
                Entities: entities);
        }
    }

    public sealed record GameplayReplicationSnapshotView(
        int SimTick,
        int Count,
        int Capacity,
        int DroppedSinceClear,
        int DroppedTotal,
        GameplayReplicationSnapshotEntityView[] Entities);

    public sealed record GameplayReplicationSnapshotEntityView(
        int ReplicationEntityId,
        int PresentationStableId,
        int TeamId,
        int PlayerId,
        long PositionXRaw,
        long PositionYRaw,
        float FacingAngleRad,
        GameplayReplicationSnapshotFlags Flags);
}
