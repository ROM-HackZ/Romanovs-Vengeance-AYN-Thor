using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace OpenRA.AndroidApp
{
	sealed class ThorArtwork : IDisposable
	{
		readonly Dictionary<string, (ThorController.IconFrame Frame, Bitmap Bitmap)> images = new();
		int epoch = -1;
		public void Begin(int value) { if (epoch != value) { Dispose(); epoch = value; } }
		public Bitmap Get(ThorController controller, string key)
		{
			if (key == "" || !controller.Icons.TryGetValue(key, out var frame)) return null;
			if (images.TryGetValue(key, out var cached) && ReferenceEquals(cached.Frame, frame)) return cached.Bitmap;
			cached.Bitmap?.Dispose();
			var bitmap = Bitmap.CreateBitmap(frame.Pixels, frame.Width, frame.Height, Bitmap.Config.Argb8888);
			images[key] = (frame, bitmap); return bitmap;
		}
		public void Dispose() { foreach (var value in images.Values) value.Bitmap.Dispose(); images.Clear(); }
	}

	static class ThorTheme
	{
		public static readonly Color Background = Color.Rgb(11, 16, 23), Panel = Color.Rgb(24, 33, 45), Border = Color.Rgb(53, 70, 86);
		public static readonly Color Gold = Color.Rgb(240, 194, 101), Muted = Color.Rgb(156, 177, 194), Green = Color.Rgb(104, 212, 162), Red = Color.Rgb(245, 113, 108);
		public static readonly Typeface Face = Typeface.Create("sans-serif-medium", TypefaceStyle.Normal);
		public static void Text(Canvas c, Paint p, string text, float x, float y, float size, Color color, Paint.Align align = null)
		{
			p.Color = color; p.TextSize = size; p.SetTypeface(Face); p.TextAlign = align ?? Paint.Align.Left; p.SetStyle(Paint.Style.Fill);
			c.DrawText(text ?? "", x, y, p);
		}
		public static void Lines(Canvas c, Paint p, string text, RectF bounds, float size, Color color, int lines = 2)
		{
			c.Save(); c.ClipRect(bounds); p.TextSize = size; p.SetTypeface(Face);
			var words = (text ?? "").Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var line = ""; var row = 0; var y = bounds.Top + size;
			for (var i = 0; i < words.Length; i++)
			{
				var next = line.Length == 0 ? words[i] : line + " " + words[i];
				if (p.MeasureText(next) <= bounds.Width()) { line = next; continue; }
				if (row + 1 >= lines) { line = next + "…"; break; }
				Text(c, p, line, bounds.Left, y, size, color); y += size + 3; row++; line = words[i];
			}
			if (p.MeasureText(line) > bounds.Width())
			{
				while (line.Length > 1 && p.MeasureText(line + "…") > bounds.Width()) line = line[..^1];
				line = line.TrimEnd(' ', '…') + "…";
			}
			if (line.Length > 0 && row < lines) Text(c, p, line, bounds.Left, y, size, color);
			c.Restore();
		}
		public static void Box(Canvas c, Paint p, RectF bounds, bool selected = false, bool enabled = true)
		{
			p.SetStyle(Paint.Style.Fill); p.Color = selected ? Color.Rgb(85, 62, 31) : enabled ? Panel : Color.Rgb(19, 25, 33);
			c.DrawRoundRect(bounds, 12, 12, p);
			p.SetStyle(Paint.Style.Stroke); p.StrokeWidth = selected ? 3 : 1.5f; p.Color = selected ? Gold : Border;
			c.DrawRoundRect(bounds, 12, 12, p); p.SetStyle(Paint.Style.Fill);
		}
		public static void Bar(Canvas c, Paint p, RectF bounds, float progress, Color color)
		{
			p.Color = Color.Rgb(8, 13, 18); c.DrawRoundRect(bounds, 3, 3, p);
			p.Color = color; c.DrawRoundRect(new RectF(bounds.Left, bounds.Top, bounds.Left + bounds.Width() * Math.Clamp(progress, 0, 1), bounds.Bottom), 3, 3, p);
		}
		public static void Icon(Canvas c, Paint p, Bitmap bitmap, RectF bounds)
		{
			if (bitmap == null) return;
			var scale = Math.Min(bounds.Width() / bitmap.Width, bounds.Height() / bitmap.Height);
			var w = bitmap.Width * scale; var h = bitmap.Height * scale;
			p.Color = Color.White; p.FilterBitmap = false;
			c.DrawBitmap(bitmap, null, new RectF(bounds.CenterX() - w / 2, bounds.CenterY() - h / 2, bounds.CenterX() + w / 2, bounds.CenterY() + h / 2), p);
		}
		public static string Symbol(string id) => id switch { "deploy" => "↓", "attack" => "✚", "stop" => "■", "same" => "×", "center" => "◎", "repair" => "+", "sell" => "$", "deselect" => "B", _ => "•" };
	}

	sealed class ThorDeck : View
	{
		readonly ThorController controller;
		readonly Paint paint = new(PaintFlags.AntiAlias);
		readonly ThorArtwork artwork = new();
		readonly List<(RectF Bounds, string Id, bool Enabled)> hits = new();
		string pressed;
		float pressX, pressY;
		long pressTime;
		bool holdReady;
		int page, filter, lastEpoch = -1, lastTab = -1;
		RectF radarBounds = new();
		Bitmap radarBitmap;
		ThorController.RadarFrame lastRadar;
		public ThorDeck(Context context, ThorController controller) : base(context) { this.controller = controller; }

		protected override void OnDraw(Canvas canvas)
		{
			base.OnDraw(canvas); canvas.DrawColor(ThorTheme.Background);
			canvas.Save(); canvas.Scale(Width / 1240f, Height / 1080f); hits.Clear();
			var s = controller.Snapshot; artwork.Begin(s.Epoch);
			if (lastEpoch != s.Epoch || lastTab != s.DeckTab) { page = 0; filter = 0; pressed = null; lastEpoch = s.Epoch; lastTab = s.DeckTab; }
			paint.Color = Color.Rgb(150, 43, 39); canvas.DrawRect(0, 0, 1240, 5, paint);
			Text(canvas, "ROMANOV’S VENGEANCE", 28, 43, 27, ThorTheme.Gold);
			Text(canvas, "THOR", 1208, 42, 20, ThorTheme.Muted, Paint.Align.Right);
			Text(canvas, s.Battle ? "$" + s.Cash.ToString("N0") : "NATIVE ANDROID", 28, 88, 31, Color.White);
			Text(canvas, s.Battle ? s.Faction.ToUpperInvariant() : "SKIRMISH · SINGLEPLAYER", 370, 84, 23, ThorTheme.Muted);
			Text(canvas, s.Battle ? $"POWER {s.PowerUsed} / {s.PowerTotal}" : "READY TO PLAY", 1208, 84, 22,
				s.PowerUsed > s.PowerTotal ? ThorTheme.Red : ThorTheme.Green, Paint.Align.Right);
			ThorTheme.Box(canvas, paint, new RectF(28, 112, 1212, 196));
			ThorTheme.Lines(canvas, paint, s.Selection, new RectF(46, 126, 864, 181), 27, Color.White, 1);
			var hp = s.Selected.Sum(a => (long)a.Health); var maximum = s.Selected.Sum(a => (long)a.MaximumHealth);
			if (maximum > 0)
			{
				var fraction = hp / (float)maximum;
				Text(canvas, $"{Math.Round(fraction * 100)}% HEALTH", 1190, 143, 19, ThorTheme.Muted, Paint.Align.Right);
				ThorTheme.Bar(canvas, paint, new RectF(904, 159, 1190, 168), fraction, fraction < .3 ? ThorTheme.Red : ThorTheme.Green);
			}
			else Text(canvas, s.InMenu ? "MENU" : "A TO SELECT", 1190, 155, 21, ThorTheme.Muted, Paint.Align.Right);
			var tabs = new[] { "COMMANDS", "BUILD & TRAIN", "POWERS" };
			for (var i = 0; i < 3; i++) Button(canvas, new RectF(28 + i * 400, 212, 412 + i * 400, 274), tabs[i], "tab:" + i, s.DeckTab == i);
			Text(canvas, "BATTLEFIELD RADAR", 28, 315, 21, ThorTheme.Muted);
			DrawRadar(canvas, s);
			var entries = s.DeckTab == 0 ? s.Commands : s.DeckTab == 1 ? s.Production : s.Powers;
			if (s.DeckTab == 1)
			{
				var labels = new[] { "ALL", "BUILDINGS", "DEFENCE", "UNITS" };
				for (var i = 0; i < labels.Length; i++) Button(canvas, new RectF(548 + i * 168, 288, 706 + i * 168, 326), labels[i], "filter:" + i, filter == i, true, 17);
				entries = entries.Where(e => filter == 0 || filter == 1 && e.Category == ThorController.Category.Buildings ||
					filter == 2 && e.Category == ThorController.Category.Defences || filter == 3 && e.Category is ThorController.Category.Infantry or ThorController.Category.Vehicles or ThorController.Category.Aircraft or ThorController.Category.Navy).ToArray();
			}
			else Text(canvas, s.DeckTab == 2 ? "SUPPORT POWERS & UPGRADES" : "ORDERS FOR YOUR SELECTION", 548, 315, 20, ThorTheme.Muted);
			var pages = Math.Max(1, (entries.Length + 7) / 8); page = Math.Clamp(page, 0, pages - 1);
			for (var i = 0; i < Math.Min(8, entries.Length - page * 8); i++)
			{
				var entry = entries[page * 8 + i]; var x = 548 + i % 2 * 340; var y = 340 + i / 2 * 112;
				Entry(canvas, new RectF(x, y, x + 324, y + 100), entry);
			}
			if (entries.Length == 0)
				ThorTheme.Lines(canvas, paint, s.DeckTab == 2 ? "Powers and upgrades appear here as you unlock them." :
					"Deploy an MCV or select another category to see your build choices.", new RectF(572, 390, 1178, 540), 28, ThorTheme.Muted, 4);
			Button(canvas, new RectF(548, 806, 724, 860), "‹ PREV", "page:previous", false, page > 0, 20);
			Text(canvas, $"{page + 1} / {pages}", 880, 842, 23, ThorTheme.Muted, Paint.Align.Center);
			Button(canvas, new RectF(1036, 806, 1212, 860), "NEXT ›", "page:next", false, page + 1 < pages, 20);
			Text(canvas, "CONTROL GROUPS", 28, 897, 21, ThorTheme.Muted);
			Text(canvas, "Tap recalls · Hold assigns · LB opens wheel", 1212, 897, 19, ThorTheme.Muted, Paint.Align.Right);
			for (var i = 0; i < 10; i++)
			{
				var x = 28 + i * 119; var bounds = new RectF(x, 914, x + 111, 988); var id = "group:" + i;
				ThorTheme.Box(canvas, paint, bounds, pressed == id, !s.InMenu);
				Text(canvas, (i + 1).ToString(), x + 15, 945, 24, s.Groups[i] > 0 ? ThorTheme.Gold : ThorTheme.Muted);
				Text(canvas, s.Groups[i] == 0 ? "EMPTY" : s.Groups[i] + " units", x + 15, 972, 16, ThorTheme.Muted);
				hits.Add((bounds, id, s.Battle && !s.InMenu)); HoldBar(canvas, bounds, id);
			}
			var hint = s.Feedback != "" ? s.Feedback : s.MapMode ? "MAP · Left stick aims · Y look · A order · B back" : s.Hint;
			ThorTheme.Lines(canvas, paint, hint, new RectF(28, 1002, 1212, 1066), 24, s.Feedback != "" ? ThorTheme.Gold : ThorTheme.Muted, 2);
			canvas.Restore(); PostInvalidateDelayed(pressed != null ? 33 : 100);
		}

		void DrawRadar(Canvas c, ThorController.State s)
		{
			ThorTheme.Box(c, paint, new RectF(28, 340, 520, 788)); radarBounds = new(); var radar = controller.Radar;
			if (radar == null || !radar.Available || radar.Width == 0)
			{
				Text(c, s.Battle ? "RADAR OFFLINE" : "YOUR BATTLEFIELD", 274, 540, 25, ThorTheme.Muted, Paint.Align.Center);
				ThorTheme.Lines(c, paint, s.Battle ? "Radar follows your faction's power and vision rules." : "Start a skirmish on the top screen.", new RectF(60, 568, 488, 662), 22, ThorTheme.Muted, 3); return;
			}
			if (!ReferenceEquals(radar, lastRadar))
			{
				if (radarBitmap == null || radarBitmap.Width != radar.Width || radarBitmap.Height != radar.Height)
				{ radarBitmap?.Dispose(); radarBitmap = Bitmap.CreateBitmap(radar.Width, radar.Height, Bitmap.Config.Argb8888); }
				radarBitmap.SetPixels(radar.Pixels, 0, radar.Width, 0, 0, radar.Width, radar.Height); lastRadar = radar;
			}
			var scale = Math.Min(464f / radar.Width, 420f / radar.Height); var w = radar.Width * scale; var h = radar.Height * scale;
			radarBounds = new RectF(274 - w / 2, 564 - h / 2, 274 + w / 2, 564 + h / 2);
			paint.Color = Color.White; paint.FilterBitmap = false; c.DrawBitmap(radarBitmap, null, radarBounds, paint);
			c.Save(); c.ClipRect(radarBounds);
			if (radar.Viewport.Length == 4)
			{
				paint.SetStyle(Paint.Style.Stroke); paint.Color = Color.White; paint.StrokeWidth = 2;
				c.DrawRect(radarBounds.Left + radar.Viewport[0] * w, radarBounds.Top + radar.Viewport[1] * h,
					radarBounds.Left + radar.Viewport[2] * w, radarBounds.Top + radar.Viewport[3] * h, paint); paint.SetStyle(Paint.Style.Fill);
			}
			if (s.MapMode)
			{
				paint.Color = ThorTheme.Gold; paint.StrokeWidth = 3; var x = radarBounds.Left + w * s.MapX; var y = radarBounds.Top + h * s.MapY;
				c.DrawLine(x - 13, y, x + 13, y, paint); c.DrawLine(x, y - 13, x, y + 13, paint);
			}
			c.Restore(); Text(c, "Tap or drag to look · RB: map controls", 30, 827, 21, ThorTheme.Muted);
		}

		void Entry(Canvas c, RectF bounds, ThorController.Entry entry)
		{
			ThorTheme.Box(c, paint, bounds, pressed == entry.Id || entry.Ready, entry.Enabled);
			var bitmap = artwork.Get(controller, entry.Icon);
			if (bitmap != null) ThorTheme.Icon(c, paint, bitmap, new RectF(bounds.Left + 10, bounds.Top + 13, bounds.Left + 85, bounds.Top + 70));
			else Text(c, ThorTheme.Symbol(entry.Id), bounds.Left + 43, bounds.Top + 51, 34, entry.Enabled ? ThorTheme.Gold : ThorTheme.Border, Paint.Align.Center);
			ThorTheme.Lines(c, paint, entry.Label, new RectF(bounds.Left + 95, bounds.Top + 6, bounds.Right - 9, bounds.Top + 63), 22, entry.Enabled ? Color.White : ThorTheme.Muted, 2);
			ThorTheme.Lines(c, paint, entry.Detail, new RectF(bounds.Left + 10, bounds.Top + 72, bounds.Right - 9, bounds.Top + 95), 17, entry.Ready ? ThorTheme.Gold : ThorTheme.Muted, 1);
			if (entry.Progress >= 0) ThorTheme.Bar(c, paint, new RectF(bounds.Left + 3, bounds.Bottom - 5, bounds.Right - 3, bounds.Bottom - 2), entry.Progress, entry.Ready ? ThorTheme.Gold : ThorTheme.Green);
				hits.Add((bounds, entry.Id, !controller.Snapshot.InMenu && (entry.Enabled || entry.CanCancel))); HoldBar(c, bounds, entry.Id);
		}
		void HoldBar(Canvas c, RectF bounds, string id)
		{
			if (pressed != id || !(id.StartsWith("build:", StringComparison.Ordinal) || id.StartsWith("group:", StringComparison.Ordinal))) return;
			var progress = Math.Clamp((SystemClock.UptimeMillis() - pressTime) / 650f, 0, 1);
			ThorTheme.Bar(c, paint, new RectF(bounds.Left + 4, bounds.Bottom - 8, bounds.Right - 4, bounds.Bottom - 3), progress, ThorTheme.Gold);
			if (progress >= 1 && !holdReady) { holdReady = true; PerformHapticFeedback(FeedbackConstants.LongPress); }
		}
		void Button(Canvas c, RectF bounds, string text, string id, bool selected, bool enabled = true, float size = 23)
		{
			ThorTheme.Box(c, paint, bounds, selected || pressed == id, enabled);
			Text(c, text, bounds.CenterX(), bounds.CenterY() + size * .35f, size, enabled ? selected ? ThorTheme.Gold : Color.White : ThorTheme.Border, Paint.Align.Center);
			hits.Add((bounds, id, enabled));
		}
		void Text(Canvas c, string text, float x, float y, float size, Color color, Paint.Align align = null) => ThorTheme.Text(c, paint, text, x, y, size, color, align);
		public override bool OnTouchEvent(MotionEvent e)
		{
			var x = e.GetX() * 1240 / Math.Max(1, Width); var y = e.GetY() * 1080 / Math.Max(1, Height);
			if (e.ActionMasked == MotionEventActions.Down)
			{
				pressed = null; pressX = x; pressY = y; pressTime = e.EventTime; holdReady = false;
				if (!controller.Snapshot.InMenu && radarBounds.Contains(x, y)) { pressed = "map"; LookAt(x, y); }
				else foreach (var hit in hits) if (hit.Enabled && hit.Bounds.Contains(x, y)) pressed = hit.Id;
				Invalidate(); return true;
			}
			if (pressed == "map")
			{
				if (e.ActionMasked is MotionEventActions.Move or MotionEventActions.Up) LookAt(x, y);
				if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) pressed = null;
				return true;
			}
			if (e.ActionMasked == MotionEventActions.Move && Math.Abs(x - pressX) + Math.Abs(y - pressY) > 50) pressed = null;
			if (e.ActionMasked == MotionEventActions.Up && pressed != null)
			{
				var id = pressed; pressed = null;
				if (!hits.Any(h => h.Id == id && h.Bounds.Contains(x, y))) return true;
				if (id.StartsWith("tab:", StringComparison.Ordinal)) { page = 0; filter = 0; controller.Execute(id); }
				else if (id.StartsWith("filter:", StringComparison.Ordinal)) { filter = int.Parse(id[7..]); page = 0; }
				else if (id == "page:previous") page = Math.Max(0, page - 1);
				else if (id == "page:next") page++;
				else if (e.EventTime - pressTime >= 650 && id.StartsWith("build:", StringComparison.Ordinal)) controller.Execute("cancel:" + id);
				else if (e.EventTime - pressTime >= 650 && id.StartsWith("group:", StringComparison.Ordinal)) controller.Execute("assign:" + id[6..]);
				else controller.Execute(id);
				PerformHapticFeedback(FeedbackConstants.VirtualKey); Invalidate(); return true;
			}
			if (e.ActionMasked == MotionEventActions.Cancel) pressed = null;
			return true;
		}
		void LookAt(float x, float y)
		{
			var radar = controller.Radar;
			if (radar == null || !radar.Available || radarBounds.Width() <= 0) return;
			controller.LookAt(radar.Epoch, (x - radarBounds.Left) / radarBounds.Width(), (y - radarBounds.Top) / radarBounds.Height());
		}
		protected override void OnDetachedFromWindow() { artwork.Dispose(); radarBitmap?.Dispose(); radarBitmap = null; lastRadar = null; base.OnDetachedFromWindow(); }
	}

	sealed class ThorWheel : View
	{
		readonly ThorController controller;
		readonly Paint paint = new(PaintFlags.AntiAlias);
		readonly ThorArtwork artwork = new();
		public ThorWheel(Context context, ThorController controller) : base(context) { this.controller = controller; }
		protected override void OnDraw(Canvas canvas)
		{
			var s = controller.Snapshot;
			if (s.Wheel != "")
			{
				artwork.Begin(s.Epoch); canvas.Save(); canvas.Scale(Width / 1920f, Height / 1080f);
				paint.Color = Color.Argb(205, 4, 9, 15); canvas.DrawRect(0, 0, 1920, 1080, paint);
				paint.Color = ThorTheme.Background; canvas.DrawCircle(960, 540, 208, paint);
				paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 2; paint.Color = ThorTheme.Border; canvas.DrawCircle(960, 540, 208, paint); paint.SetStyle(Paint.Style.Fill);
				Text(canvas, s.WheelTitle.ToUpperInvariant(), 960, 462, 32, ThorTheme.Gold);
				Text(canvas, $"{s.Page + 1} / {s.WheelPages} · D-PAD TO CHANGE PAGE", 960, 501, 18, ThorTheme.Muted);
				Text(canvas, "LEFT STICK TO CHOOSE", 960, 548, 24, Color.White);
				Text(canvas, "A confirm · B close", 960, 589, 24, Color.White);
				Text(canvas, s.Wheel == "Groups" ? "X assign · LT+X add · Y centre" : "X cancels one queued item", 960, 632, 20, ThorTheme.Muted);
				for (var i = 0; i < s.WheelEntries.Length; i++)
				{
					var entry = s.WheelEntries[i]; var angle = i * 2 * Math.PI / s.WheelEntries.Length - Math.PI / 2;
					var x = 960 + (float)Math.Cos(angle) * 470; var y = 540 + (float)Math.Sin(angle) * 342;
					ThorTheme.Box(canvas, paint, new RectF(x - 160, y - 64, x + 160, y + 64), s.Highlight == i, entry.Enabled);
					var bitmap = artwork.Get(controller, entry.Icon);
					if (bitmap != null) ThorTheme.Icon(canvas, paint, bitmap, new RectF(x - 147, y - 42, x - 67, y + 17));
					else ThorTheme.Text(canvas, paint, s.Wheel == "Groups" ? (s.Page * 8 + i + 1).ToString() : ThorTheme.Symbol(entry.Id), x - 108, y + 1, 37, entry.Enabled ? ThorTheme.Gold : ThorTheme.Muted, Paint.Align.Center);
					ThorTheme.Lines(canvas, paint, entry.Label, new RectF(x - 52, y - 50, x + 148, y + 13), 24, entry.Enabled ? Color.White : ThorTheme.Muted, 2);
					ThorTheme.Lines(canvas, paint, entry.Detail, new RectF(x - 145, y + 28, x + 145, y + 55), 19, entry.Ready ? ThorTheme.Gold : ThorTheme.Muted, 1);
					if (entry.Progress >= 0) ThorTheme.Bar(canvas, paint, new RectF(x - 154, y + 58, x + 154, y + 62), entry.Progress, ThorTheme.Green);
				}
				if (s.Feedback != "") Text(canvas, s.Feedback, 960, 1030, 25, ThorTheme.Gold);
				canvas.Restore();
			}
			PostInvalidateDelayed(s.Wheel == "" ? 100 : 33);
		}
		void Text(Canvas c, string text, float x, float y, float size, Color color) => ThorTheme.Text(c, paint, text, x, y, size, color, Paint.Align.Center);
		protected override void OnDetachedFromWindow() { artwork.Dispose(); base.OnDetachedFromWindow(); }
	}
}
