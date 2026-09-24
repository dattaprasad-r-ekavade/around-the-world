using System;
using System.IO;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldPathNetworkTests
{
    [Fact]
    public void RouteUsesLocalLegsAndExplicitExteriorAndDoorTransitionsAfterReload()
    {
        var outsideWestId = Guid.NewGuid();
        var outsideEastId = Guid.NewGuid();
        var interiorId = Guid.NewGuid();
        var startId = Guid.NewGuid();
        var boundaryWestId = Guid.NewGuid();
        var boundaryEastId = Guid.NewGuid();
        var eastDoorId = Guid.NewGuid();
        var interiorSpawnId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var doorInstanceId = Guid.NewGuid();
        var west = Graph(outsideWestId,
            new CellPathNode(startId, new NavigationPoint(0, 0, 0)),
            new CellPathNode(boundaryWestId, new NavigationPoint(2, 0, 0)),
            new CellPathEdge(boundaryWestId, startId, 0.75f), WorldCellKind.Exterior);
        var east = Graph(outsideEastId,
            new CellPathNode(boundaryEastId, new NavigationPoint(10, 0, 0)),
            new CellPathNode(eastDoorId, new NavigationPoint(12, 0, 0)),
            new CellPathEdge(boundaryEastId, eastDoorId, 0.75f), WorldCellKind.Exterior);
        var interior = Graph(interiorId,
            new CellPathNode(interiorSpawnId, new NavigationPoint(0, 0, 0)),
            new CellPathNode(targetId, new NavigationPoint(2, 0, 0)),
            new CellPathEdge(interiorSpawnId, targetId, 0.75f), WorldCellKind.Interior);
        var network = new WorldPathNetwork
        {
            Cells = new[] { west, east, interior },
            Connections = new[]
            {
                new WorldPathConnection
                {
                    Id = Guid.NewGuid(),
                    From = new WorldPathNodeRef(outsideWestId, boundaryWestId),
                    To = new WorldPathNodeRef(outsideEastId, boundaryEastId),
                    Kind = WorldPathConnectionKind.ExteriorBoundary,
                    ClearanceRadius = 0.75f
                },
                new WorldPathConnection
                {
                    Id = Guid.NewGuid(),
                    From = new WorldPathNodeRef(outsideEastId, eastDoorId),
                    To = new WorldPathNodeRef(interiorId, interiorSpawnId),
                    Kind = WorldPathConnectionKind.Door,
                    ClearanceRadius = 0.75f,
                    DoorInstanceId = doorInstanceId
                }
            }
        };
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world-paths.json");
            WorldPathNetworkFile.SaveAtomic(path, network);
            var loaded = WorldPathNetworkFile.Load(path, new[] { west, east, interior });
            var route = WorldRouteSearch.FindShortestRoute(loaded,
                new WorldPathNodeRef(outsideWestId, startId),
                new WorldPathNodeRef(interiorId, targetId), requiredClearanceRadius: 0.5f);

            Assert.NotNull(route);
            Assert.Equal(3, route.Legs.Count);
            Assert.Equal(outsideWestId, route.Legs[0].CellId);
            Assert.Equal(new[] { startId, boundaryWestId }, route.Legs[0].NodeIds);
            Assert.Equal(WorldPathConnectionKind.ExteriorBoundary, route.Legs[0].TransitionToNext!.Kind);
            Assert.Equal(outsideEastId, route.Legs[1].CellId);
            Assert.Equal(new[] { boundaryEastId, eastDoorId }, route.Legs[1].NodeIds);
            var doorTransition = route.Legs[1].TransitionToNext;
            Assert.NotNull(doorTransition);
            Assert.Equal(WorldPathConnectionKind.Door, doorTransition!.Kind);
            Assert.Equal(doorInstanceId, doorTransition.DoorInstanceId);
            Assert.Equal(interiorId, route.Legs[2].CellId);
            Assert.Equal(new[] { interiorSpawnId, targetId }, route.Legs[2].NodeIds);
            Assert.Null(route.Legs[2].TransitionToNext);
            Assert.Equal(6d, route.Distance, precision: 6);
            Assert.Null(WorldRouteSearch.FindShortestRoute(loaded,
                new WorldPathNodeRef(outsideWestId, startId),
                new WorldPathNodeRef(interiorId, targetId), requiredClearanceRadius: 0.8f));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WorldConnectionRejectsMissingNodesAndInvalidDoorReferences()
    {
        var cellA = Guid.NewGuid();
        var cellB = Guid.NewGuid();
        var nodeA = Guid.NewGuid();
        var graphA = Graph(cellA, new CellPathNode(nodeA, new NavigationPoint(0, 0, 0)), WorldCellKind.Exterior);
        var graphB = Graph(cellB, new CellPathNode(Guid.NewGuid(), new NavigationPoint(0, 0, 0)), WorldCellKind.Exterior);
        var missingNodeLink = new WorldPathNetwork
        {
            Cells = new[] { graphA, graphB },
            Connections = new[]
            {
                new WorldPathConnection
                {
                    Id = Guid.NewGuid(),
                    From = new WorldPathNodeRef(cellA, nodeA),
                    To = new WorldPathNodeRef(cellB, Guid.NewGuid()),
                    Kind = WorldPathConnectionKind.ExteriorBoundary,
                    ClearanceRadius = 0.5f
                }
            }
        };
        Assert.Throws<InvalidDataException>(() => missingNodeLink.Validate());

        var interiorGraph = Graph(cellB, graphB.Nodes[0], WorldCellKind.Interior);
        var noDoorId = new WorldPathNetwork
        {
            Cells = new[] { graphA, interiorGraph },
            Connections = new[]
            {
                new WorldPathConnection
                {
                    Id = Guid.NewGuid(),
                    From = new WorldPathNodeRef(cellA, nodeA),
                    To = new WorldPathNodeRef(cellB, interiorGraph.Nodes[0].Id),
                    Kind = WorldPathConnectionKind.Door,
                    ClearanceRadius = 0.5f
                }
            }
        };
        Assert.Throws<InvalidDataException>(() => noDoorId.Validate());
    }

    private static CellPathGraph Graph(Guid cellId, CellPathNode first, CellPathNode second,
        CellPathEdge edge, WorldCellKind kind) => new()
    {
        CellId = cellId,
        Kind = kind,
        Nodes = new[] { first, second },
        Edges = new[] { edge }
    };

    private static CellPathGraph Graph(Guid cellId, CellPathNode node, WorldCellKind kind) => new()
    {
        CellId = cellId,
        Kind = kind,
        Nodes = new[] { node }
    };

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-world-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
