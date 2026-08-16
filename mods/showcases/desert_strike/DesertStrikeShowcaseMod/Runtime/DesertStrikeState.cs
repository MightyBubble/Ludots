using System.Collections.Generic;
using Arch.Core;

namespace DesertStrikeShowcaseMod.Runtime
{
    public readonly record struct DesertStrikePurchase(string UnitId, int LaneIndex);

    public sealed class DesertStrikeState
    {
        public Entity PlayerBase = Entity.Null;
        public Entity AiBase = Entity.Null;
        public int PlayerTeam;
        public int AiTeam;

        public readonly List<DesertStrikePurchase> PlayerQueue = new();
        public readonly List<DesertStrikePurchase> AiQueue = new();
        public int PlayerNextLane;
        public int AiNextLane;

        public readonly Dictionary<int, Entity> PlayerSpawnMarkers = new();
        public readonly Dictionary<int, Entity> AiSpawnMarkers = new();

        public int WaveNumber;
        public int NextWaveStep;
        public int NextIncomeStep;

        public bool GameOver;
        public int WinnerPlayerId;
        public int DestroyedBaseTeam;

        public int WaveReceiptChannelId;
        public int UnitsSpawned;
        public int UnitsDestroyed;
        public int PurchaseDeniedCount;
    }
}
