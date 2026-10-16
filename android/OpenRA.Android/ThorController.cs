using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Android.Views;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;
using AKey = Android.Views.Keycode;

namespace OpenRA.AndroidApp
{
	// Android supplies hardware state; only the engine thread reads or changes the game.
	sealed partial class ThorController
	{
		public enum Category { Orders, Buildings, Defences, Infantry, Vehicles, Aircraft, Navy, Upgrades, Powers }
		public sealed record Entry(string Id, string Label, bool Enabled = true)
		{
			public Category Category { get; init; }
			public string Detail { get; init; } = "";
			public string Icon { get; init; } = "";
			public float Progress { get; init; } = -1;
			public int Queued { get; init; }
			public bool Ready { get; init; }
			public bool CanCancel { get; init; }
			public bool Unlocked { get; init; }
			public string Description { get; init; } = "";
		}
		sealed record WheelPage(string Title, Entry[] Entries);
		public sealed record IconFrame(int[] Pixels, int Width, int Height);
		public sealed record ActorState(uint Id, string Type, string Name, int X, int Y, int Health, int MaximumHealth);
		public sealed record State(bool Battle, string Status, string Selection, Entry[] Commands,
			Entry[] Production, string Wheel, int Highlight, int Page, int[] Groups, int Tick, bool MapMode, float MapX, float MapY)
		{
			public ActorState[] Selected { get; init; } = [];
			public int CameraX { get; init; }
			public int CameraY { get; init; }
			public int CursorX { get; init; }
			public int CursorY { get; init; }
			public float Zoom { get; init; }
			public bool Dragging { get; init; }
			public bool RadarAvailable { get; init; }
			public string Targeting { get; init; } = "";
			public Dictionary<string, int> Owned { get; init; } = new();
			public bool InMenu { get; init; }
			public string Window { get; init; } = "";
			public int Epoch { get; init; }
			public int Cash { get; init; }
			public int PowerUsed { get; init; }
			public int PowerTotal { get; init; }
			public int? CommanderPoints { get; init; }
			public string Faction { get; init; } = "";
			public string Hint { get; init; } = "";
			public string Feedback { get; init; } = "";
			public Entry[] Powers { get; init; } = [];
			public Entry[] WheelEntries { get; init; } = [];
			public string WheelTitle { get; init; } = "";
			public int WheelPages { get; init; }
			public int DeckTab { get; init; }
			public string[] MenuButtons { get; init; } = [];
			public MenuTarget[] Navigation { get; init; } = [];
			public int Width { get; init; }
			public int Height { get; init; }
			public bool Paused { get; init; }
			public int RenderFrame { get; init; }
			public string[] InputEvents { get; init; } = [];
		}
		public sealed record RadarFrame(int Epoch, int[] Pixels, int Width, int Height, bool Available, float[] Viewport);
		sealed record Axes(float X, float Y, float Z, float Rz, float Lt, float Rt);
		readonly ConcurrentQueue<(AKey Key, bool Down, int Repeat)> buttons = new();
		readonly ConcurrentQueue<string> actions = new();
		readonly ConcurrentQueue<(int Epoch, float X, float Y)> mapTargets = new();
		readonly HashSet<AKey> held = new();
		readonly Queue<string> inputTrace = new();
		volatile Axes axes = new(0, 0, 0, 0, 0, 0);
		public volatile State Snapshot = new(false, "Starting…", "", [], [], "", 0, 0, new int[10], 0, false, .5f, .5f);
		public volatile RadarFrame Radar;
		public volatile Dictionary<string, IconFrame> Icons = new();
		volatile bool gamepadActive;
		readonly Dictionary<string, Action> productionActions = new();
		World previousWorld;
		World suspendedWorld;
		WorldRenderer renderer;
		IInputHandler input;
		long lastTime, lastSnapshot, lastStateFile;
		State lastRecorded;
		float cursorX, cursorY;
		int2 lastMousePosition;
		bool mouseHeld, triggerHeld, triggerSuppressed, bumperSuppressed, shift;
		bool mapMode;
		bool inMenu, sticksBlocked;
		int hatX, hatY;
		int deckTab;
		Widget previousWindow;
		string feedback = "";
		long feedbackUntil;
		readonly Dictionary<string, IconFrame> iconCache = new();
		readonly ConcurrentQueue<(float X, float Y, float Zoom)> gestures = new();
		WheelPage[] commandPages = [];
		Entry[] powers = [];
		float mapX = .5f, mapY = .5f;
		int highlight = -1, page, epoch;
		string wheel = "";
		Entry[] commands = [], production = [];
		readonly string statePath;

		public ThorController(string statePath) { this.statePath = statePath; }
		public void Execute(string id) => actions.Enqueue(id);
		public void LookAt(int worldEpoch, float x, float y) => mapTargets.Enqueue((worldEpoch, x, y));
		public void Gesture(float x, float y, float zoom) => gestures.Enqueue((x, y, zoom));
		public void Touch() { gamepadActive = false; Release(); }
		public void Suspend() { Release(); actions.Enqueue("suspend"); }
		public void Resume() => actions.Enqueue("resume");
		void TraceInput(string text)
		{
			inputTrace.Enqueue(text);
			while (inputTrace.Count > 24) inputTrace.Dequeue();
		}

		public bool Motion(MotionEvent e)
		{
			if ((e.Source & InputSourceType.Joystick) != InputSourceType.Joystick)
				return false;
			UpdateHat(ref hatX, e.GetAxisValue(Axis.HatX), AKey.DpadLeft, AKey.DpadRight);
			UpdateHat(ref hatY, e.GetAxisValue(Axis.HatY), AKey.DpadUp, AKey.DpadDown);
			var z = e.GetAxisValue(Axis.Z);
			var rz = e.GetAxisValue(Axis.Rz);
			if (z == 0 && rz == 0) { z = e.GetAxisValue(Axis.Rx); rz = e.GetAxisValue(Axis.Ry); }
			axes = new(e.GetAxisValue(Axis.X), e.GetAxisValue(Axis.Y), z, rz,
				Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake)),
				Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas)));
			var a = axes;
			if (Math.Abs(a.X) + Math.Abs(a.Y) + Math.Abs(a.Z) + Math.Abs(a.Rz) > .25f || a.Lt > .5f || a.Rt > .5f || hatX != 0 || hatY != 0) gamepadActive = true;
			return true;
		}

		void UpdateHat(ref int previous, float value, AKey negative, AKey positive)
		{
			var next = value < -.5f ? -1 : value > .5f ? 1 : 0;
			if (next == previous) return;
			if (previous != 0) buttons.Enqueue((previous < 0 ? negative : positive, false, 0));
			if (next != 0) buttons.Enqueue((next < 0 ? negative : positive, true, 0));
			previous = next;
		}

		public bool Key(KeyEvent e)
		{
			if (!IsControllerKey(e.KeyCode)) return false;
			gamepadActive = true;
			if (e.Action is KeyEventActions.Down or KeyEventActions.Up)
				buttons.Enqueue((e.KeyCode, e.Action == KeyEventActions.Down, e.RepeatCount));
			return true;
		}

		static bool IsControllerKey(AKey k) => k is AKey.ButtonA or AKey.ButtonB or AKey.ButtonX or AKey.ButtonY
			or AKey.ButtonL1 or AKey.ButtonR1 or AKey.ButtonL2 or AKey.ButtonR2 or AKey.ButtonThumbl
			or AKey.ButtonThumbr or AKey.ButtonStart or AKey.ButtonSelect or AKey.DpadUp or AKey.DpadDown
			or AKey.DpadLeft or AKey.DpadRight;

		public void Release()
		{
			axes = new(0, 0, 0, 0, 0, 0);
			actions.Enqueue("release");
		}

		static float Dead(float value)
		{
			const float dead = .18f;
			return Math.Abs(value) <= dead ? 0 : Math.Sign(value) * (Math.Abs(value) - dead) / (1 - dead);
		}

		public void Frame(IInputHandler handler)
		{
			// Selection and targeting are local UI state. Match the native widget
			// dispatch boundary; actual simulation changes still go through orders.
			Sync.RunUnsynced(Game.CurrentWorldRenderer?.World, () => FrameUnsynced(handler));
		}

		void FrameUnsynced(IInputHandler handler)
		{
			input = handler;
			renderer = Game.CurrentWorldRenderer;
			var world = renderer?.World;
			var battle = world?.Type == WorldType.Regular && world.LocalPlayer != null && world.WorldTick > 0;
			var currentWindow = ActiveMenu();
			inMenu = currentWindow != null || !battle || world.IsLoadingGameSave;
			if (currentWindow != previousWindow)
			{
				previousWindow = currentWindow;
				sticksBlocked = true;
				if (mouseHeld) CancelDrag();
			}
			if (world != previousWorld)
			{
				previousWorld = world; epoch++; page = 0; wheel = "";
				mapMode = false; Radar = null;
				cursorX = Game.Renderer.Resolution.Width / 2f;
				cursorY = Game.Renderer.Resolution.Height / 2f;
				productionActions.Clear();
				iconCache.Clear(); Icons = new();
				deckTab = 0; highlight = -1;
			}
			var now = Stopwatch.GetTimestamp();
			var dt = Math.Clamp((now - lastTime) / (float)Stopwatch.Frequency, 0, .05f);
			lastTime = now;
			var sticks = axes;
			var playing = battle && !inMenu;
			while (gestures.TryDequeue(out var gesture))
				if (playing)
				{
					renderer.Viewport.Scroll(new float2(-gesture.X * Game.Renderer.Resolution.Width, -gesture.Y * Game.Renderer.Resolution.Height), false);
					renderer.Viewport.AdjustZoom(gesture.Zoom);
				}
			while (actions.TryDequeue(out var action)) Act(action, world, battle);
			while (mapTargets.TryDequeue(out var target))
				if (playing && target.Epoch == epoch) Ui.Root.GetOrNull<RadarWidget>("RADAR_MINIMAP")?.TargetThorRadar(target.X, target.Y, false);
			while (buttons.TryDequeue(out var button))
			{
				var before = $"{wheel}:{page}";
				// Hat axes and key events share edge tracking; repeats cannot fire twice.
				if (!(button.Down ? held.Add(button.Key) : held.Remove(button.Key))) continue;
				UpdateModifiers(sticks, playing);
				Button(button.Key, button.Down, world, playing);
				TraceInput($"{button.Key} {(button.Down ? "down" : "up")} repeat {button.Repeat} {before} -> {wheel}:{page}");
			}
			UpdateModifiers(sticks, playing);
			var x = Dead(sticks.X); var y = Dead(sticks.Y);
			if (sticksBlocked)
			{
				if (x == 0 && y == 0) sticksBlocked = false;
				x = y = 0;
			}
			if (wheel != "")
			{
				var count = wheel == "Groups" ? (page == 0 ? 8 : 2) :
					page < commandPages.Length ? commandPages[page].Entries.Length : 0;
				if (Math.Abs(x) + Math.Abs(y) > .35f)
					highlight = count == 0 ? -1 : ((int)Math.Round(Math.Atan2(x, -y) * count / (2 * Math.PI)) + count) % count;
			}
			else if (gamepadActive)
			{
				var aim = held.Contains(AKey.ButtonThumbr);
				if (playing && mapMode)
				{
					mapX = Math.Clamp(mapX + x * dt * .5f, 0, 1);
					mapY = Math.Clamp(mapY + y * dt * .5f, 0, 1);
				}
				else if (playing && !mouseHeld && !aim)
				{
					if (x != 0 || y != 0)
					{
						renderer.Viewport.Scroll(new float2(x * dt * 720, y * dt * 720), false);
						cursorX = Game.Renderer.Resolution.Width / 2f;
						cursorY = Game.Renderer.Resolution.Height / 2f;
					}
					renderer.Viewport.AdjustZoom(-Dead(sticks.Rz) * dt * 1.3f);
				}
				else
				{
					var speed = mouseHeld ? 650 : 850;
					cursorX += (aim ? Dead(sticks.Z) : x) * dt * speed;
					cursorY += (aim ? Dead(sticks.Rz) : y) * dt * speed;
				}
				cursorX = Math.Clamp(cursorX, 2, Math.Max(2, Game.Renderer.Resolution.Width - 3));
				cursorY = Math.Clamp(cursorY, 2, Math.Max(2, Game.Renderer.Resolution.Height - 3));
				Mouse(MouseInputEvent.Move, mouseHeld ? MouseButton.Left : MouseButton.None);
			}
			if ((now - lastSnapshot) / (double)Stopwatch.Frequency > .15)
			{
				lastSnapshot = now;
				Publish(world, battle);
			}
		}

		void UpdateModifiers(Axes sticks, bool battle)
		{
			shift = sticks.Lt > .5f || held.Contains(AKey.ButtonL2);
			var rt = sticks.Rt > (triggerHeld ? .35f : .55f) || held.Contains(AKey.ButtonR2);
			if (!rt) triggerSuppressed = false;
			if (!held.Contains(AKey.ButtonL1)) bumperSuppressed = false;
			triggerHeld = rt;
			var next = battle && rt && !triggerSuppressed ? "Commands" :
				battle && held.Contains(AKey.ButtonL1) && !bumperSuppressed ? "Groups" : "";
			if (next != wheel)
			{
				TraceInput($"Wheel {wheel} -> {next}; reset page");
				if (mouseHeld)
				{
					CancelDrag();
				}
				page = 0;
				highlight = -1;
				if (next == "") sticksBlocked = true;
				else mapMode = false;
				wheel = next;
				lastSnapshot = 0;
			}
			input.ModifierKeys(shift ? Modifiers.Shift : Modifiers.None);
		}

		void Mouse(MouseInputEvent type, MouseButton button)
		{
			input.ModifierKeys(shift ? Modifiers.Shift : Modifiers.None);
			var position = new int2((int)cursorX, (int)cursorY);
			var delta = type == MouseInputEvent.Move ? position - lastMousePosition : int2.Zero;
			lastMousePosition = position;
			input.OnMouseInput(new MouseInput(type, button, position, delta,
				shift ? Modifiers.Shift : Modifiers.None, 1));
		}

		void Button(AKey key, bool down, World world, bool battle)
		{
			if (key == AKey.ButtonA)
			{
				if (mapMode && battle)
				{
					if (down) Ui.Root.GetOrNull<RadarWidget>("RADAR_MINIMAP")?.TargetThorRadar(mapX, mapY, true);
					return;
				}
				if (wheel != "")
				{
					if (down)
					{
						if (highlight < 0) { Feedback("Choose with the left stick first"); return; }
						if (wheel == "Groups") Group(world, highlight + page * 8, "recall");
						else if (page < commandPages.Length && highlight < commandPages[page].Entries.Length)
							Act(commandPages[page].Entries[highlight].Id, world, battle);
					}
					return;
				}
				if (!down && !mouseHeld) return;
				mouseHeld = down;
				Mouse(down ? MouseInputEvent.Down : MouseInputEvent.Up, MouseButton.Left);
				return;
			}
			if (!down) return;
			if (key == AKey.ButtonStart) { Keyboard(OpenRA.Keycode.ESCAPE); return; }
			if (key == AKey.ButtonB)
			{
				var wasTargeting = battle && world.OrderGenerator.GetType() != typeof(UnitOrderGenerator);
				if (mouseHeld) CancelDrag();
				if (wheel != "") { triggerSuppressed = bumperSuppressed = true; wheel = ""; sticksBlocked = true; }
				else if (mapMode) mapMode = false;
				else if (battle)
				{
					if (wasTargeting) world.CancelInputMode();
					else world.Selection.Clear();
				}
				else Keyboard(OpenRA.Keycode.ESCAPE);
				return;
			}
			if (!battle)
			{
				if (key is AKey.DpadUp or AKey.DpadDown or AKey.DpadLeft or AKey.DpadRight) NavigateMenu(key);
				return;
			}
			if (key == AKey.ButtonSelect) { deckTab = (deckTab + 1) % 3; lastSnapshot = 0; return; }
			if (key == AKey.ButtonR1) { if (wheel == "") mapMode = !mapMode; return; }
			if (mapMode && key == AKey.ButtonY) { Ui.Root.GetOrNull<RadarWidget>("RADAR_MINIMAP")?.TargetThorRadar(mapX, mapY, false); return; }
			if (wheel != "")
			{
				if (key is AKey.DpadLeft or AKey.DpadRight)
				{
					var pages = wheel == "Groups" ? 2 : Math.Max(1, commandPages.Length);
					page = (page + (key == AKey.DpadRight ? 1 : pages - 1)) % pages;
					highlight = -1; lastSnapshot = 0;
				}
				if (highlight < 0) return;
				if (wheel == "Groups" && key == AKey.ButtonX) Group(world, highlight + page * 8, shift ? "add" : "assign");
				if (wheel == "Groups" && key == AKey.ButtonY) Group(world, highlight + page * 8, "center");
				if (wheel == "Commands" && key == AKey.ButtonX && page < commandPages.Length && highlight < commandPages[page].Entries.Length)
					Act("cancel:" + commandPages[page].Entries[highlight].Id, world, battle);
				return;
			}
			if (key == AKey.ButtonY) Act("stop", world, true);
			if (key == AKey.ButtonX) Act("attack", world, true);
			if (key == AKey.ButtonThumbl) Act(shift ? "same" : "center", world, true);
			if (key == AKey.DpadUp) Cycle(world, a => a.Info.HasTraitInfo<HarvesterInfo>() && a.IsIdle);
			if (key == AKey.DpadDown) Cycle(world, a => a.Info.Name.EndsWith("cnst", StringComparison.Ordinal));
			if (key == AKey.DpadLeft) Cycle(world, a => !a.Info.HasTraitInfo<BuildingInfo>() && a.Info.HasTraitInfo<ISelectableInfo>());
			if (key == AKey.DpadRight) Cycle(world, a => a.Info.HasTraitInfo<BuildingInfo>() && a.TraitsImplementing<ProductionQueue>().Any());
		}

		void Keyboard(OpenRA.Keycode key)
		{
			input.OnKeyInput(new KeyInput { Key = key, Event = KeyInputEvent.Down });
			input.OnKeyInput(new KeyInput { Key = key, Event = KeyInputEvent.Up });
		}

		void Group(World world, int index, string action)
		{
			if (index < 0 || index >= 10) return;
			var groups = world.WorldActor.TraitOrDefault<ControlGroups>();
			if (groups == null) return;
			if (action is "assign" or "add" && !world.Selection.Actors.Any(a => a.Owner == world.LocalPlayer)) { Feedback("Select your units first"); return; }
			if (action is "recall" or "center" && !groups.GetActorsInControlGroup(index).Any()) { Feedback($"Group {index + 1} is empty · X assigns units"); return; }
			if (action == "assign") groups.CreateControlGroup(index);
			if (action == "add") groups.AddSelectionToControlGroup(index);
			if (action == "recall") groups.SelectControlGroup(index);
			if (action == "center") renderer.Viewport.Center(groups.GetActorsInControlGroup(index));
			RecenterCursor();
			Feedback($"Group {index + 1} · " + (action == "assign" ? "assigned" : action == "add" ? "units added" : action == "center" ? "centred" : "selected"));
		}

		void Cycle(World world, Func<Actor, bool> predicate)
		{
			var candidates = world.Actors.Where(a => a.Owner == world.LocalPlayer && !a.IsDead && predicate(a)).OrderBy(a => a.ActorID).ToArray();
			if (candidates.Length == 0) return;
			var index = Array.FindIndex(candidates, a => world.Selection.Contains(a));
			var chosen = candidates[(index + 1) % candidates.Length];
			world.Selection.Combine(world, [chosen], false, false);
			renderer.Viewport.Center([chosen]);
			RecenterCursor();
		}

		void Act(string id, World world, bool battle)
		{
			if (id == "suspend")
			{
				if (battle && !world.IsReplay && !world.PredictedPaused && world.LobbyInfo.NonBotClients.Count() == 1)
				{ suspendedWorld = world; world.SetPauseState(true); }
				return;
			}
			if (id == "resume")
			{
				if (suspendedWorld != null && suspendedWorld == world && ActiveMenu() == null) world.SetPauseState(false);
				suspendedWorld = null;
				return;
			}
			if (id == "cancel-touch") { CancelDrag(); return; }
			if (id == "release")
			{
				if (mouseHeld) CancelDrag();
				mouseHeld = false; held.Clear(); shift = false; wheel = "";
				input.ModifierKeys(Modifiers.None);
				mapMode = false;
				sticksBlocked = true;
				return;
			}
			if (id.StartsWith("tab:", StringComparison.Ordinal)) { deckTab = int.Parse(id[4..]); lastSnapshot = 0; return; }
			if (!battle || inMenu) return;
			var infoOnly = id.StartsWith("info:", StringComparison.Ordinal);
			if (infoOnly) id = id[5..];
			var available = commands.Concat(production).Concat(powers).FirstOrDefault(e => e.Id == id);
			if (available != null && (infoOnly || !available.Enabled)) { Feedback(available.Label + " · " + available.Detail + (available.Description == "" ? "" : " · " + available.Description)); return; }
			if (productionActions.TryGetValue(id, out var produce)) { produce(); return; }
			var entry = commands.FirstOrDefault(e => e.Id == id);
			if (entry != null && !entry.Enabled) { Feedback(entry.Detail); return; }
			var selected = world.Selection.Actors.Where(a => a.Owner == world.LocalPlayer && !a.IsDead).ToArray();
			if (id == "center") { renderer.Viewport.Center(selected); RecenterCursor(); }
			if (id == "deselect") { world.CancelInputMode(); world.Selection.Clear(); }
			if (id == "stop") foreach (var actor in selected) world.IssueOrder(new Order("Stop", actor, false));
			if (id == "attack") world.OrderGenerator = new AttackMoveOrderGenerator(selected, MouseButton.Left);
			if (id == "repair") world.OrderGenerator = new RepairOrderGenerator();
			if (id == "sell") world.OrderGenerator = new SellOrderGenerator();
			if (id == "deploy")
				foreach (var actor in selected)
					foreach (var trait in actor.TraitsImplementing<IIssueDeployOrder>())
						if (trait.CanIssueDeployOrder(actor, shift))
						{
							var order = trait.IssueDeployOrder(actor, shift);
							if (order != null) world.IssueOrder(order);
						}
			if (id == "same")
			{
				var names = selected.Select(a => a.Info.Name).ToHashSet();
				world.Selection.Combine(world, world.Actors.Where(a => a.Owner == world.LocalPlayer && !a.IsDead && names.Contains(a.Info.Name))
					.Where(a => { var p = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(a.CenterPosition));
						return p.X >= 0 && p.Y >= 0 && p.X < Game.Renderer.Resolution.Width && p.Y < Game.Renderer.Resolution.Height; }), false, false);
			}
			if (id.StartsWith("group:", StringComparison.Ordinal)) Group(world, int.Parse(id[6..]), "recall");
			if (id.StartsWith("assign:", StringComparison.Ordinal)) Group(world, int.Parse(id[7..]), "assign");
			if (entry != null) Feedback(entry.Label);
		}

		void CancelDrag()
		{
			if (renderer?.World?.OrderGenerator is SelectDirectionalTarget) renderer.World.CancelInputMode();
			if (Ui.MouseFocusWidget is WorldInteractionControllerWidget interaction) interaction.CancelThorDrag();
			else Ui.MouseFocusWidget?.YieldMouseFocus(default);
			mouseHeld = false;
		}

		static string Name(ActorInfo info)
		{
			var key = info.TraitInfos<TooltipInfo>().FirstOrDefault()?.Name;
			return key == null ? info.Name : FluentProvider.GetMessage(key);
		}
	}
}
