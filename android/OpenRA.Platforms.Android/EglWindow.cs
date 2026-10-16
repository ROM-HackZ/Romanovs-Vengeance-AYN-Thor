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
using System.Runtime.InteropServices;
using Android.Opengl;
using Android.Views;

namespace OpenRA.Platforms.Android
{
	/// <summary>
	/// Minimal EGL wrapper for an Android Surface, plus the GL entry point resolver
	/// installed into OpenGL.GetProcAddressOverride.
	/// </summary>
	sealed class EglWindow : IDisposable
	{
		[DllImport("libEGL.so", EntryPoint = "eglGetProcAddress")]
		static extern IntPtr EglGetProcAddress(string procname);

		[DllImport("libdl.so", EntryPoint = "dlopen")]
		static extern IntPtr DlOpen(string filename, int flags);

		[DllImport("libdl.so", EntryPoint = "dlsym")]
		static extern IntPtr DlSym(IntPtr handle, string symbol);

		const int RTLD_NOW = 2;

		static IntPtr glesLibrary;

		/// <summary>
		/// Resolves GLES entry points. Core functions are exported directly by libGLESv3.so;
		/// extension functions (e.g. KHR_debug) require eglGetProcAddress.
		/// </summary>
		public static IntPtr GetProcAddress(string name)
		{
			if (glesLibrary == IntPtr.Zero)
			{
				glesLibrary = DlOpen("libGLESv3.so", RTLD_NOW);
				if (glesLibrary == IntPtr.Zero)
					glesLibrary = DlOpen("libGLESv2.so", RTLD_NOW);
			}

			var proc = glesLibrary != IntPtr.Zero ? DlSym(glesLibrary, name) : IntPtr.Zero;
			if (proc == IntPtr.Zero)
				proc = EglGetProcAddress(name);

			return proc;
		}

		/// <summary>
		/// Builds a GL extension query for OpenGL.ExtensionSupportedOverride using the
		/// space-separated GL_EXTENSIONS string (valid on OpenGL ES).
		/// </summary>
		public static Func<string, bool> CreateExtensionChecker()
		{
			const int GL_EXTENSIONS = 0x1F03;
			var getString = Marshal.GetDelegateForFunctionPointer<GetStringDelegate>(GetProcAddress("glGetString"));
			var extensions = Marshal.PtrToStringAnsi(getString(GL_EXTENSIONS)) ?? "";
			var set = new HashSet<string>(extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries));
			return set.Contains;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate IntPtr GetStringDelegate(int name);

		EGLDisplay display;
		EGLContext context;
		EGLSurface surface;
		EGLSurface parkingSurface;
		EGLConfig config;
		bool vsync = true;

		public EglWindow(Surface windowSurface)
		{
			display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
			if (display == EGL14.EglNoDisplay)
				throw new InvalidOperationException("eglGetDisplay failed");

			var version = new int[2];
			if (!EGL14.EglInitialize(display, version, 0, version, 1))
				throw new InvalidOperationException("eglInitialize failed");

			var configAttribs = new[]
			{
				EGL14.EglSurfaceType, EGL14.EglWindowBit | EGL14.EglPbufferBit,
				EGL14.EglRenderableType, EGLExt.EglOpenglEs3BitKhr,
				EGL14.EglRedSize, 8,
				EGL14.EglGreenSize, 8,
				EGL14.EglBlueSize, 8,
				EGL14.EglAlphaSize, 0,
				EGL14.EglDepthSize, 0,
				EGL14.EglStencilSize, 0,
				EGL14.EglNone
			};

			var configs = new EGLConfig[1];
			var numConfigs = new int[1];
			if (!EGL14.EglChooseConfig(display, configAttribs, 0, configs, 0, 1, numConfigs, 0) || numConfigs[0] < 1)
				throw new InvalidOperationException("eglChooseConfig failed: no GLES3-compatible config");

			config = configs[0];

			var contextAttribs = new[] { EGL14.EglContextClientVersion, 3, EGL14.EglNone };
			context = EGL14.EglCreateContext(display, config, EGL14.EglNoContext, contextAttribs, 0);
			if (context == null || context == EGL14.EglNoContext)
				throw new InvalidOperationException($"eglCreateContext failed: 0x{EGL14.EglGetError():x}");

			parkingSurface = EGL14.EglCreatePbufferSurface(display, config,
				new[] { EGL14.EglWidth, 1, EGL14.EglHeight, 1, EGL14.EglNone }, 0);
			if (parkingSurface == null || parkingSurface == EGL14.EglNoSurface)
				throw new InvalidOperationException($"eglCreatePbufferSurface failed: 0x{EGL14.EglGetError():x}");

			CreateSurface(windowSurface);

			EGL14.EglSwapInterval(display, 1);
		}

		void CreateSurface(Surface windowSurface)
		{
			surface = EGL14.EglCreateWindowSurface(display, config, windowSurface, new[] { EGL14.EglNone }, 0);
			if (surface == null || surface == EGL14.EglNoSurface)
				throw new InvalidOperationException($"eglCreateWindowSurface failed: 0x{EGL14.EglGetError():x}");

			if (!EGL14.EglMakeCurrent(display, surface, surface, context))
				throw new InvalidOperationException($"eglMakeCurrent failed: 0x{EGL14.EglGetError():x}");
		}

		/// <summary>Park the context offscreen while Android replaces its window.</summary>
		public void ReleaseSurface()
		{
			// Logic ticks can upload palettes and textures even while rendering is
			// suspended. Keep a valid current context for those uploads.
			if (!EGL14.EglMakeCurrent(display, parkingSurface, parkingSurface, context))
				throw new InvalidOperationException($"eglMakeCurrent (park) failed: 0x{EGL14.EglGetError():x}");

			if (surface != null && surface != EGL14.EglNoSurface)
				EGL14.EglDestroySurface(display, surface);

			surface = null;
		}

		/// <summary>Binds a new window surface to the existing context (app resume / rotation).</summary>
		public void RecreateSurface(Surface windowSurface)
		{
			ReleaseSurface();
			CreateSurface(windowSurface);
			EGL14.EglSwapInterval(display, vsync ? 1 : 0);
		}

		public void SwapBuffers()
		{
			EGL14.EglSwapBuffers(display, surface);
		}

		public void SetVSyncEnabled(bool enabled)
		{
			vsync = enabled;
			EGL14.EglSwapInterval(display, enabled ? 1 : 0);
		}

		public void Dispose()
		{
			if (display != null && display != EGL14.EglNoDisplay)
			{
				EGL14.EglMakeCurrent(display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);

				if (surface != null && surface != EGL14.EglNoSurface)
					EGL14.EglDestroySurface(display, surface);
				if (parkingSurface != null && parkingSurface != EGL14.EglNoSurface)
					EGL14.EglDestroySurface(display, parkingSurface);

				if (context != null && context != EGL14.EglNoContext)
					EGL14.EglDestroyContext(display, context);

				EGL14.EglTerminate(display);
			}

			surface = null;
			parkingSurface = null;
			context = null;
			display = null;
		}
	}
}
