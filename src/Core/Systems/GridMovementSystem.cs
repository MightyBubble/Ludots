using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Map;

namespace Ludots.Core.Systems
{
    public sealed class GridMovementSystem
    {
        private readonly World _world;
        private readonly WorldMap _worldMap;
        private readonly QueryDescription _query;
        private readonly int _worldWidth;
        private readonly int _worldHeight;

        public GridMovementSystem(World world, WorldMap worldMap)
        {
            _world = world;
            _worldMap = worldMap;
            _query = new QueryDescription().WithAll<Position, Velocity>();
            _worldWidth = _worldMap.TotalWidth * WorldMap.WorldScale;
            _worldHeight = _worldMap.TotalHeight * WorldMap.WorldScale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float dt)
        {
            var job = new MoveJob
            {
                WorldWidth = _worldWidth,
                WorldHeight = _worldHeight,
            };
            _world.InlineQuery<MoveJob, Position, Velocity>(in _query, ref job);
        }

        private struct MoveJob : IForEach<Position, Velocity>
        {
            public int WorldWidth;
            public int WorldHeight;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref Position pos, ref Velocity vel)
            {
                pos.GridPos.X += vel.Value.X;
                pos.GridPos.Y += vel.Value.Y;

                if (pos.GridPos.X < 0 || pos.GridPos.X > WorldWidth)
                {
                    vel.Value.X = -vel.Value.X;
                    pos.GridPos.X += vel.Value.X;
                }

                if (pos.GridPos.Y < 0 || pos.GridPos.Y > WorldHeight)
                {
                    vel.Value.Y = -vel.Value.Y;
                    pos.GridPos.Y += vel.Value.Y;
                }
            }
        }
    }
}
