#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Concurrent;
using System.Threading;
using Android.Views;
using OpenRA.Platforms.Default;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	/// <summary>
	/// IPlatformWindow backed by the Android SurfaceView owned by the app's main activity.
	///
	/// Input: the activity posts MotionEvents (touch, mouse, hover, scroll) and KeyEvents,
	/// which are translated to OpenRA input events on the posting thread and drained by
	/// PumpInput on the engine thread. Touch maps to the left mouse button; a mouse
	/// (Bluetooth/USB, or the emulator's host mouse) gets full L/R/M buttons, wheel and hover.
	///
	/// Lifecycle: when the activity surface is destroyed (app backgrounded, screen off) the
	/// window reports IsSuspended so the game loop stops rendering; the EGL surface is
	/// recreated on the engine thread when a new surface arrives (the EGL context, and
	/// therefore all GL resources, are preserved).
	/// </summary>
	public sealed class AndroidPlatformWindow : IPlatformWindow
	{
		static readonly object SurfaceSync = new();
		static volatile Surface surface;
		static Size surfaceSize;
		static int surfaceVersion;
		static float density = 2f;

		static readonly ConcurrentQueue<Action<IInputHandler>> InputQueue = new();
		static Modifiers currentMods;
		static int2 pointerPos;
		static MouseButton pointerButtons = MouseButton.None;
		static bool touchCommandActive;

		readonly AndroidGraphicsContext context;
		readonly float windowScale;
		float scaleModifier;
		bool contextHasSurface;
		int appliedSurfaceVersion;

		/// <summary>Called by the activity before the game starts, to inform scaling decisions.</summary>
		public static void SetDisplayDensity(float d)
		{
			density = d;
		}

		/// <summary>Called by the activity (UI thread) when the rendering surface is created or changed.</summary>
		public static void SetSurface(Surface s, int width, int height)
		{
			lock (SurfaceSync)
			{
				surface = s;
				surfaceSize = new Size(width, height);
				surfaceVersion++;
				Monitor.PulseAll(SurfaceSync);
			}
		}

		/// <summary>Called by the activity (UI thread) when the rendering surface is destroyed.</summary>
		public static void ClearSurface()
		{
			lock (SurfaceSync)
			{
				surface = null;
				surfaceVersion++;
				Monitor.PulseAll(SurfaceSync);
			}
		}

		public IGraphicsContext Context => context;

		public AndroidPlatformWindow(Size requestEffectiveWindowSize, WindowMode windowMode,
			float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile requestProfile)
		{
			this.scaleModifier = scaleModifier;

			if (requestProfile != GLProfile.Automatic && requestProfile != GLProfile.Embedded)
				Log.Write("graphics", $"Requested GL profile {requestProfile} is not supported on Android, using Embedded (GLES).");

			// Half the display density gives a reasonable effective resolution for the UI layout.
			windowScale = Math.Max(1f, density / 2f);

			Surface s;
			lock (SurfaceSync)
			{
				Console.WriteLine("Waiting for Android surface...");
				while (surface == null)
					Monitor.Wait(SurfaceSync);

				s = surface;
				appliedSurfaceVersion = surfaceVersion;
			}

			Console.WriteLine($"Android surface: {surfaceSize.Width}x{surfaceSize.Height} (density {density}, window scale {windowScale})");

			OpenGL.GetProcAddressOverride = EglWindow.GetProcAddress;

			// The EGL context is created and made current on the engine thread;
			// all GL work (including Present) runs on this thread.
			var egl = new EglWindow(s);
			context = new AndroidGraphicsContext(this, egl);
			contextHasSurface = true;
			activeWindow = this;
		}

		/// <summary>
		/// Called on the engine thread from AndroidGraphicsContext.Present().
		/// Recreates the EGL surface if the activity supplied a new one, releases it if the
		/// surface is gone. Returns false when there is currently nothing to render to.
		/// </summary>
		internal bool SyncSurface()
		{
			if (appliedSurfaceVersion == surfaceVersion && contextHasSurface)
				return true;

			lock (SurfaceSync)
			{
				appliedSurfaceVersion = surfaceVersion;

				if (surface == null)
				{
					if (contextHasSurface)
					{
						context.ReleaseSurface();
						contextHasSurface = false;
					}

					return false;
				}

				context.RecreateSurface(surface);
				contextHasSurface = true;
				return true;
			}
		}

		public Size NativeWindowSize => new Size((int)(surfaceSize.Width / windowScale), (int)(surfaceSize.Height / windowScale));

		public Size EffectiveWindowSize => new Size((int)(NativeWindowSize.Width / scaleModifier), (int)(NativeWindowSize.Height / scaleModifier));

		public float NativeWindowScale => windowScale;

		public float EffectiveWindowScale => windowScale * scaleModifier;

		public Size SurfaceSize => surfaceSize;

		public int DisplayCount => 1;

		public int CurrentDisplay => 0;

		public bool HasInputFocus => true;

		/// <summary>
		/// True while the activity surface is destroyed (app backgrounded, screen off).
		/// The game loop skips rendering while suspended; rendering resumes automatically
		/// when a new surface arrives (Present rebinds the EGL surface).
		/// </summary>
		// The engine queries this before rendering, not after its first resumed
		// frame. Rebind before any palette, framebuffer or sprite work uses GL.
		public bool IsSuspended => !SyncSurface();

		public GLProfile GLProfile => GLProfile.Embedded;

		public GLProfile[] SupportedGLProfiles => new[] { GLProfile.Embedded };

		public event Action<float, float, float, float> OnWindowScaleChanged = (oldNative, oldEffective, newNative, newEffective) => { };

		#region Input translation (called on the activity UI thread)

		static int2 EventPosition(float x, float y)
		{
			// Touch/mouse events are in surface (pixel) coordinates: scale to effective window coordinates.
			// windowScale/scaleModifier are instance state, but only ever set before input starts flowing,
			// so using the latest values from the active window is fine.
			var w = activeWindow;
			if (w != null && w.EffectiveWindowSize != surfaceSize)
			{
				var s = 1 / w.EffectiveWindowScale;
				return new int2((int)(x * s), (int)(y * s));
			}

			return new int2((int)x, (int)y);
		}

		static AndroidPlatformWindow activeWindow;

		/// <summary>Called by the activity (UI thread) for touch and mouse motion/button events.</summary>
		public static void EnqueueMotion(MotionEvent e)
		{
			// Drop excess motion coalescing candidates when the engine is suspended or busy.
			if (InputQueue.Count > 200)
				return;

			currentMods = MakeModifiers(e.MetaState);
			var pos = EventPosition(e.GetX(), e.GetY());
			var delta = pos - pointerPos;

			switch (e.ActionMasked)
			{
				case MotionEventActions.Down:
					pointerPos = pos;
					pointerButtons |= MouseButton.Left;

					// Touch taps arrive without a preceding hover: emit a Move first so the
					// engine's hover state (and hover-dependent cursors such as MCV deploy)
					// reflects the tap position when the Down event is processed.
					if (delta != int2.Zero)
						EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
							MouseInputEvent.Move, pointerButtons & ~MouseButton.Left, pos, delta, currentMods, 0)));

					EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
						MouseInputEvent.Down, MouseButton.Left, pos, int2.Zero, currentMods,
						MultiTapDetection.DetectFromMouse(1, pos))));
					break;

				case MotionEventActions.Up:
				case MotionEventActions.Cancel:
					pointerPos = pos;
					pointerButtons &= ~MouseButton.Left;

					if (touchCommandActive)
					{
						// Two-finger command gesture: the right click was already issued on
						// PointerDown. Release the left button (held since the first finger's
						// Down) with Shift so the click does not alter the current selection
						// (Shift-combine leaves it unchanged), then end the gesture.
						touchCommandActive = false;
						EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
							MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, currentMods | Modifiers.Shift,
							MultiTapDetection.InfoFromMouse(1))));
						break;
					}

					EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
						MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, currentMods,
						MultiTapDetection.InfoFromMouse(1))));
					break;

				case MotionEventActions.PointerDown:
					// Second finger touching while the first is down: treat as a right click at
					// the primary pointer position. Touch has no right button, but the Modern
					// control scheme issues all commands (move, deploy, force-fire) with it.
					if (e.PointerCount == 2 && pointerButtons.HasFlag(MouseButton.Left))
					{
						var primaryPos = EventPosition(e.GetX(0), e.GetY(0));

						touchCommandActive = true;
						EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
							MouseInputEvent.Down, MouseButton.Right, primaryPos, int2.Zero, currentMods,
							MultiTapDetection.DetectFromMouse(3, primaryPos))));
						EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
							MouseInputEvent.Up, MouseButton.Right, primaryPos, int2.Zero, currentMods,
							MultiTapDetection.InfoFromMouse(3))));
					}

					break;

				case MotionEventActions.Move:
				case MotionEventActions.HoverMove:
					pointerPos = pos;
					EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
						MouseInputEvent.Move, pointerButtons, pos, delta, currentMods, 0)));
					break;

				case MotionEventActions.ButtonPress:
				case MotionEventActions.ButtonRelease:
				{
					var (button, sdlButton) = MakeButton(e.ActionButton);
					if (button == MouseButton.None)
						break; // Primary button already arrives as ACTION_DOWN/UP

					var down = e.ActionMasked == MotionEventActions.ButtonPress;
					if (down)
						pointerButtons |= button;
					else
						pointerButtons &= ~button;

					EnqueueMouse(inputHandler => inputHandler.OnMouseInput(new MouseInput(
						down ? MouseInputEvent.Down : MouseInputEvent.Up, button, pos, int2.Zero, currentMods,
						down ? MultiTapDetection.DetectFromMouse(sdlButton, pos) : MultiTapDetection.InfoFromMouse(sdlButton))));
					break;
				}

				case MotionEventActions.Scroll:
				{
					var wheelY = (int)e.GetAxisValue(Axis.Vscroll);
					if (wheelY != 0)
						EnqueueMouse(inputHandler => inputHandler.OnMouseInput(
							new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pos, new int2(0, wheelY), currentMods, 0)));

					break;
				}
			}
		}

		public static void ClearTouchState()
		{
			pointerButtons = MouseButton.None;
			touchCommandActive = false;
		}

		static void EnqueueMouse(Action<IInputHandler> a)
		{
			InputQueue.Enqueue(a);
		}

		static (MouseButton Button, byte SdlButton) MakeButton(MotionEventButtonState actionButton)
		{
			if (actionButton.HasFlag(MotionEventButtonState.Secondary))
				return (MouseButton.Right, 3);

			if (actionButton.HasFlag(MotionEventButtonState.Tertiary))
				return (MouseButton.Middle, 2);

			return (MouseButton.None, 0);
		}

		static Modifiers MakeModifiers(MetaKeyStates meta)
		{
			return (meta.HasFlag(MetaKeyStates.ShiftOn) ? Modifiers.Shift : 0)
				 | (meta.HasFlag(MetaKeyStates.CtrlOn) ? Modifiers.Ctrl : 0)
				 | (meta.HasFlag(MetaKeyStates.AltOn) ? Modifiers.Alt : 0)
				 | (meta.HasFlag(MetaKeyStates.MetaOn) ? Modifiers.Meta : 0);
		}

		/// <summary>Called by the activity (UI thread) for key events. Returns true if consumed.</summary>
		public static bool EnqueueKey(KeyEvent e)
		{
			var key = MapKey(e.KeyCode);
			if (key == null)
				return false; // Let the system handle it (e.g. volume keys)

			currentMods = MakeModifiers(e.MetaState);

			if (e.Action == KeyEventActions.Up)
			{
				var keyUp = new KeyInput
				{
					Event = KeyInputEvent.Up,
					Key = key.Value,
					Modifiers = currentMods,
					UnicodeChar = unchecked((char)(int)key.Value),
					MultiTapCount = MultiTapDetection.InfoFromKeyboard(key.Value, currentMods),
					IsRepeat = false
				};
				InputQueue.Enqueue(inputHandler => inputHandler.OnKeyInput(keyUp));
				return true;
			}

			var keyDown = new KeyInput
			{
				Event = KeyInputEvent.Down,
				Key = key.Value,
				Modifiers = currentMods,
				UnicodeChar = unchecked((char)(int)key.Value),
				MultiTapCount = MultiTapDetection.DetectFromKeyboard(key.Value, currentMods),
				IsRepeat = e.RepeatCount > 0
			};
			InputQueue.Enqueue(inputHandler => inputHandler.OnKeyInput(keyDown));

			// Mirror SDL_TEXTINPUT for text-producing keys (letters, digits, punctuation, space).
			// Control characters (Enter, Tab, Backspace, Escape...) are handled via OnKeyInput.
			var uc = e.UnicodeChar;
			if (uc >= 32 && !currentMods.HasModifier(Modifiers.Ctrl) && !currentMods.HasModifier(Modifiers.Alt))
			{
				var text = char.ConvertFromUtf32(uc);
				InputQueue.Enqueue(inputHandler => inputHandler.OnTextInput(text));
			}

			return true;
		}

		static Keycode? MapKey(global::Android.Views.Keycode keyCode)
		{
			if (AndroidKeyMap.Map.TryGetValue(keyCode, out var key))
				return key;

			return null;
		}

		#endregion

		public void PumpInput(IInputHandler inputHandler)
		{
			SyncSurface();
			inputHandler.ModifierKeys(currentMods);

			while (InputQueue.TryDequeue(out var a))
				a(inputHandler);

			InputFrame?.Invoke(inputHandler);
		}

		public static Action<IInputHandler> InputFrame;

		public string GetClipboardText()
		{
			try
			{
				var clipboard = (global::Android.Content.ClipboardManager)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ClipboardService);
				return clipboard?.PrimaryClip?.GetItemAt(0)?.Text ?? "";
			}
			catch
			{
				return "";
			}
		}

		public bool SetClipboardText(string text)
		{
			try
			{
				var clipboard = (global::Android.Content.ClipboardManager)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ClipboardService);
				if (clipboard != null)
					clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText("OpenRA", text);

				return true;
			}
			catch
			{
				return false;
			}
		}

		public void GrabWindowMouseFocus() { }

		public void ReleaseWindowMouseFocus() { }

		public IHardwareCursor CreateHardwareCursor(string name, Size size, byte[] data, int2 hotspot, bool pixelDouble)
		{
			// No hardware cursor support on Android: the engine falls back to software cursor
			// rendering when null is returned. The software cursor follows the mouse position
			// (hover) for mouse users, and the last touch position for touch users.
			return null;
		}

		public void SetHardwareCursor(IHardwareCursor cursor) { }

		public void SetWindowTitle(string title) { }

		public void SetRelativeMouseMode(bool mode) { }

		public void SetScaleModifier(float scale)
		{
			var oldEffective = EffectiveWindowScale;
			scaleModifier = scale;
			OnWindowScaleChanged(windowScale, oldEffective, windowScale, EffectiveWindowScale);
		}

		public void Dispose()
		{
			if (activeWindow == this)
				activeWindow = null;

			context.Dispose();
		}

		static AndroidPlatformWindow ActiveWindow => activeWindow;

		internal void Activate()
		{
			activeWindow = this;
		}
	}
}
