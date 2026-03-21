using Arch.System;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionMaintenanceSystem : ISystem<float>
    {
        private readonly SelectionRuntime _selection;

        public SelectionMaintenanceSystem(Arch.Core.World world, SelectionRuntime selection)
        {
            _selection = selection;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            _selection.SweepDanglingState();
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
