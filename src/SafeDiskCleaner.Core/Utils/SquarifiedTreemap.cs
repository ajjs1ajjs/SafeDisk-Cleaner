namespace SafeDiskCleaner.Core.Utils;

/// <summary>An input leaf of a treemap: identifier plus non-negative weight.</summary>
public sealed record TreemapInput(string Id, double Value);

/// <summary>A laid-out rectangle in the coordinate space passed to <see cref="SquarifiedTreemap.Layout"/>.</summary>
public sealed record TreemapTile(string Id, double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap layout (Bruls, Huizing &amp; van Wijk, 2000).
/// Produces aspect-ratio-friendly rectangles: larger items first, rows grown
/// along the shorter side of the remaining rectangle while that improves the
/// worst item aspect ratio.
/// </summary>
public static class SquarifiedTreemap
{
    public static IReadOnlyList<TreemapTile> Layout(IReadOnlyList<TreemapInput> items, double width, double height)
    {
        var tiles = new List<TreemapTile>();
        if (items is not { Count: > 0 } || width <= 0 || height <= 0)
        {
            return tiles;
        }

        var ordered = items
            .Where(i => i.Value > 0 && !string.IsNullOrEmpty(i.Id))
            .OrderByDescending(i => i.Value)
            .ToList();
        if (ordered.Count == 0)
        {
            return tiles;
        }

        var scale = width * height / ordered.Sum(i => i.Value);
        var queue = new Queue<(string Id, double Area)>(
            ordered.Select(i => (i.Id, i.Value * scale)));

        double x = 0, y = 0, w = width, h = height;
        var row = new List<(string Id, double Area)>();
        var rowAreas = new List<double>();

        while (queue.Count > 0)
        {
            var side = Math.Min(w, h);

            row.Clear();
            rowAreas.Clear();
            var first = queue.Dequeue();
            row.Add(first);
            rowAreas.Add(first.Area);

            // Grow the row while adding the next item does not worsen the worst ratio.
            while (queue.Count > 0)
            {
                var nextArea = queue.Peek().Area;
                var currentWorst = WorstRatio(rowAreas, side);
                rowAreas.Add(nextArea);
                var mergedWorst = WorstRatio(rowAreas, side);
                if (mergedWorst <= currentWorst)
                {
                    row.Add(queue.Dequeue());
                }
                else
                {
                    rowAreas.RemoveAt(rowAreas.Count - 1);
                    break;
                }
            }

            // Lay the row along the shorter side: the strip spans the short side
            // completely and its thickness cuts into the long side.
            var rowArea = rowAreas.Sum();
            var offset = 0.0;
            if (h <= w)
            {
                // vertical column on the left: spans full height, thickness along X
                var thickness = rowArea / h;
                foreach (var (id, area) in row)
                {
                    var len = area / thickness;
                    tiles.Add(new TreemapTile(id, x, y + offset, thickness, len));
                    offset += len;
                }

                x += thickness;
                w -= thickness;
            }
            else
            {
                // horizontal row on top: spans full width, thickness along Y
                var thickness = rowArea / w;
                foreach (var (id, area) in row)
                {
                    var len = area / thickness;
                    tiles.Add(new TreemapTile(id, x + offset, y, len, thickness));
                    offset += len;
                }

                y += thickness;
                h -= thickness;
            }
        }

        return tiles;
    }

    /// <summary>Worst (largest) aspect ratio among items of a row laid into a strip of side <paramref name="length"/>.</summary>
    internal static double WorstRatio(IReadOnlyList<double> areas, double length)
    {
        if (areas.Count == 0 || length <= 0)
        {
            return double.MaxValue;
        }

        var sum = areas.Sum();
        if (sum <= 0)
        {
            return double.MaxValue;
        }

        var thickness = sum / length;
        var worst = 0.0;
        foreach (var area in areas)
        {
            if (area <= 0)
            {
                continue;
            }

            var itemLength = area / thickness;
            var ratio = Math.Max(thickness / itemLength, itemLength / thickness);
            worst = Math.Max(worst, ratio);
        }

        return worst;
    }
}
