using System;
using Arch.System;

namespace Ludots.Core.Gameplay.Narrative
{
    public sealed class NarrativeRuntimeSystem : ISystem<float>
    {
        private readonly NarrativeDirector _director;

        public NarrativeRuntimeSystem(NarrativeDirector director)
        {
            _director = director ?? throw new ArgumentNullException(nameof(director));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            _director.Update(dt);
        }
    }
}
