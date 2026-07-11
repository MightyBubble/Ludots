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

        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);

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
        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);
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
        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);
        var massNavigationChunks = new HashSet<long>(board.LoadedChunks.ActiveChunkKeys);
        using WorldGridLoadedChunkContributor roadNetwork = board.LoadedChunksSource.AcquireContributor("road-network-cache-test");
        long remoteRoadChunk = GraphChunkKey.Pack(100, -100);

        roadNetwork.UpdateWindow(centerXcm: 50_000, centerYcm: -50_000, radiusCm: 0);
        simulation.UpdateStreamingWindow(new Vector2(
            simulation.FlowWorkAreaCenterXCm,
            simulation.FlowWorkAreaCenterYCm));

        Assert.That(board.LoadedChunks.IsLoaded(remoteRoadChunk), Is.True);
        Assert.That(simulation.LoadedChunkCount, Is.EqualTo(massNavigationChunks.Count),
            "MassNavigation evidence must report its contributor window, not the board-wide contributor union.");
        Assert.That(board.LoadedChunks.ActiveChunkKeys.Count, Is.GreaterThan(simulation.LoadedChunkCount));
        foreach (long chunkKey in massNavigationChunks)
        {
            Assert.That(board.LoadedChunks.IsLoaded(chunkKey), Is.True, $"MassNavigation cached chunk {chunkKey} must remain active.");
        }
    }

    [Test]
    public void Simulation_RebindingBoardReleasesPreviousBoardContribution()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        using var firstBoard = new GridBoard(
            new BoardId("first-binding"),
            "first-binding",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        using var secondBoard = new GridBoard(
            new BoardId("second-binding"),
            "second-binding",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

        simulation.BindBoardWorld(firstBoard.WorldSize, firstBoard.LoadedChunksSource);
        Assert.That(firstBoard.LoadedChunks.ActiveChunkKeys, Is.Not.Empty);

        simulation.BindBoardWorld(secondBoard.WorldSize, secondBoard.LoadedChunksSource);

        Assert.That(firstBoard.LoadedChunks.ActiveChunkKeys, Is.Empty);
        Assert.That(firstBoard.LoadedChunksSource.ContributorCount, Is.Zero);
        Assert.That(simulation.LoadedChunks, Is.SameAs(secondBoard.LoadedChunks));
        Assert.That(secondBoard.LoadedChunks.ActiveChunkKeys, Is.Not.Empty);
        Assert.That(secondBoard.LoadedChunksSource.ContributorCount, Is.EqualTo(1));
    }

    [Test]
    public void Simulation_RetainExpiryEvictsOnlyItsPreviousWindow()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        config.Streaming.RetainSeconds = 0f;
        config.Capacity.LoadedChunkCapacity = 64;
        using var board = new GridBoard(
            new BoardId("retained-window"),
            "retained-window",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
        using WorldGridLoadedChunkContributor roadNetwork = board.LoadedChunksSource.AcquireContributor("road-network-retain-test");
        long remoteRoadChunk = GraphChunkKey.Pack(100, -100);
        roadNetwork.SetLoaded(remoteRoadChunk, loaded: true);
        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);
        var previousWindow = new HashSet<long>(board.LoadedChunks.ActiveChunkKeys);
        previousWindow.Remove(remoteRoadChunk);

        simulation.BeginFrame(1f);
        simulation.UpdateStreamingWindow(new Vector2(50_000f, 50_000f));

        Assert.That(board.LoadedChunks.IsLoaded(remoteRoadChunk), Is.True);
        Assert.That(previousWindow, Is.Not.Empty);
        foreach (long chunkKey in previousWindow)
        {
            Assert.That(board.LoadedChunks.IsLoaded(chunkKey), Is.False, $"Expired MassNavigation chunk {chunkKey} must be released.");
        }
    }

    [Test]
    public void Simulation_StationaryWindowRemainsLoadedAfterRetainInterval()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        config.Streaming.RetainSeconds = 0f;
        using var board = new GridBoard(
            new BoardId("stationary-window"),
            "stationary-window",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);
        var currentWindow = new HashSet<long>(board.LoadedChunks.ActiveChunkKeys);
        simulation.BeginFrame(10f);
        simulation.UpdateStreamingWindow(new Vector2(
            simulation.FlowWorkAreaCenterXCm,
            simulation.FlowWorkAreaCenterYCm));

        Assert.That(currentWindow, Is.Not.Empty);
        Assert.That(board.LoadedChunks.ActiveChunkKeys, Is.EquivalentTo(currentWindow));
    }

    [Test]
    public void Simulation_RetainIntervalStartsWhenWindowIsLeft()
    {
        MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
        config.Streaming.RetainSeconds = 2f;
        config.Capacity.LoadedChunkCapacity = 128;
        using var board = new GridBoard(
            new BoardId("departure-retain-window"),
            "departure-retain-window",
            CreateBoardConfig(chunkSizeCells: 4, gridCellSizeCm: 125));
        var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

        simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource);
        var previousWindow = new HashSet<long>(board.LoadedChunks.ActiveChunkKeys);
        simulation.BeginFrame(10f);
        simulation.UpdateStreamingWindow(new Vector2(50_000f, 50_000f));

        foreach (long chunkKey in previousWindow)
        {
            Assert.That(board.LoadedChunks.IsLoaded(chunkKey), Is.True, $"Recently departed chunk {chunkKey} must be retained.");
        }

        simulation.BeginFrame(3f);
        simulation.UpdateStreamingWindow(new Vector2(50_000f, 50_000f));

        foreach (long chunkKey in previousWindow)
        {
            Assert.That(board.LoadedChunks.IsLoaded(chunkKey), Is.False, $"Expired departed chunk {chunkKey} must be released.");
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
            () => simulation.BindBoardWorld(board.WorldSize, board.LoadedChunksSource))!;

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
