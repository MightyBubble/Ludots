using System;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct VfxEffectAssetData
    {
        public VfxEffectAssetData(
            ParticleEffectAssetData particleSystem,
            int particleEffectAssetId)
        {
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

            ParticleSystem = particleSystem;
            ParticleEffectAssetId = particleEffectAssetId;
        }

        public PrefabVfxSpawnMode SpawnMode =>
            ParticleSystem?.SpawnMode
            ?? throw new InvalidOperationException("VFX effect data requires a registered Quarks particle effect.");

        public ParticleEffectAssetData? ParticleSystem { get; }

        public int ParticleEffectAssetId { get; }

        public bool IsValid =>
            ParticleEffectAssetId > 0 &&
            ParticleSystem != null &&
            ParticleSystem.IsValid;
    }
}
