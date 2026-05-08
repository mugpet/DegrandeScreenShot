namespace DegrandeScreenShot.Core;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
	public int Left => X;

	public int Top => Y;

	public int Right => X + Width;

	public int Bottom => Y + Height;

	public bool HasArea => Width > 0 && Height > 0;

	public bool Contains(PixelPoint point)
	{
		return point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
	}

	public PixelRect Offset(int deltaX, int deltaY)
	{
		return new PixelRect(X + deltaX, Y + deltaY, Width, Height);
	}

	public static PixelRect FromPoints(PixelPoint anchor, PixelPoint current, bool constrainToSquare)
	{
		var deltaX = current.X - anchor.X;
		var deltaY = current.Y - anchor.Y;

		if (constrainToSquare)
		{
			var size = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
			deltaX = Math.Sign(deltaX == 0 ? 1 : deltaX) * size;
			deltaY = Math.Sign(deltaY == 0 ? 1 : deltaY) * size;
		}

		var left = deltaX >= 0 ? anchor.X : anchor.X + deltaX;
		var top = deltaY >= 0 ? anchor.Y : anchor.Y + deltaY;

		return new PixelRect(left, top, Math.Abs(deltaX), Math.Abs(deltaY));
	}
}

public enum SelectionInteraction
{
	None,
	Selecting,
	Moving,
}

public sealed class SelectionSession
{
	private PixelPoint _anchorPoint;
	private PixelPoint _currentPoint;
	private PixelPoint _moveOrigin;
	private PixelRect _moveRect;
	private bool _isPointerDown;

	public PixelRect? Selection { get; private set; }

	public SelectionInteraction Interaction { get; private set; }

	public void BeginSelection(PixelPoint anchor)
	{
		_anchorPoint = anchor;
		_currentPoint = anchor;
		_isPointerDown = true;
		Selection = new PixelRect(anchor.X, anchor.Y, 0, 0);
		Interaction = SelectionInteraction.Selecting;
	}

	public void UpdateSelection(PixelPoint current, bool constrainToSquare)
	{
		if (Interaction != SelectionInteraction.Selecting)
		{
			return;
		}

		_currentPoint = current;
		Selection = PixelRect.FromPoints(_anchorPoint, current, constrainToSquare);
	}

	public bool BeginMove(PixelPoint origin)
	{
		if (Selection is null)
		{
			return false;
		}

		_moveOrigin = origin;
		_moveRect = Selection.Value;
		_currentPoint = origin;
		Interaction = SelectionInteraction.Moving;
		return true;
	}

	public void UpdateMove(PixelPoint current)
	{
		if (Interaction != SelectionInteraction.Moving)
		{
			return;
		}

		_currentPoint = current;
		Selection = _moveRect.Offset(current.X - _moveOrigin.X, current.Y - _moveOrigin.Y);
	}

	public bool TryToggleMoveWhileDragging(bool shouldMove, PixelPoint pointerPosition)
	{
		if (!_isPointerDown || Selection is null)
		{
			return false;
		}

		if (shouldMove)
		{
			if (Interaction == SelectionInteraction.Moving)
			{
				UpdateMove(pointerPosition);
				return true;
			}

			return BeginMove(pointerPosition);
		}

		if (Interaction != SelectionInteraction.Moving)
		{
			return false;
		}

		_anchorPoint = new PixelPoint(Selection.Value.Left, Selection.Value.Top);
		_currentPoint = pointerPosition;
		Interaction = SelectionInteraction.Selecting;
		Selection = PixelRect.FromPoints(_anchorPoint, pointerPosition, false);
		return true;
	}

	public bool Nudge(int deltaX, int deltaY)
	{
		if (Selection is null)
		{
			return false;
		}

		Selection = Selection.Value.Offset(deltaX, deltaY);
		return true;
	}

	public void Complete()
	{
		_isPointerDown = false;

		if (Selection is { HasArea: false })
		{
			Selection = null;
		}

		Interaction = SelectionInteraction.None;
	}

	public void Clear()
	{
		_isPointerDown = false;
		Selection = null;
		Interaction = SelectionInteraction.None;
	}
}
