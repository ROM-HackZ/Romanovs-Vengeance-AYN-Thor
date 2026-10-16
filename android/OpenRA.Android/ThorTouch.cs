using System;
using Android.Views;
using OpenRA.Platforms.Android;

namespace OpenRA.AndroidApp
{
	// A finger selects; two fingers manipulate the map. Defer the initial click
	// so adding a second finger never sends an order at the first finger's spot.
	sealed class ThorTouch : IDisposable
	{
		readonly ThorController controller;
		MotionEvent first;
		bool gestures, multi, dragging;
		float centerX, centerY, span, travel, scaleTravel;
		long started;
		public ThorTouch(ThorController controller) { this.controller = controller; }

		public void Handle(MotionEvent e, int width, int height)
		{
			if (e.ActionMasked == MotionEventActions.Down)
			{
				Cancel(); controller.Touch();
				gestures = controller.Snapshot.Battle && !controller.Snapshot.InMenu;
				if (!gestures) { AndroidPlatformWindow.EnqueueMotion(e); return; }
				first = MotionEvent.Obtain(e);
				multi = dragging = false; travel = scaleTravel = 0; started = e.EventTime;
				return;
			}
			if (!gestures) { AndroidPlatformWindow.EnqueueMotion(e); return; }
			if (e.ActionMasked == MotionEventActions.PointerDown && e.PointerCount >= 2)
			{
				multi = true;
				AndroidPlatformWindow.ClearTouchState();
				controller.Execute("cancel-touch");
				Measure(e, out centerX, out centerY, out span);
				return;
			}
			if (e.ActionMasked == MotionEventActions.Move)
			{
				if (multi)
				{
					if (e.PointerCount < 2) return;
					Measure(e, out var x, out var y, out var distance);
					var dx = (x - centerX) / Math.Max(1, width); var dy = (y - centerY) / Math.Max(1, height);
					var zoom = span > 24 && distance > 24 ? (float)Math.Log(distance / span) : 0;
					travel += Math.Abs(dx) + Math.Abs(dy); scaleTravel += Math.Abs(zoom);
					controller.Gesture(dx, dy, zoom);
					centerX = x; centerY = y; span = distance;
				}
				else if (first != null)
				{
					var distance = Math.Abs(e.GetX() - first.GetX()) + Math.Abs(e.GetY() - first.GetY());
					if (!dragging && distance > 12 && controller.Snapshot.Targeting is "UnitOrderGenerator" or "SelectDirectionalTarget")
					{ AndroidPlatformWindow.EnqueueMotion(first); dragging = true; }
					AndroidPlatformWindow.EnqueueMotion(e);
				}
				return;
			}
			if (e.ActionMasked == MotionEventActions.Up)
			{
				if (multi)
				{
					if (travel < .025f && scaleTravel < .10f && e.EventTime - started < 450) controller.Execute("deselect");
				}
				else
				{
					if (!dragging)
					{
						using var down = MotionEvent.Obtain(e); down.Action = MotionEventActions.Down;
						AndroidPlatformWindow.EnqueueMotion(down);
					}
					AndroidPlatformWindow.EnqueueMotion(e);
				}
				first?.Dispose(); first = null;
				multi = dragging = false;
				return;
			}
			if (e.ActionMasked == MotionEventActions.Cancel) Cancel();
		}

		static void Measure(MotionEvent e, out float x, out float y, out float span)
		{
			x = (e.GetX(0) + e.GetX(1)) / 2; y = (e.GetY(0) + e.GetY(1)) / 2;
			var dx = e.GetX(0) - e.GetX(1); var dy = e.GetY(0) - e.GetY(1);
			span = (float)Math.Sqrt(dx * dx + dy * dy);
		}

		public void Cancel()
		{
			var active = first != null || dragging || multi;
			first?.Dispose(); first = null;
			AndroidPlatformWindow.ClearTouchState();
			if (active) controller.Execute("cancel-touch");
			multi = dragging = false;
		}
		public void Dispose() => Cancel();
	}
}
