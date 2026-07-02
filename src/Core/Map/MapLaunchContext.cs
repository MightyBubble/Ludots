using System.Collections.Generic;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Runtime launch/session selection carried with map load.
    /// Map identity describes world content; launch context describes how that world is entered.
    /// </summary>
    public sealed class MapLaunchContext
    {
        public int SelectedPlayerId { get; init; }
        public IReadOnlyDictionary<string, object>? Metadata { get; init; }

        public bool HasSelectedPlayer => SelectedPlayerId > 0;

        public bool IsEmpty =>
            !HasSelectedPlayer &&
            (Metadata == null || Metadata.Count == 0);

        public static MapLaunchContext? Create(
            int selectedPlayerId = 0,
            IReadOnlyDictionary<string, object>? metadata = null)
        {
            var context = new MapLaunchContext
            {
                SelectedPlayerId = selectedPlayerId,
                Metadata = metadata,
            };
            return context.IsEmpty ? null : context;
        }
    }

    public readonly record struct MapLoadRequest(MapId MapId, MapLaunchContext? LaunchContext = null)
    {
        public static MapLoadRequest FromMapId(string mapId) => new(new MapId(mapId));

        public static MapLoadRequest FromMapId(string mapId, MapLaunchContext? launchContext) =>
            new(new MapId(mapId), launchContext);

        public string MapIdValue => MapId.Value;
    }
}
