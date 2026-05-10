using DegrandeScreenShot.Core;

namespace DegrandeScreenShot.Tests;

public class SelectionSessionTests
{
    [Fact]
    public void CreatesSquareSelectionWhenShiftConstraintIsApplied()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(30, 20), true);
        session.Complete();

        Assert.Equal(new PixelRect(10, 10, 20, 20), session.Selection);
    }

    [Fact]
    public void MovesSelectionByArrowNudge()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(25, 40));
        session.UpdateSelection(new PixelPoint(75, 80), false);
        session.Complete();
        session.Nudge(1, -1);

        Assert.Equal(new PixelRect(26, 39, 50, 40), session.Selection);
    }

    [Fact]
    public void MovesExistingSelectionWhenDragStartsInsideSelection()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(50, 40), false);
        session.Complete();

        var started = session.BeginMove(new PixelPoint(20, 20));
        session.UpdateMove(new PixelPoint(26, 28));
        session.Complete();

        Assert.True(started);
        Assert.Equal(new PixelRect(16, 18, 40, 30), session.Selection);
    }

    [Fact]
    public void SwitchesFromResizingToMovingAndBackDuringSameDrag()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(50, 40), false);

        var moveStarted = session.TryToggleMoveWhileDragging(true, new PixelPoint(20, 20));
        session.UpdateMove(new PixelPoint(25, 25));
        var resizeStarted = session.TryToggleMoveWhileDragging(false, new PixelPoint(60, 55));
        session.UpdateSelection(new PixelPoint(60, 55), false);
        session.Complete();

        Assert.True(moveStarted);
        Assert.True(resizeStarted);
        Assert.Equal(new PixelRect(15, 15, 45, 40), session.Selection);
    }

    [Fact]
    public void StartsMoveImmediatelyEvenWhenPointerHasDraggedPastSelectionBounds()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(60, 60));
        session.UpdateSelection(new PixelPoint(20, 20), false);

        var moveStarted = session.TryToggleMoveWhileDragging(true, new PixelPoint(10, 10));
        session.UpdateMove(new PixelPoint(0, 0));
        session.Complete();

        Assert.True(moveStarted);
        Assert.Equal(new PixelRect(10, 10, 40, 40), session.Selection);
    }
}

public class ScrollCaptureStitcherTests
{
    [Fact]
    public void FindsDynamicTopAndBottomByIgnoringStaticChrome()
    {
        ulong[] beforeRows = [1, 1, 10, 11, 12, 13, 14, 90, 90];
        ulong[] afterRows = [1, 1, 11, 12, 13, 14, 15, 90, 90];

        var dynamicTop = ScrollCaptureStitcher.FindDynamicTop(beforeRows, afterRows);
        var dynamicBottom = ScrollCaptureStitcher.FindDynamicBottomExclusive(beforeRows, afterRows);

        Assert.Equal(2, dynamicTop);
        Assert.Equal(7, dynamicBottom);
    }

    [Fact]
    public void FindsLargestTailToHeadOverlap()
    {
        ulong[] existingRows = [10, 11, 12, 13, 14, 15];
        ulong[] incomingRows = [13, 14, 15, 16, 17];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2);

        Assert.Equal(3, overlap);
    }

    [Fact]
    public void ReturnsZeroWhenNoSufficientOverlapExists()
    {
        ulong[] existingRows = [10, 11, 12, 13];
        ulong[] incomingRows = [98, 99, 100, 101];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2);

        Assert.Equal(0, overlap);
    }

    [Fact]
    public void AllowsSmallRowDifferencesWithinOverlap()
    {
        ulong[] existingRows = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19];
        ulong[] incomingRows = [12, 999, 14, 15, 16, 17, 18, 19, 20, 21];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 4);

        Assert.Equal(8, overlap);
    }

    [Fact]
    public void FindsOverlapAfterStickyRowsAtIncomingTop()
    {
        ulong[] existingRows = [100, 101, 102, 103, 104, 105, 106, 107];
        ulong[] incomingRows = [900, 901, 104, 105, 106, 107, 108, 109];

        var match = ScrollCaptureStitcher.FindBestVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2, maxIncomingOffsetRows: 3);

        Assert.Equal(4, match.OverlapRows);
        Assert.Equal(2, match.IncomingOffsetRows);
        Assert.Equal(6, match.AppendStartRow);
    }

    [Fact]
    public void AllowsBrowserSeamWithSmallRowDifferences()
    {
        var existingRows = Enumerable.Range(0, 180).Select(index => (ulong)(1000 + index)).ToArray();
        var incomingRows = new ulong[180];

        Array.Copy(existingRows, 68, incomingRows, 0, 112);
        for (var index = 0; index < 112; index += 6)
        {
            incomingRows[index] = (ulong)(9000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 49);

        Assert.Equal(112, overlap);
    }

    [Fact]
    public void AllowsLargeBrowserSeamWithRepeatedVisualDifferences()
    {
        var existingRows = Enumerable.Range(0, 1300).Select(index => (ulong)(10000 + index)).ToArray();
        var incomingRows = new ulong[1300];

        Array.Copy(existingRows, 299, incomingRows, 0, 1001);
        for (var index = 0; index < 1001; index += 5)
        {
            incomingRows[index] = (ulong)(50000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 61);

        Assert.Equal(1001, overlap);
    }

    [Fact]
    public void AllowsVeryLargeBrowserSeamWithHeavyRerendering()
    {
        var existingRows = Enumerable.Range(0, 1300).Select(index => (ulong)(20000 + index)).ToArray();
        var incomingRows = new ulong[1300];

        Array.Copy(existingRows, 299, incomingRows, 0, 1001);
        for (var index = 0; index < 462; index++)
        {
            incomingRows[index] = (ulong)(70000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 61);

        Assert.Equal(1001, overlap);
    }
}
