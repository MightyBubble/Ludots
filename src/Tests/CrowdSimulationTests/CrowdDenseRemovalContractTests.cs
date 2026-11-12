using System.Reflection;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// Separation / Calm 按下标对齐稠密行并活过 GatherUnits。
/// 退场只有 ReleaseDense,同时用 MoveAvoidanceRow 搬走分离行。公开移除仍然只有 Clear。
/// </summary>
public sealed class CrowdDenseRemovalContractTests
{
    [Test]
    public void PublicRemoval_IsOnlyClear()
    {
        MethodInfo[] unitMethods = typeof(CrowdSimUnits).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.That(unitMethods.Any(method => method.Name == nameof(CrowdSimUnits.Clear)), Is.True);

        foreach (MethodInfo method in unitMethods)
        {
            bool removalName =
                method.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Destroy", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Swap", StringComparison.OrdinalIgnoreCase);
            Assert.That(removalName, Is.False, $"{method.Name} would be a public removal path beside Clear.");
        }

        Assert.That(typeof(CrowdSimSession).GetMethod("RemoveDense"), Is.Null);
        Assert.That(typeof(CrowdMovementKernel).GetMethod("CarryPushOnSwapRemove"), Is.Null);
        Assert.That(
            typeof(CrowdSimUnits).GetMethod("ReleaseDense", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Not.Null);
        Assert.That(
            typeof(CrowdMovementKernel).GetMethod("MoveAvoidanceRow", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Not.Null);
    }
}
