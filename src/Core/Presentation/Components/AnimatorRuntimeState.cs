namespace Ludots.Core.Presentation.Components
{
    public struct AnimatorRuntimeState
    {
        public const int NoState = -1;

        public int ControllerId;
        public int CurrentStateIndex;
        public int NextStateIndex;
        public float StateElapsedSeconds;
        public float TransitionElapsedSeconds;
        public float TransitionDurationSeconds;
        public bool Initialized;
        public int ReportedMissingControllerId;
        public int LastCompletedStateIndex;

        public readonly bool IsTransitioning => NextStateIndex != NoState;

        public static AnimatorRuntimeState Create(int controllerId)
        {
            return new AnimatorRuntimeState
            {
                ControllerId = controllerId,
                CurrentStateIndex = NoState,
                NextStateIndex = NoState,
                StateElapsedSeconds = 0f,
                TransitionElapsedSeconds = 0f,
                TransitionDurationSeconds = 0f,
                Initialized = false,
                ReportedMissingControllerId = 0,
                LastCompletedStateIndex = NoState,
            };
        }
    }
}
