using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using PresenterTreeOverrideShowcaseMod.Runtime;

namespace PresenterTreeOverrideShowcaseMod.Systems
{
    /// <summary>
    /// 镜头按地图 metadata 的 cameraTour 循环换站。每一站到点就强制写一次姿态，
    /// 停留的那几秒不再改。玩家不能把镜头留在某一站。
    /// </summary>
    internal sealed class PresenterTreeCameraTourSystem : BaseSystem<World, float>
    {
        private readonly GameEngine _engine;
        private string? _configuredMapId;
        private bool _hasTour;
        private PresenterTreeCameraTour _tour;
        private float _elapsedSeconds;
        private int _currentStop;

        public PresenterTreeCameraTourSystem(GameEngine engine)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
        }

        public override void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!string.Equals(mapId, _configuredMapId, StringComparison.Ordinal))
            {
                _configuredMapId = mapId;
                _hasTour = PresenterTreeShowcaseMetadata.TryReadCameraTour(_engine, out _tour);
                _elapsedSeconds = 0f;
                _currentStop = 0;
            }

            if (!_hasTour)
            {
                return;
            }

            _elapsedSeconds += dt;
            int stop = _tour.StopIndexAt(_elapsedSeconds);
            if (stop == _currentStop)
            {
                return;
            }

            _currentStop = stop;
            _engine.SetService(CoreServiceKeys.CameraPoseRequest, _tour.Poses[stop]);
        }
    }
}
