using FluentAssertions;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.Tests;

public sealed class SquarifiedTreemapTests
{
    [Fact]
    public void EmptyInputs_ProduceNoTiles()
    {
        SquarifiedTreemap.Layout([], 100, 100).Should().BeEmpty();
        SquarifiedTreemap.Layout([new TreemapInput("a", 0)], 100, 100).Should().BeEmpty();
        SquarifiedTreemap.Layout([new TreemapInput("a", 10)], 0, 100).Should().BeEmpty();
    }

    [Fact]
    public void SingleItem_FillsWholeArea()
    {
        var tiles = SquarifiedTreemap.Layout([new TreemapInput("only", 5)], 400, 200);

        tiles.Should().ContainSingle();
        tiles[0].Width.Should().BeApproximately(400, 1e-6);
        tiles[0].Height.Should().BeApproximately(200, 1e-6);
    }

    [Fact]
    public void Areas_AreProportional_ToValues()
    {
        // classic Bruls "powers of two" input
        var items = Enumerable.Range(1, 6).Select(i => new TreemapInput($"i{i}", (double)(1 << i))).ToList();

        var tiles = SquarifiedTreemap.Layout(items, 640, 480);

        tiles.Should().HaveCount(6);
        foreach (var tile in tiles)
        {
            var value = items.First(i => i.Id == tile.Id).Value;
            var expectedArea = value / items.Sum(i => i.Value) * 640 * 480;
            (tile.Width * tile.Height).Should().BeApproximately(expectedArea, expectedArea * 0.01,
                "each tile area must match its share within 1%");
        }
    }

    [Fact]
    public void Tiles_StayWithinBounds_AndCoverTheCanvas()
    {
        var items = Enumerable.Range(1, 12).Select(i => new TreemapInput($"n{i}", 37.0 * i % 91 + 7)).ToList();

        var tiles = SquarifiedTreemap.Layout(items, 500, 300);

        tiles.Select(t => t.Id).Should().BeEquivalentTo(items.Select(i => i.Id), "every input gets a tile");
        foreach (var t in tiles)
        {
            t.X.Should().BeGreaterThanOrEqualTo(0);
            t.Y.Should().BeGreaterThanOrEqualTo(0);
            (t.X + t.Width).Should().BeLessThanOrEqualTo(500 + 1e-6);
            (t.Y + t.Height).Should().BeLessThanOrEqualTo(300 + 1e-6);
        }

        var totalTileArea = tiles.Sum(t => t.Width * t.Height);
        totalTileArea.Should().BeApproximately(500.0 * 300.0, 500.0 * 300.0 * 0.001, "tiles must cover the canvas");
    }

    [Fact]
    public void WorstRatio_StaysReasonable_ForTypicalData()
    {
        var items = Enumerable.Range(1, 20).Select(i => new TreemapInput($"f{i}", 20 + 11 * i)).ToList();
        var tiles = SquarifiedTreemap.Layout(items, 800, 600);

        var worst = tiles.Max(t => Math.Max(t.Width / t.Height, t.Height / t.Width));
        worst.Should().BeLessThanOrEqualTo(12.0,
            "squarified layout keeps tiles reasonably square for moderately-spread inputs");
    }
}
