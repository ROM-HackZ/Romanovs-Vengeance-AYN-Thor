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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Android.App;
using Android.Content.PM;
using Android.OS;
using ALog = Android.Util.Log;
using Android.Views;
using Android.Hardware.Display;
using Android.Widget;
using System.Linq;
using OpenRA.Platforms.Android;

[assembly: Application(LargeHeap = true, Icon = "@mipmap/ic_launcher")]
[assembly: UsesPermission("android.permission.INTERNET")]

namespace OpenRA.AndroidApp
{
	[Activity(
		Label = "Romanov's Vengeance - Thor",
		Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
		MainLauncher = true,
		ScreenOrientation = ScreenOrientation.SensorLandscape,
		ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.Keyboard,
		LaunchMode = LaunchMode.SingleInstance)]
	public class MainActivity : Activity, ISurfaceHolderCallback
	{
		const string Tag = "OpenRA";

		Thread engineThread;
		ThorController controller;
		ThorTouch touch;
		Presentation deck;

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			Window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
			Window.AddFlags(WindowManagerFlags.KeepScreenOn);

			var view = new SurfaceView(this);
			view.Holder.AddCallback(this);
			view.Focusable = true;
			view.FocusableInTouchMode = true;

			// Touch events cover taps, drags and the emulator's host mouse (which arrives as
			// touchscreen input). GenericMotion covers mouse hover, secondary buttons and the
			// scroll wheel (Bluetooth/USB mouse, or touchpad on a physical device).
			view.Touch += (_, e) =>
			{
				touch?.Handle(e.Event, view.Width, view.Height);
				e.Handled = true;
			};

			view.GenericMotion += (_, e) =>
			{
				AndroidPlatformWindow.EnqueueMotion(e.Event);
				e.Handled = true;
			};

			controller = new ThorController(Path.Combine(GetExternalFilesDir(null).AbsolutePath, "thor-state.json"));
			touch = new ThorTouch(controller);
			AndroidPlatformWindow.InputFrame = controller.Frame;
			var layout = new FrameLayout(this);
			layout.AddView(view, new FrameLayout.LayoutParams(-1, -1));
			layout.AddView(new ThorWheel(this, controller), new FrameLayout.LayoutParams(-1, -1));
			SetContentView(layout);
			var displays = (DisplayManager)GetSystemService(DisplayService);
			var secondary = displays.GetDisplays().FirstOrDefault(d => d.DisplayId != Display.DefaultDisplay);
			if (secondary != null)
			{
				deck = new ThorPresentation(this, secondary, controller);
				deck.SetContentView(new ThorDeck(deck.Context, controller));
				deck.Show();
			}
			EnableImmersiveMode();

			AndroidPlatformWindow.SetDisplayDensity(Resources.DisplayMetrics.Density);

			engineThread = new Thread(EngineMain) { IsBackground = true, Name = "OpenRAEngine" };
			engineThread.Start();
		}

		/// <summary>
		/// Hides the status bar and navigation bar / gesture pill so the game owns the
		/// entire screen. Immersive-sticky re-hides them after transient system gestures.
		/// </summary>
		void EnableImmersiveMode() => EnterImmersiveMode(Window);

		internal static void EnterImmersiveMode(Window window)
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
			{
				var c = window.InsetsController;
				c.Hide(WindowInsets.Type.SystemBars());
				c.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
			}
			else
			{
				// Pre-API 30 fallback
				window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
					SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen |
					SystemUiFlags.HideNavigation | SystemUiFlags.LayoutStable |
					SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation);
			}
		}

		public override void OnWindowFocusChanged(bool hasFocus)
		{
			base.OnWindowFocusChanged(hasFocus);
			if (hasFocus)
				EnableImmersiveMode();
		}

		public void SurfaceCreated(ISurfaceHolder holder)
		{
			var frame = holder.SurfaceFrame;
			ALog.Info(Tag, $"Surface created: {frame.Width()}x{frame.Height()}");
			AndroidPlatformWindow.SetSurface(holder.Surface, frame.Width(), frame.Height());
		}

		public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
		{
			ALog.Info(Tag, $"Surface changed: {width}x{height}");
			AndroidPlatformWindow.SetSurface(holder.Surface, width, height);
		}

		public void SurfaceDestroyed(ISurfaceHolder holder)
		{
			ALog.Info(Tag, "Surface destroyed");
			AndroidPlatformWindow.ClearSurface();
		}

		/// <summary>
		/// Routes all keyboard input (physical keyboard, emulator host keyboard, Android back
		/// button) into the engine. Unmapped keys (volume, etc.) fall through to the system.
		/// The soft keyboard is intentionally not shown; text entry works via physical keys.
		/// </summary>
		public override bool DispatchKeyEvent(KeyEvent e)
		{
			if (controller?.Key(e) == true) return true;
			if (AndroidPlatformWindow.EnqueueKey(e))
				return true;

			return base.DispatchKeyEvent(e);
		}

		public override bool DispatchGenericMotionEvent(MotionEvent e)
		{
			if (controller?.Motion(e) == true) return true;
			return base.DispatchGenericMotionEvent(e);
		}

		protected override void OnPause()
		{
			touch?.Cancel();
			controller?.Suspend();
			deck?.Hide();
			base.OnPause();
		}

		protected override void OnResume()
		{
			base.OnResume();
			controller?.Resume();
			deck?.Show();
		}

		protected override void OnDestroy()
		{
			touch?.Dispose();
			controller?.Release();
			deck?.Dismiss();
			Game.Exit();
			base.OnDestroy();
		}

		void EngineMain()
		{
			// Route engine Console output to logcat (mono-stdout is not visible by default).
			Console.SetOut(new LogcatTextWriter(Tag, false));
			Console.SetError(new LogcatTextWriter(Tag, true));

			try
			{
				ALog.Info(Tag, "Engine thread starting");
				InstallDllImportResolvers();

				var engineDir = ExtractGameFiles();

				Game.RegisterPlatformFactory(() => new AndroidPlatform());

				// Force-load the mod assemblies: they are bundled in the APK and resolved
				// by name (see ObjectCreator), never loaded from disk on Android.
				foreach (var name in new[] { "OpenRA.Mods.Common", "OpenRA.Mods.Cnc", "OpenRA.Mods.D2k", "OpenRA.Mods.AS", "OpenRA.Mods.RA2" })
				{
					Assembly.Load(new AssemblyName(name));
					ALog.Info(Tag, $"Loaded assembly {name}");
				}

				var supportDir = Path.Combine(GetExternalFilesDir(null).AbsolutePath, "support");
				Directory.CreateDirectory(supportDir);

				var args = new[]
				{
					"Game.Mod=rv",
					"Game.UseClassicMouseStyle=true",
					"Game.ViewportEdgeScroll=false",
					"Graphics.GLProfile=Embedded",
					"Graphics.DisableHardwareCursors=true",
					"Debug.PerfText=false",
					"Engine.EngineDir=" + engineDir,
					"Engine.SupportDir=" + supportDir,
				};

				ALog.Info(Tag, "Calling Game.InitializeAndRun");
				Game.InitializeAndRun(args);
				ALog.Info(Tag, "Game exited cleanly");
				RunOnUiThread(Finish);
			}
			catch (Exception e)
			{
				ALog.Error(Tag, "Fatal engine error: " + e);
				Console.WriteLine("FATAL: " + e);
				RunOnUiThread(() => new AlertDialog.Builder(this).SetTitle("The game stopped")
					.SetMessage(e.Message).SetPositiveButton("Close", (_, _) => Finish()).Show());
			}
		}

		/// <summary>
		/// Maps the DllImport names used by the OpenRA bindings to the bundled native libs.
		/// </summary>
		static void InstallDllImportResolvers()
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				{ "freetype6", "libfreetype6.so" },
				{ "lua51", "liblua51.so" },
				{ "soft_oal", "libsoft_oal.so" },
			};

			IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
			{
				if (!map.TryGetValue(libraryName, out var file))
					return IntPtr.Zero;

				try
				{
					return NativeLibrary.Load(file);
				}
				catch (Exception e)
				{
					ALog.Error(Tag, $"Failed to load native library {file}: {e.Message}");
					throw;
				}
			}

			foreach (var asmName in new[] { "Eluant", "OpenAL-CS", "OpenRA.Platforms.Android" })
				NativeLibrary.SetDllImportResolver(Assembly.Load(new AssemblyName(asmName)), Resolve);
		}

		/// <summary>
		/// Extracts the bundled game data (mods, shaders, VERSION) from APK assets to the
		/// app files dir on first run (or when the app version changes).
		/// </summary>
		string ExtractGameFiles()
		{
			var dest = Path.Combine(FilesDir.AbsolutePath, "openra");
			var stamp = Path.Combine(FilesDir.AbsolutePath, "openra.stamp");
			var version = PackageManager.GetPackageInfo(PackageName, 0).VersionName;

			if (File.Exists(stamp) && File.ReadAllText(stamp) == version && Directory.Exists(dest))
			{
				ALog.Info(Tag, $"Game files already extracted at {dest}");
				return dest;
			}

			ALog.Info(Tag, $"Extracting game files to {dest}...");
			if (Directory.Exists(dest))
				Directory.Delete(dest, true);

			CopyAssetTree("openra", dest);
			File.WriteAllText(stamp, version);

			ALog.Info(Tag, "Extraction complete");
			return dest;
		}

		void CopyAssetTree(string assetPath, string destDir)
		{
			Directory.CreateDirectory(destDir);

			foreach (var entry in Assets.List(assetPath))
			{
				var childAsset = assetPath + "/" + entry;
				var childDest = Path.Combine(destDir, entry);

				if (Assets.List(childAsset).Length > 0)
				{
					CopyAssetTree(childAsset, childDest);
				}
				else
				{
					using var src = Assets.Open(childAsset);
					using var dst = File.Create(childDest);
					src.CopyTo(dst);
				}
			}
		}
	}

	sealed class ThorPresentation : Presentation
	{
		readonly ThorController controller;
		public ThorPresentation(Android.Content.Context context, Display display, ThorController controller) : base(context, display)
		{ this.controller = controller; }
		protected override void OnStart()
		{
			base.OnStart();
			Window.AddFlags(WindowManagerFlags.Fullscreen);
			MainActivity.EnterImmersiveMode(Window);
		}
		public override void OnWindowFocusChanged(bool hasFocus)
		{
			base.OnWindowFocusChanged(hasFocus);
			if (hasFocus) MainActivity.EnterImmersiveMode(Window);
		}
		public override bool DispatchKeyEvent(KeyEvent e) => controller.Key(e) || AndroidPlatformWindow.EnqueueKey(e) || base.DispatchKeyEvent(e);
		public override bool DispatchGenericMotionEvent(MotionEvent e) => controller.Motion(e) || base.DispatchGenericMotionEvent(e);
	}

	/// <summary>Line-buffered TextWriter that forwards to Android.Util.Log.</summary>
	sealed class LogcatTextWriter : TextWriter
	{
		readonly string tag;
		readonly bool isError;
		readonly System.Text.StringBuilder line = new();

		public LogcatTextWriter(string tag, bool isError)
		{
			this.tag = tag;
			this.isError = isError;
		}

		public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

		public override void Write(char value)
		{
			lock (line)
			{
				if (value == '\n')
					FlushLine();
				else if (value != '\r')
					line.Append(value);
			}
		}

		public override void Flush()
		{
			lock (line)
				if (line.Length > 0)
					FlushLine();
		}

		void FlushLine()
		{
			var text = line.ToString();
			line.Clear();
			if (isError)
				ALog.Error(tag, text);
			else
				ALog.Info(tag, text);
		}
	}

}
