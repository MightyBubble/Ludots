namespace DynamicNavBakeShowcaseMod.Runtime;

/// <summary>
/// Wait policy for squad deploy after RecomputePath may schedule a resident transition.
/// SynchronousDrain preserves existing UI/acceptance behavior; NonBlocking is for host-frame timelines.
/// </summary>
internal enum DynamicNavBakeShowcaseDeployWaitPolicy : byte
{
    SynchronousDrain = 0,
    NonBlocking = 1
}
