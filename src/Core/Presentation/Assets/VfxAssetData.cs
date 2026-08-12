using System;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct VfxAssetData
    {
        public VfxAssetData(
            ParticleVfxAssetData particleSystem,
            int particleVfxAssetId)
        {
            if (particleSystem == null)
            {
                throw new ArgumentNullException(nameof(particleSystem));
            }

            if (!particleSystem.IsValid)
            {
                throw new ArgumentException("Quarks particle VFX assets require valid particle data.", nameof(particleSystem));
            }

            if (particleVfxAssetId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(particleVfxAssetId),
                    "VFX assets must reference a registered particle VFX asset id.");
            }

            ParticleSystem = particleSystem;
            ParticleVfxAssetId = particleVfxAssetId;
        }

        public PrefabVfxSpawnMode SpawnMode =>
            ParticleSystem?.SpawnMode
            ?? throw new InvalidOperationException("VFX effect data requires a registered Quarks particle VFX.");

        public ParticleVfxAssetData? ParticleSystem { get; }

        public int ParticleVfxAssetId { get; }

        public bool IsValid =>
            ParticleVfxAssetId > 0 &&
            ParticleSystem != null &&
            ParticleSystem.IsValid;
    }
}
