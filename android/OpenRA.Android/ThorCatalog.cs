using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Android.Views;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;
using AKey = Android.Views.Keycode;

namespace OpenRA.AndroidApp
{
	sealed partial class ThorController
	{
		public sealed record MenuTarget(string Id, string Label, int X, int Y);

		void Feedback(string text)
		{
			feedback = text;
			feedbackUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
			lastSnapshot = 0;
		}

		void RecenterCursor()
		{
			cursorX = Game.Renderer.Resolution.Width / 2f;
			cursorY = Game.Renderer.Resolution.Height / 2f;
		}

		static IEnumerable<ButtonWidget> VisibleButtons(Widget root)
		{
			if (root == null || !root.IsVisible()) yield break;
			if (root is ButtonWidget button && !button.IsDisabled() && button.RenderBounds.Width > 4 && button.RenderBounds.Height > 4)
				yield return button;
			foreach (var child in root.Children)
				foreach (var item in VisibleButtons(child)) yield return item;
		}

		// The in-game menu is attached under MENU_ROOT, outside Ui's window stack.
		static Widget ActiveMenu() => Ui.CurrentWindow() ?? Ui.Root.GetOrNull("MENU_ROOT")?.Children.LastOrDefault(w => w.IsVisible());

		MenuTarget[] MenuTargets() => VisibleButtons(ActiveMenu() ?? Ui.Root)
			.Select(b => new MenuTarget(b.Id ?? "", b.GetText?.Invoke() ?? b.Text ?? "",
				b.RenderBounds.X + b.RenderBounds.Width / 2, b.RenderBounds.Y + b.RenderBounds.Height / 2))
			.Where(b => b.X > 0 && b.Y > 0 && b.X < Game.Renderer.Resolution.Width && b.Y < Game.Renderer.Resolution.Height)
			.ToArray();

		void NavigateMenu(AKey key)
		{
			var horizontal = key is AKey.DpadLeft or AKey.DpadRight;
			var direction = key is AKey.DpadLeft or AKey.DpadUp ? -1 : 1;
			var candidates = MenuTargets();
			var target = candidates.Select(t => (Target: t,
				Forward: (horizontal ? t.X - cursorX : t.Y - cursorY) * direction,
				Side: Math.Abs(horizontal ? t.Y - cursorY : t.X - cursorX)))
				.Where(t => t.Forward > 5)
				.OrderBy(t => t.Forward + t.Side * 3).Select(t => t.Target).FirstOrDefault();
			target ??= candidates.OrderBy(t => direction * (horizontal ? t.X : t.Y)).FirstOrDefault();
			if (target == null) return;
			cursorX = target.X; cursorY = target.Y;
			Mouse(MouseInputEvent.Move, MouseButton.None);
			Feedback(target.Label);
		}

		string CacheIcon(World world, string image, string sequence, string palette)
		{
			var key = image + ":" + sequence + ":" + palette;
			if (iconCache.ContainsKey(key)) return key;
			if (!world.Map.Sequences.HasSequence(image, sequence)) return "";
			var animation = new Animation(world, image);
			animation.Play(sequence);
			var sprite = animation.Image;
			var bounds = sprite.Bounds;
			var width = Math.Abs(bounds.Width); var height = Math.Abs(bounds.Height);
			if (width == 0 || height == 0) return "";
			// Read the actual icon atlas, not the rendered game screen. Cache the
			// small result; this never runs for an already-exported cameo.
			var data = sprite.Sheet.GetData();
			var colors = sprite.Channel == TextureChannel.RGBA ? null : renderer.Palette(palette).Palette;
			var pixels = new int[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var sx = bounds.Width > 0 ? bounds.Left + x : bounds.Left - x - 1;
					var sy = bounds.Height > 0 ? bounds.Top + y : bounds.Top - y - 1;
					var offset = (sy * sprite.Sheet.Size.Width + sx) * 4;
					pixels[y * width + x] = colors != null ? unchecked((int)colors[data[offset + (int)sprite.Channel]]) :
						data[offset + 3] << 24 | data[offset + 2] << 16 | data[offset + 1] << 8 | data[offset];
				}
			iconCache[key] = new(pixels, width, height);
			return key;
		}

		static Category QueueCategory(string type)
		{
			var name = type.Split('.')[0];
			return name switch
			{
				"Building" => Category.Buildings, "Support" or "Defense" or "Defence" => Category.Defences,
				"Infantry" => Category.Infantry, "Vehicle" => Category.Vehicles, "Aircraft" => Category.Aircraft,
				"Ship" or "Naval" => Category.Navy, "Power" or "Upgrade" => Category.Upgrades,
				_ => Category.Vehicles
			};
		}

		void Publish(World world, bool battle)
		{
			var versionLabel = Ui.Root.GetOrNull<LabelWidget>("VERSION_LABEL");
			if (versionLabel != null) versionLabel.GetText = () => "THOR PREVIEW";
			productionActions.Clear();
			var entries = new List<Entry>();
			var powerEntries = new List<Entry>();
			var groups = new int[10];
			var selected = !battle ? [] : world.Selection.Actors.Where(a => !a.IsDead && !a.Disposed).ToArray();
			var owned = selected.Where(a => a.Owner == world.LocalPlayer).ToArray();
			var cash = 0; var used = 0; var total = 0;
			var selection = "Choose Singleplayer, then Skirmish";
			var faction = "";
			if (battle)
			{
				cash = world.LocalPlayer.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0;
				var powerManager = world.LocalPlayer.PlayerActor.TraitOrDefault<PowerManager>();
				used = powerManager?.PowerDrained ?? 0; total = powerManager?.PowerProvided ?? 0;
				faction = FluentProvider.GetMessage(world.LocalPlayer.Faction.Name);
				var radar = Ui.Root.GetOrNull<RadarWidget>("RADAR_MINIMAP");
				if (radar != null && !inMenu)
				{
					var image = radar.ExportThorRadar();
					Radar = new(epoch, image.Pixels, image.Width, image.Height, image.Available, image.Viewport);
				}
				selection = selected.Length == 0 ? "No units selected" : string.Join(", ", selected.GroupBy(a => a.Info.Name)
					.Select(g => g.Count() + " × " + Name(g.First().Info)));
				var controlGroups = world.WorldActor.TraitOrDefault<ControlGroups>();
				for (var i = 0; i < 10; i++) groups[i] = controlGroups?.GetActorsInControlGroup(i).Count() ?? 0;
				foreach (var actor in world.Actors.Where(a => a.Owner == world.LocalPlayer && !a.IsDead && !a.Disposed))
					foreach (var queue in actor.TraitsImplementing<ProductionQueue>())
						foreach (var item in queue.BuildableItems().OrderBy(i => BuildableInfo.GetTraitForQueue(i, queue.Info.Type).GetBuildPaletteOrder(i, queue)).ThenBy(i => i.Name))
						{
							var capturedQueue = queue; var capturedItem = item;
							var buildable = BuildableInfo.GetTraitForQueue(item, queue.Info.Type);
							var id = $"build:{epoch}:{actor.ActorID}:{queue.Info.Type}:{item.Name}";
							var queued = queue.AllQueued().Where(q => q.Item == item.Name).ToArray();
							var current = queued.FirstOrDefault();
							var canQueue = queue.CanQueue(item, out _, out _);
							var category = QueueCategory(queue.Info.Type);
							var rsi = item.TraitInfoOrDefault<RenderSpritesInfo>();
							var producer = queue.MostLikelyProducer();
							var image = rsi?.GetImage(item, producer.Trait?.Faction ?? world.LocalPlayer.Faction.InternalName);
							var icon = image == null ? "" : CacheIcon(world, image, buildable.Icon,
								buildable.IconPalette + (buildable.IconPaletteIsPlayerPalette ? actor.Owner.InternalName : ""));
							var detail = current == null ? (category == Category.Upgrades ? "Unlock upgrade" : "$" + queue.GetProductionCost(item)) : current.Done ? "READY · A to place" :
								current.Paused ? "ON HOLD · tap to resume" : $"{queued.Length} queued · {Math.Max(0, (current.TotalTime - current.RemainingTime) * 100 / Math.Max(1, current.TotalTime))}%";
							if (current == null && !canQueue) detail += " · unavailable";
							var entry = new Entry(id, Name(item), !inMenu && (canQueue || current != null))
							{
								Category = category, Detail = detail, Icon = icon, Queued = queued.Length,
								Ready = current?.Done == true, CanCancel = queued.Length > 0,
								Progress = current == null ? -1 : current.Done ? 1 : 1 - current.RemainingTime / (float)Math.Max(1, current.TotalTime)
							};
							if (category == Category.Upgrades) powerEntries.Add(entry); else entries.Add(entry);
							productionActions[id] = () =>
							{
								if (capturedQueue.Actor.IsDead || capturedQueue.Actor.Disposed || !capturedQueue.BuildableItems().Contains(capturedItem)) return;
								var ready = capturedQueue.AllQueued().FirstOrDefault(q => q.Item == capturedItem.Name && q.Done);
								if (ready != null && capturedItem.HasTraitInfo<BuildingInfo>())
								{
									world.OrderGenerator = new PlaceBuildingOrderGenerator(capturedQueue, capturedItem.Name, renderer);
									Feedback("Choose a location · A places · B cancels");
								}
								else if (capturedQueue.AllQueued().Any(q => q.Item == capturedItem.Name && q.Paused))
								{ world.IssueOrder(Order.PauseProduction(capturedQueue.Actor, capturedItem.Name, false)); Feedback("Production resumed"); }
								else if (capturedQueue.CanQueue(capturedItem, out _, out _))
								{ world.IssueOrder(Order.StartProduction(capturedQueue.Actor, capturedItem.Name, shift ? 5 : 1, true)); Feedback(Name(capturedItem) + (shift ? " · 5 queued" : " · queued")); }
								else Feedback("Unavailable · check funds, requirements or queue limit");
							};
							productionActions["cancel:" + id] = () =>
							{
								if (!capturedQueue.Actor.IsDead && !capturedQueue.Actor.Disposed && capturedQueue.AllQueued().Any(q => q.Item == capturedItem.Name))
								{ world.IssueOrder(Order.CancelProduction(capturedQueue.Actor, capturedItem.Name, 1)); Feedback(Name(capturedItem) + " · cancelled one"); }
							};
						}

				var manager = world.LocalPlayer.PlayerActor.TraitOrDefault<SupportPowerManager>();
				if (manager != null)
					foreach (var power in manager.Powers.Values.Where(p => !p.Disabled).OrderBy(p => p.Info.SupportPowerPaletteOrder))
					{
						var level = power.GetLevel();
						if (level == 0 || !power.Info.Names.TryGetValue(level, out var name)) continue;
						var capturedPower = power;
						var id = "power:" + epoch + ":" + power.Key;
						var seconds = (power.RemainingTicks * world.Timestep + 999) / 1000;
						powerEntries.Add(new Entry(id, FluentProvider.GetMessage(name), !inMenu && power.Ready && cash >= power.Info.Cost)
						{
							Category = Category.Powers, Ready = power.Ready,
							Detail = power.Ready ? (power.Info.Cost > 0 ? "$" + power.Info.Cost + " · READY" : "READY · choose a target") :
								power.Active ? $"Charging · {seconds / 60}:{seconds % 60:00}" : "On hold · requires power",
							Progress = power.TotalTicks == 0 ? 1 : 1 - power.RemainingTicks / (float)power.TotalTicks,
							Icon = power.Info.Icons.TryGetValue(level, out var sequence) ? CacheIcon(world, power.Info.IconImage, sequence, power.Info.IconPalette) : ""
						});
						productionActions[id] = () => { if (capturedPower.Ready && !capturedPower.Disabled) { capturedPower.Target(); Feedback("Choose a target · A confirms · B cancels"); } };
					}
			}
			production = entries.OrderBy(e => e.Category).ToArray();
			powers = powerEntries.OrderByDescending(e => e.Category).ToArray();
			var canAct = battle && !inMenu;
			commands = [
				new("deploy", "Deploy / unload", canAct && owned.Any(a => a.TraitsImplementing<IIssueDeployOrder>().Any(t => t.CanIssueDeployOrder(a, shift)))) { Detail = "Use the selected unit's special action" },
				new("attack", "Attack move", canAct && owned.Any(a => !a.Info.HasTraitInfo<BuildingInfo>())) { Detail = "Choose a destination, then A" },
				new("stop", "Stop", canAct && owned.Length > 0) { Detail = "Stop current orders" },
				new("same", "Same type", canAct && owned.Length > 0) { Detail = "Select matching units on screen" },
				new("center", "Centre selection", canAct && selected.Length > 0) { Detail = "Bring the selection into view" },
				new("repair", "Repair", canAct) { Detail = "A on your damaged building" },
				new("sell", "Sell", canAct) { Detail = "A on your building · B cancels" },
				new("deselect", "Deselect", canAct && selected.Length > 0) { Detail = "Clear the current selection" }
			];
			var pages = new List<WheelPage> { new("Orders", commands) };
			foreach (var category in production.Concat(powers).GroupBy(e => e.Category))
				foreach (var chunk in category.Chunk(8)) pages.Add(new(category.Key.ToString(), chunk));
			commandPages = pages.ToArray();
			var totalPages = wheel == "Groups" ? 2 : commandPages.Length;
			page = Math.Clamp(page, 0, totalPages - 1);
			var wheelEntries = wheel == "Groups" ? Enumerable.Range(page * 8, page == 0 ? 8 : 2)
				.Select(i => new Entry("group:" + i, "Group " + (i + 1), groups[i] > 0) { Detail = groups[i] + " units · X assigns selection" }).ToArray() : commandPages[page].Entries;
			Icons = new(iconCache);
			var generator = world?.OrderGenerator?.GetType().Name ?? "";
			Snapshot = new(battle, battle ? $"${cash} · {faction} · Power {used}/{total}" : "Native Android · AYN Thor",
				selection, commands, production, wheel, highlight, page, groups, world?.WorldTick ?? 0, mapMode, mapX, mapY)
			{
				Selected = selected.Select(a => { var health = a.TraitOrDefault<Health>(); return new ActorState(a.ActorID, a.Info.Name, Name(a.Info), a.CenterPosition.X, a.CenterPosition.Y, health?.HP ?? 0, health?.MaxHP ?? 0); }).ToArray(),
				CameraX = renderer?.Viewport.CenterLocation.X ?? 0, CameraY = renderer?.Viewport.CenterLocation.Y ?? 0,
				CursorX = (int)cursorX, CursorY = (int)cursorY, Zoom = renderer?.Viewport.Zoom ?? 1,
				Dragging = mouseHeld, RadarAvailable = Radar?.Available == true, Targeting = generator,
				Owned = !battle ? new() : world.Actors.Where(a => a.Owner == world.LocalPlayer && !a.IsDead && !a.Disposed).GroupBy(a => a.Info.Name).ToDictionary(g => g.Key, g => g.Count()),
				InMenu = inMenu, Window = ActiveMenu()?.Id ?? "", Epoch = epoch, Cash = cash, PowerUsed = used, PowerTotal = total, Faction = faction,
				Hint = inMenu ? "Left stick: cursor · D-pad: jump between buttons · A choose · B back" :
					generator == "PlaceBuildingOrderGenerator" ? "PLACING BUILDING · A confirm · B cancel · Left stick pan" :
					generator == "SelectDirectionalTarget" ? "AIRSTRIKE · Hold A + left stick: approach direction · Release A to launch · B cancel" :
					generator != "UnitOrderGenerator" ? "CHOOSE TARGET · A confirm · B cancel" : "A select / order · Hold A + left stick: selection box",
				Feedback = Stopwatch.GetTimestamp() < feedbackUntil ? feedback : "", Powers = powers,
				WheelTitle = wheel == "Groups" ? (page == 0 ? "Groups 1–8" : "Groups 9–10") : commandPages[page].Title,
				WheelEntries = wheelEntries, WheelPages = totalPages, DeckTab = deckTab,
				MenuButtons = inMenu ? MenuTargets().Select(t => t.Id + ": " + t.Label).ToArray() : [],
				Navigation = inMenu ? MenuTargets() : [], Width = Game.Renderer.Resolution.Width, Height = Game.Renderer.Resolution.Height,
				Paused = world?.Paused == true, RenderFrame = Game.RenderFrame, InputEvents = inputTrace.ToArray()
			};
			var now = Stopwatch.GetTimestamp();
			var changed = lastRecorded == null || Snapshot.Wheel != lastRecorded.Wheel || Snapshot.Highlight != lastRecorded.Highlight ||
				Snapshot.Page != lastRecorded.Page || Snapshot.Window != lastRecorded.Window || Snapshot.InMenu != lastRecorded.InMenu ||
				Snapshot.Selection != lastRecorded.Selection || Snapshot.DeckTab != lastRecorded.DeckTab || Snapshot.Targeting != lastRecorded.Targeting ||
				Snapshot.Feedback != lastRecorded.Feedback || Snapshot.Paused != lastRecorded.Paused ||
				(inMenu && (Snapshot.CursorX != lastRecorded.CursorX || Snapshot.CursorY != lastRecorded.CursorY));
			if ((now - lastStateFile) / (double)Stopwatch.Frequency > 1 || changed)
			{
				lastStateFile = now;
				try
				{
					System.IO.File.WriteAllText(statePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(Snapshot));
					System.IO.File.Move(statePath + ".tmp", statePath, true);
					lastRecorded = Snapshot;
				}
				catch (System.IO.IOException) { }
			}
		}
	}
}
