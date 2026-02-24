using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;

namespace Ludots.Core.Systems
{
    public sealed class GridMovementSystem
    {
        private readonly World _world;
        private readonly WorldMap _worldMap;
        private readonly QueryDescription _query;
        private readonly ForEachWithEntity<Position, Velocity> _move;
        private readonly int _worldWidth;
        private readonly int _worldHeight;

        public GridMovementSystem(World world, WorldMap worldMap)
        {
            _world = world;
            _worldMap = worldMap;
            _query = new QueryDescription().WithAll<Position, Velocity>();
            _move = Move;
            _worldWidth = _worldMap.TotalWidth * WorldMap.WorldScale;
            _worldHeight = _worldMap.TotalHeight * WorldMap.WorldScale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float dt)
        {
            _world.Query(in _query, _move);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Move(Entity entity, ref Position pos, ref Velocity vel)
        {
            pos.GridPos += vel.Value;

            if (pos.GridPos.X < 0 || pos.GridPos.X > _worldWidth)
            {
                vel.Value.X = -vel.Value.X;
                pos.GridPos.X += vel.Value.X;
            }

            if (pos.GridPos.Y < 0 || pos.GridPos.Y > _worldHeight)
            {
                vel.Value.Y = -vel.Value.Y;
                pos.GridPos.Y += vel.Value.Y;
            }
        }
    }
}
