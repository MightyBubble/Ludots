namespace Ludots.Core.Presentation
{
    public interface IBenchmarkSceneController
    {
        bool IsActive { get; }
        bool SupportsScatterControl { get; }
        bool IsCleanPerformanceScene { get; }
        bool SuppressHostDiagnosticUi { get; }
        bool SuppressHostDebugGuides { get; }
        int ScatterMin { get; }
        int ScatterMax { get; }
        int ScatterTarget { get; }
        int ScatterAppliedTotal { get; }
        void SetScatterTargetFromRatio(float ratio);
        void ApplyScatterTarget();
        void ApplyScatterLayout(int total);
    }
}
