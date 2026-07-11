using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Board;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.GraphWorld;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationStreamingOwnershipTests
{
    [Test]
    public void GridBoard_OwnsIndependentWorldGridLoadedChunks()
    {
        BoardConfig config = CreateBoardConfig(chunkSizeCells: 32, gridCellSizeCm: 200);
        using var first = new GridBoard(new BoardId("first"), "first", config);
        using var second = new GridBoard(new BoardId("second"), "second", config);

        Assert.That(first.LoadedChunks, Is.TypeOf<WorldGridLoadedChunks>());
        Assert.That(((WorldGridLoadedChunks)first.LoadedChunks).ChunkSizeCm, Is.EqualTo(6400));
        Assert.That(second.LoadedChunks, Is.TypeOf<WorldGridLoadedChunks>());
        Assert.That(second.LoadedChunks, Is.Not.SameAs(first.LoadedChunks));
    }

    [Test]
    public void Simulation_BindsBoardOwnedLoadedChunksAndSubmitsItsStreamingWindow()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        using var board = new GridBoard(
            new BoardId("mass_navigation"),
            "mass_navigation",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunks);

        Assert.That(simulation.LoadedChunks, Is.SameAs(board.LoadedChunks));
        Assert.That(simulation.LoadedChunkCount, Is.GreaterThan(0));
    }

    [Test]
    public void Simulation_ReleasingMassNavigationWindowPreservesRoadNetworkContribution()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        using var board = new GridBoard(
            new BoardId("combined"),
            "combined",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
        using WorldGridLoadedChunkContributor roadNetwork = board.LoadedChunksSource.AcquireContributor("road-network-test");
        long remoteRoadChunk = GraphChunkKey.Pack(100, -100);

        roadNetwork.SetLoaded(remoteRoadChunk, loaded: true);
        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunks);
        simulation.ReleaseStreamingWindow();

        Assert.That(board.LoadedChunks.IsLoaded(remoteRoadChunk), Is.True);
        Assert.That(board.LoadedChunks.ActiveChunkKeys, Is.EquivalentTo(new[] { remoteRoadChunk }));
    }

    [Test]
    public void Simulation_SameWindowCacheRemainsValidAfterRoadNetworkMovesItsWindow()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        using var board = new GridBoard(
            new BoardId("cached-window"),
            "cached-window",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunks);
        var massNavigationChunks = new HashSet<long>(board.LoadedChunks.ActiveChunkKeys);
        using WorldGridLoadedChunkContributor roadNetwork = board.LoadedChunksSource.AcquireContributor("road-network-cache-test");
        long remoteRoadChunk = GraphChunkKey.Pack(100, -100);

        roadNetwork.UpdateWindow(centerXcm: 50_000, centerYcm: -50_000, radiusCm: 0);
        simulation.UpdateStreamingWindow(new Vector2(
            simulation.FlowWorkAreaCenterXCm,
            simulation.FlowWorkAreaCenterYCm));

        Assert.That(board.LoadedChunks.IsLoaded(remoteRoadChunk), Is.True);
        foreach (long chunkKey in massNavigationChunks)
        {
            Assert.That(board.LoadedChunks.IsLoaded(chunkKey), Is.True, $"MassNavigation cached chunk {chunkKey} must remain active.");
        }
    }

    [Test]
    public void Simulation_RejectsBoardChunkSizeThatDisagreesWithStreamingConfig()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        using var board = new GridBoard(
            new BoardId("invalid"),
            "invalid",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 150));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => simulation.BindBoardWorld(board.WorldSize, board.LoadedChunks))!;

        Assert.That(error.Message, Does.Contain("streaming chunk size"));
        Assert.That(error.Message, Does.Contain("500"));
        Assert.That(error.Message, Does.Contain("600"));
    }

    private static BoardConfig CreateBoardConfig(int chunkSizeCells, int gridCellSizeCm)
    {
        return new BoardConfig
        {
            Name = "default",
            SpatialType = "Grid",
            WidthInMacroTiles = 10,
            HeightInMacroTiles = 10,
            GridCellSizeCm = gridCellSizeCm,
            ChunkSizeCells = chunkSizeCells,
        };
    }
}
