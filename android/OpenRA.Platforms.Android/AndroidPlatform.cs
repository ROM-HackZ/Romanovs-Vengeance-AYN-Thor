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
using OpenRA.Platforms.Default;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	/// <summary>
	/// Android platform-integration library, registered with Game.RegisterPlatformFactory
	/// by the app before initialization (assemblies are bundled in the APK and cannot be
	/// loaded from disk the way desktop platforms are).
	/// </summary>
	public class AndroidPlatform : IPlatform
	{
		public IPlatformWindow CreateWindow(
			Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			return new AndroidPlatformWindow(size, windowMode, scaleModifier, vertexBatchSize, indexBatchSize, videoDisplay, profile);
		}

		public ISoundEngine CreateSound(string device)
		{
			try
			{
				return new OpenAlSoundEngine(device);
			}
			catch (Exception e)
			{
				Log.Write("sound", "Failed to initialize OpenAL device. Error was");
				Log.Write("sound", e.ToString());
				return new DummySoundEngine();
			}
		}

		public IFont CreateFont(byte[] data)
		{
			return new FreeTypeFont(data);
		}
	}
}
