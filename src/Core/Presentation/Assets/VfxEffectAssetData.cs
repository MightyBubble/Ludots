using System;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct VfxEffectAssetData
    {
        public VfxEffectAssetData(
            PrefabVfxSpawnMode spawnMode,
            ParticleEffectAssetData particleSystem,
            int particleEffectAssetId)
        {
            if (!Enum.IsDefined(typeof(PrefabVfxSpawnMode), spawnMode))
            {
                throw new ArgumentOutOfRangeException(nameof(spawnMode));
            }

            if (particleSystem == null)
            {
                throw new ArgumentNullException(nameof(particleSystem));
            }

            if (!particleSystem.IsValid)
            {
                throw new ArgumentException("Quarks particle VFX assets require valid particle data.", nameof(particleSystem));
            }

            if (particleEffectAssetId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(particleEffectAssetId),
                    "VFX assets must reference a registered particle effect asset id.");
            }

            SpawnMode = spawnMode;
            ParticleSystem = particleSystem;
            ParticleEffectAssetId = particleEffectAssetId;
        }

        public PrefabVfxSpawnMode SpawnMode { get; }

        public ParticleEffectAssetData? ParticleSystem { get; }

        public int ParticleEffectAssetId { get; }

        public bool IsValid =>
            Enum.IsDefined(typeof(PrefabVfxSpawnMode), SpawnMode) &&
            ParticleEffectAssetId > 0 &&
            ParticleSystem != null &&
            ParticleSystem.IsValid;
    }
}
