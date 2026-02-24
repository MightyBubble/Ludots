using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.InteropServices.JavaScript;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.Map.Generator;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Coordinates;
using Ludots.Web.Interop;
using Microsoft.JSInterop;
using Ludots.Core.Mathematics;

namespace Ludots.Web.Services
{
    public class GameInitializer : IDisposable
    {
        private readonly IResourceService _resourceService;
        private readonly IJSRuntime _jsRuntime;
        private readonly ICoordinateMapper _mapper;
        private readonly World _world;
        private readonly WorldMap _worldMap;
        private DotNetObjectReference<GameInitializer> _objRef;

        private readonly float[] _positionBuffer;
        private readonly int _entityCount = 20000;
        private readonly QueryDescription _moveAndPackQuery;
        private readonly ForEachWithEntity<Position, Velocity> _moveAndPack;
        private readonly int _worldWidth;
        private readonly int _worldHeight;
        private int _packIndex;

        public GameInitializer(IResourceService resourceService, IJSRuntime jsRuntime)
        {
            _resourceService = resourceService;
            _jsRuntime = jsRuntime;
            _world = World.Create();
            _worldMap = new WorldMap();
            _objRef = DotNetObjectReference.Create(this);
            _mapper = CoordinateSystemFactory.CreateMapper(PlatformType.Web);
            // Allocate for Vector4 (X, Y, Z, W) alignment
            _positionBuffer = new float[_entityCount * 4];
            _moveAndPackQuery = new QueryDescription().WithAll<Position, Velocity>();
            _moveAndPack = MoveAndPack;
            _worldWidth = WorldMap.TotalWidth * WorldMap.WorldScale;
            _worldHeight = WorldMap.TotalHeight * WorldMap.WorldScale;
        }

        public async Task InitializeAsync()
        {
            Console.WriteLine("Initializing Game...");

            if (OperatingSystem.IsBrowser())
            {
                await JSHost.ImportAsync("LudotsRender", "./js/ludotsRender.js");
            }

            Console.WriteLine("Generating Procedural Map...");
            var generator = new MapGenerator();
            generator.GenerateSingleTile(_worldMap, 0, 0);

            SpawnDebugEntities(_entityCount);

            await _jsRuntime.InvokeVoidAsync("ludots.startGameLoop", _objRef);
            
            Console.WriteLine("Game Initialized & Loop Started.");
        }

        private void SpawnDebugEntities(int count)
        {
            var random = new Random();
            
            for (int i = 0; i < count; i++)
            {
                // Align with Raylib Debug: -5000 to 5000 logic units
                var pos = new IntVector2(random.Next(-5000, 5001), random.Next(-5000, 5001));
                var vel = new IntVector2(random.Next(-100, 101), random.Next(-100, 101));
                
                _world.Create(
                    new Position { GridPos = pos },
                    new Velocity { Value = vel }
                );
            }
        }

        [JSInvokable]
        public void GameLoop(float dt) // dt in milliseconds
        {
            _packIndex = 0;
            _world.Query(in _moveAndPackQuery, _moveAndPack);
            // Cast float[] to Span<byte> for MemoryView compatibility
            EntityRenderInterop.UpdateEntityPositionsInt32(MemoryMarshal.AsBytes(_positionBuffer.AsSpan()), _packIndex);
        }

        private void MoveAndPack(Entity entity, ref Position pos, ref Velocity vel)
        {
            pos.GridPos += vel.Value;
            
            // Simplified boundary check for debug (Logic Space -5000 to 5000)
            if (Math.Abs(pos.GridPos.X) > 5000)
            {
                vel.Value.X = -vel.Value.X;
                pos.GridPos.X += vel.Value.X;
            }

            if (Math.Abs(pos.GridPos.Y) > 5000)
            {
                vel.Value.Y = -vel.Value.Y;
                pos.GridPos.Y += vel.Value.Y;
            }

            if (_packIndex >= _entityCount) return;

            var baseIndex = _packIndex * 4;
            if ((uint)(baseIndex + 3) < (uint)_positionBuffer.Length)
            {
                var visualPos = _mapper.LogicToVisual(pos.GridPos, 0);

                _positionBuffer[baseIndex] = visualPos.X;
                _positionBuffer[baseIndex + 1] = visualPos.Y;
                _positionBuffer[baseIndex + 2] = visualPos.Z;
                _positionBuffer[baseIndex + 3] = 0f; // Padding/W
            }
            _packIndex++;
        }

        public void Dispose()
        {
            _objRef?.Dispose();
            World.Destroy(_world);
        }
    }
}
