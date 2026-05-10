namespace DegrandeScreenShot.Core;

public static class ScrollCaptureStitcher
{
    private const double RequiredSmallOverlapMatchRatio = 0.82;
    private const double RequiredLargeOverlapMatchRatio = 0.75;
    private const double RequiredVeryLargeOverlapMatchRatio = 0.50;
    private const int LargeOverlapRowThreshold = 200;
    private const int VeryLargeOverlapRowThreshold = 800;

    public static VerticalOverlapMatch FindBestVerticalOverlap(
        IReadOnlyList<ulong> existingRows,
        IReadOnlyList<ulong> incomingRows,
        int minOverlapRows,
        int maxIncomingOffsetRows)
    {
        var bestMatch = VerticalOverlapMatch.None;
        var maxOffset = Math.Min(maxIncomingOffsetRows, Math.Max(0, incomingRows.Count - minOverlapRows));
        for (var incomingOffset = 0; incomingOffset <= maxOffset; incomingOffset++)
        {
            var match = FindVerticalOverlapMatch(existingRows, incomingRows, incomingOffset, minOverlapRows);
            if (match.OverlapRows <= bestMatch.OverlapRows)
            {
                continue;
            }

            bestMatch = match;
        }

        return bestMatch;
    }

    public static int FindDynamicTop(IReadOnlyList<ulong> beforeRows, IReadOnlyList<ulong> afterRows)
    {
        var limit = Math.Min(beforeRows.Count, afterRows.Count);
        for (var index = 0; index < limit; index++)
        {
            if (beforeRows[index] != afterRows[index])
            {
                return index;
            }
        }

        return limit;
    }

    public static int FindDynamicBottomExclusive(IReadOnlyList<ulong> beforeRows, IReadOnlyList<ulong> afterRows)
    {
        var limit = Math.Min(beforeRows.Count, afterRows.Count);
        for (var offset = 0; offset < limit; offset++)
        {
            var beforeIndex = beforeRows.Count - 1 - offset;
            var afterIndex = afterRows.Count - 1 - offset;
            if (beforeRows[beforeIndex] != afterRows[afterIndex])
            {
                return beforeIndex + 1;
            }
        }

        return beforeRows.Count - limit;
    }

    public static int FindVerticalOverlap(IReadOnlyList<ulong> existingRows, IReadOnlyList<ulong> incomingRows, int minOverlapRows)
    {
        return FindVerticalOverlapMatch(existingRows, incomingRows, 0, minOverlapRows).OverlapRows;
    }

    private static VerticalOverlapMatch FindVerticalOverlapMatch(IReadOnlyList<ulong> existingRows, IReadOnlyList<ulong> incomingRows, int incomingOffsetRows, int minOverlapRows)
    {
        var availableIncomingRows = incomingRows.Count - incomingOffsetRows;
        var maxOverlap = Math.Min(existingRows.Count, availableIncomingRows);
        for (var overlap = maxOverlap; overlap >= minOverlapRows; overlap--)
        {
            var startIndex = existingRows.Count - overlap;
            var matches = 0;
            for (var offset = 0; offset < overlap; offset++)
            {
                if (existingRows[startIndex + offset] == incomingRows[incomingOffsetRows + offset])
                {
                    matches++;
                }
            }

            var requiredMatchRatio = overlap switch
            {
                >= VeryLargeOverlapRowThreshold => RequiredVeryLargeOverlapMatchRatio,
                >= LargeOverlapRowThreshold => RequiredLargeOverlapMatchRatio,
                _ => RequiredSmallOverlapMatchRatio,
            };
            if (matches >= Math.Ceiling(overlap * requiredMatchRatio))
            {
                return new VerticalOverlapMatch(overlap, incomingOffsetRows, (double)matches / overlap);
            }
        }

        return VerticalOverlapMatch.None;
    }
}

public readonly record struct VerticalOverlapMatch(int OverlapRows, int IncomingOffsetRows, double MatchRatio)
{
    public VerticalOverlapMatch(int overlapRows, int incomingOffsetRows)
        : this(overlapRows, incomingOffsetRows, 0)
    {
    }

    public static VerticalOverlapMatch None => new(0, 0, 0);

    public int AppendStartRow => IncomingOffsetRows + OverlapRows;
}