namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Global presentation frame state for smooth visual rendering.
    /// This is a singleton component that provides interpolation factor to ALL visual sync systems.
    /// 
    /// Architecture alignment with Performer:
    /// - Written by: PresentationFrameSetupSystem (once per render frame, before all visual systems)
    /// - Read by: Any visual sync system (Physics2D, Animation, Network, etc.)
    /// - Data flow: Logic domain → Interpolation → Visual domain (VisualTransform) → DrawBuffer
    /// 
    /// This is a GENERIC mechanism, not tied to any specific system like Physics2D.
    /// </summary>
    public struct PresentationFrameState
    {
        /// <summary>
        /// Interpolation factor [0, 1] for the current render frame.
        /// 0 = Just after FixedUpdate (use previous state)
        /// 1 = Just before next FixedUpdate (use current state)
        /// 
        /// Calculated as: accumulatedTime / FixedDeltaTime
        /// where accumulatedTime is from Pacemaker.
        /// 
        /// All visual sync systems should use this value to interpolate between
        /// previous and current logic states.
        /// </summary>
        public float InterpolationAlpha;
        
        /// <summary>
        /// Whether interpolation is enabled globally.
        /// When false, InterpolationAlpha is always 1.0 (no interpolation).
        /// </summary>
        public bool Enabled;
        
        /// <summary>
        /// Render frame delta time (wall-clock).
        /// </summary>
        public float RenderDeltaTime;
        
        /// <summary>
        /// Fixed update delta time (for reference).
        /// </summary>
        public float FixedDeltaTime;
        
        /// <summary>
        /// Number of FixedUpdates that ran this render frame.
        /// Useful for debugging timing issues.
        /// </summary>
        public int FixedUpdatesThisFrame;
        
        /// <summary>
        /// Default state with interpolation enabled.
        /// </summary>
        public static PresentationFrameState Default => new PresentationFrameState
        {
            InterpolationAlpha = 1f,
            Enabled = true,
            RenderDeltaTime = 0f,
            FixedDeltaTime = 0.02f,
            FixedUpdatesThisFrame = 0
        };
    }
    
    /// <summary>
    /// Marker tag for the singleton entity holding PresentationFrameState.
    /// </summary>
    public struct PresentationFrameStateTag { }
}
