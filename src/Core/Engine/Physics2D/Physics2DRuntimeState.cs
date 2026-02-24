namespace Ludots.Core.Engine.Physics2D
{
    public struct Physics2DRuntimeState
    {
        public bool AnyAwakeDynamicBodies;
        
        /// <summary>
        /// Time of the last physics step (from Time.FixedTotalTime).
        /// Used for render interpolation calculations.
        /// </summary>
        public double LastPhysicsStepTime;
        
        /// <summary>
        /// Physics step duration (1 / PhysicsHz).
        /// Used for render interpolation calculations.
        /// </summary>
        public float PhysicsStepDuration;
        
        /// <summary>
        /// Physics interpolation alpha (0 to 1).
        /// Calculated from DiscreteRateTickDistributor.InterpolationAlpha.
        /// Visual sync systems should use THIS value, not PresentationFrameState.InterpolationAlpha!
        /// </summary>
        public float InterpolationAlpha;
    }
}

