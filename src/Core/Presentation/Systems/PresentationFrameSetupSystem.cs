using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Sets up the presentation frame state at the start of each render frame.
    /// This system MUST run before all visual sync systems.
    /// 
    /// Architecture:
    /// - Runs once per render frame (not per FixedUpdate)
    /// - Reads Pacemaker.InterpolationAlpha
    /// - Writes to PresentationFrameState singleton
    /// - All visual sync systems then read from PresentationFrameState
    /// 
    /// This is the single source of truth for interpolation, ensuring:
    /// - All systems use the same alpha value
    /// - No redundant calculations
    /// - Easy to disable/debug interpolation globally
    /// </summary>
    public sealed class PresentationFrameSetupSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly RealtimePacemaker? _pacemaker;
        private Entity _stateEntity;
        private bool _initialized;
        
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Whether interpolation is enabled.
        /// Set to false to disable interpolation (alpha = 1).
        /// </summary>
        public bool InterpolationEnabled { get; set; } = true;
        
        public PresentationFrameSetupSystem(World world, IPacemaker pacemaker)
        {
            _world = world;
            _pacemaker = pacemaker as RealtimePacemaker;
        }
        
        public void Initialize()
        {
            // Create singleton entity with PresentationFrameState
            _stateEntity = _world.Create(
                PresentationFrameState.Default,
                new PresentationFrameStateTag()
            );
            _initialized = true;
        }
        
        public void BeforeUpdate(in float dt) { }
        
        public void Update(in float renderDeltaTime)
        {
            if (!Enabled || !_initialized) return;
            
            // Calculate interpolation alpha
            float alpha = 1f;
            if (InterpolationEnabled && _pacemaker != null)
            {
                alpha = _pacemaker.InterpolationAlpha;
            }
            
            // Update the singleton state
            ref var state = ref _world.Get<PresentationFrameState>(_stateEntity);
            state.InterpolationAlpha = alpha;
            state.Enabled = InterpolationEnabled;
            state.RenderDeltaTime = renderDeltaTime;
            state.FixedDeltaTime = Time.FixedDeltaTime;
            // Note: FixedUpdatesThisFrame would need to be tracked by Pacemaker
        }
        
        public void AfterUpdate(in float dt) { }
        
        public void Dispose() { }
        
        /// <summary>
        /// Gets the current interpolation alpha.
        /// Can be called by systems that don't want to query the component.
        /// </summary>
        public float GetInterpolationAlpha()
        {
            if (!_initialized || !_world.IsAlive(_stateEntity)) return 1f;
            return _world.Get<PresentationFrameState>(_stateEntity).InterpolationAlpha;
        }
    }
}
