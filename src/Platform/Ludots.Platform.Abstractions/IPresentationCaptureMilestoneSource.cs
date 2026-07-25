namespace Ludots.Platform.Abstractions;

public readonly record struct PresentationCaptureMilestoneSnapshot(
    string Id,
    int Order,
    uint Revision);

public interface IPresentationCaptureMilestoneSource
{
    PresentationCaptureMilestoneSnapshot Current { get; }

    bool TryResolveOrder(string milestoneId, out int order);
}
