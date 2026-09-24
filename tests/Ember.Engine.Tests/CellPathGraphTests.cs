using System;
using System.IO;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class CellPathGraphTests
{
    [Fact]
    public void CellGraphRoundTripsStableNodeEdgeAndClearanceData()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "cell.paths.json");
            var cellId = Guid.NewGuid();
            var start = Guid.NewGuid();
            var target = Guid.NewGuid();
            var graph = new CellPathGraph
            {
                CellId = cellId,
                Kind = WorldCellKind.Interior,
                Nodes = new[]
                {
                    new CellPathNode(start, new NavigationPoint(1, 2, 3)),
                    new CellPathNode(target, new NavigationPoint(4, 5, 6))
                },
                Edges = new[] { new CellPathEdge(start, target, 0.65f) }
            };

            CellPathGraphFile.SaveAtomic(path, graph);
            var loaded = CellPathGraphFile.Load(path);

            Assert.Equal(cellId, loaded.CellId);
            Assert.Equal(WorldCellKind.Interior, loaded.Kind);
            Assert.Equal(graph.Nodes, loaded.Nodes);
            Assert.Equal(graph.Edges, loaded.Edges);
            Assert.Equal(0.65f, Assert.Single(loaded.Edges).ClearanceRadius);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsInvalidEdgesAndMissingRouteEndpoints()
    {
        var directory = TemporaryDirectory();
        var start = Guid.NewGuid();
        var target = Guid.NewGuid();
        var graph = new CellPathGraph
        {
            CellId = Guid.NewGuid(),
            Nodes = new[] { new CellPathNode(start, new NavigationPoint(0, 0, 0)) },
            Edges = new[] { new CellPathEdge(start, target, 0.5f) }
        };

        try
        {
            Assert.Throws<InvalidDataException>(() => graph.Validate());
            Assert.Throws<InvalidDataException>(() => CellPathGraphFile.SaveAtomic(
                Path.Combine(directory, "invalid.json"), graph));
            Assert.Throws<ArgumentException>(() => CellRouteSearch.FindShortestRoute(
                new CellPathGraph { CellId = Guid.NewGuid(), Nodes = graph.Nodes }, target, start));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindsShortestClearRouteAndReportsUnreachableTargets()
    {
        var start = Guid.NewGuid();
        var near = Guid.NewGuid();
        var alternate = Guid.NewGuid();
        var target = Guid.NewGuid();
        var graph = new CellPathGraph
        {
            CellId = Guid.NewGuid(),
            Nodes = new[]
            {
                new CellPathNode(start, new NavigationPoint(0, 0, 0)),
                new CellPathNode(near, new NavigationPoint(1, 0, 0)),
                new CellPathNode(alternate, new NavigationPoint(0, 1, 0)),
                new CellPathNode(target, new NavigationPoint(2, 0, 0))
            },
            Edges = new[]
            {
                new CellPathEdge(start, target, 0.1f),
                new CellPathEdge(start, near, 1f),
                new CellPathEdge(near, target, 1f),
                new CellPathEdge(start, alternate, 1f),
                new CellPathEdge(alternate, target, 1f)
            }
        };

        var route = CellRouteSearch.FindShortestRoute(graph, start, target, requiredClearanceRadius: 0.5f);

        Assert.NotNull(route);
        Assert.Equal(new[] { start, near, target }, route.NodeIds);
        Assert.Equal(2d, route.Distance, precision: 6);
        Assert.Null(CellRouteSearch.FindShortestRoute(graph, start, target, requiredClearanceRadius: 1.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CellRouteSearch.FindShortestRoute(graph, start, target, float.NaN));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-cell-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
