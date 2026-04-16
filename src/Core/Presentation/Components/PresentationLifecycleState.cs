namespace Ludots.Core.Presentation.Components
{
    public struct PresentationLifecycleState
    {
        public bool Spawned;
        public bool PendingDestroy;
        public bool DestroyEventPublished;
    }
}
