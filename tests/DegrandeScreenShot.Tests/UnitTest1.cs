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
