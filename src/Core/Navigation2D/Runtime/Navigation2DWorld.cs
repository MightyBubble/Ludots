using Arch.Core;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Runtime
{
    public sealed unsafe class Navigation2DWorld : IDisposable
    {
        public readonly Navigation2DWorldSettings Settings;

        public UnsafeList<Entity> Entities;
        public UnsafeList<Fix64Vec2> PositionsCm;
        public UnsafeList<Fix64Vec2> VelocitiesCmPerSec;
        public UnsafeList<Fix64> RadiiCm;
        public UnsafeList<Fix64> MaxSpeedCmPerSec;

        public int Count => Entities.Count;

        public Navigation2DWorld(Navigation2DWorldSettings settings)
        {
            Settings = settings;

            Entities = new UnsafeList<Entity>(settings.MaxAgents);
            PositionsCm = new UnsafeList<Fix64Vec2>(settings.MaxAgents);
            VelocitiesCmPerSec = new UnsafeList<Fix64Vec2>(settings.MaxAgents);
            RadiiCm = new UnsafeList<Fix64>(settings.MaxAgents);
            MaxSpeedCmPerSec = new UnsafeList<Fix64>(settings.MaxAgents);
        }

        public void Clear()
        {
            Entities.Clear();
            PositionsCm.Clear();
            VelocitiesCmPerSec.Clear();
            RadiiCm.Clear();
            MaxSpeedCmPerSec.Clear();
        }

        public void Dispose()
        {
            Entities.Dispose();
            PositionsCm.Dispose();
            VelocitiesCmPerSec.Dispose();
            RadiiCm.Dispose();
            MaxSpeedCmPerSec.Dispose();
        }
    }
}

