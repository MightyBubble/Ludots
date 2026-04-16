namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabVfxPart
    {
        public PrefabVfxPart(int effectAssetId, PrefabVfxSpawnMode spawnMode = PrefabVfxSpawnMode.Once)
        {
            EffectAssetId = effectAssetId;
            SpawnMode = spawnMode;
        }

        public int EffectAssetId { get; }

        public PrefabVfxSpawnMode SpawnMode { get; }
    }
}
