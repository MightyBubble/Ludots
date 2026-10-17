using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Sets up the presentation frame state at the start of each render frame
    /// (not per FixedUpdate). This system MUST run before all visual sync systems:
    /// it is the single source of truth for the interpolation alpha, and all
    /// visual sync systems read it from the PresentationFrameState singleton.
    /// </summary>
    public sealed class PresentationFrameSetupSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly RealtimePacemaker? _pacemaker;
        private readonly Func<IPresentationInterpolationSource?>? _externalInterpolationSource;
        private Entity _stateEntity;
        private bool _initialized;
        
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Set to false to disable interpolation (alpha = 1).
        /// </summary>
        public bool InterpolationEnabled { get; set; } = true;
        
        public PresentationFrameSetupSystem(
            World world,
            IPacemaker pacemaker,
            Func<IPresentationInterpolationSource?>? externalInterpolationSource = null)
        {
            _world = world;
            _pacemaker = pacemaker as RealtimePacemaker;
            _externalInterpolationSource = externalInterpolationSource;
        }
        
        public void Initialize()
        {
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
            
            float alpha = 1f;
            if (InterpolationEnabled)
            {
                IPresentationInterpolationSource? external = _externalInterpolationSource?.Invoke();
                alpha = external?.InterpolationAlpha ?? _pacemaker?.InterpolationAlpha ?? 1f;
                if (!float.IsFinite(alpha) || alpha < 0f || alpha > 1f)
                {
                    throw new InvalidOperationException(
                        $"Presentation interpolation source returned invalid alpha {alpha}.");
                }
            }
            
            // Update the singleton state
            ref var state = ref _world.Get<PresentationFrameState>(_stateEntity);
            state.InterpolationAlpha = alpha;
            state.Enabled = InterpolationEnabled;
            state.RenderDeltaTime = renderDeltaTime;
            state.FixedDeltaTime = Time.FixedDeltaTime;
            unchecked
            {
                state.FrameId++;
            }
            // Note: FixedUpdatesThisFrame would need to be tracked by Pacemaker
        }
        
        public void AfterUpdate(in float dt) { }
        
        public void Dispose() { }
        
        /// <summary>
        /// Current interpolation alpha, for systems that don't want to query the component.
        /// </summary>
        public float GetInterpolationAlpha()
        {
            if (!_initialized || !_world.IsAlive(_stateEntity)) return 1f;
            return _world.Get<PresentationFrameState>(_stateEntity).InterpolationAlpha;
        }
    }
}
