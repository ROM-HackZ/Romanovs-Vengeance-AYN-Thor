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

using System.Collections.Generic;
using AKeycode = Android.Views.Keycode;

namespace OpenRA.Platforms.Android
{
	/// <summary>
	/// Maps Android KeyEvent keycodes to OpenRA keycodes (which mirror SDL_Keycode values).
	/// Letters/digits/punctuation map to their (unshifted) ASCII keysym, matching SDL's
	/// behavior of reporting the base key with modifiers tracked separately.
	/// </summary>
	static class AndroidKeyMap
	{
		public static readonly IReadOnlyDictionary<AKeycode, Keycode> Map = Build();

		static Dictionary<AKeycode, Keycode> Build()
		{
			var map = new Dictionary<AKeycode, Keycode>
			{
				{ AKeycode.Back, Keycode.ESCAPE }, // Android back button acts as Escape
				{ AKeycode.Escape, Keycode.ESCAPE },
				{ AKeycode.Enter, Keycode.RETURN },
				{ AKeycode.NumpadEnter, Keycode.KP_ENTER },
				{ AKeycode.DpadUp, Keycode.UP },
				{ AKeycode.DpadDown, Keycode.DOWN },
				{ AKeycode.DpadLeft, Keycode.LEFT },
				{ AKeycode.DpadRight, Keycode.RIGHT },
				{ AKeycode.Del, Keycode.BACKSPACE },
				{ AKeycode.ForwardDel, Keycode.DELETE },
				{ AKeycode.Tab, Keycode.TAB },
				{ AKeycode.Space, Keycode.SPACE },
				{ AKeycode.MoveHome, Keycode.HOME },
				{ AKeycode.MoveEnd, Keycode.END },
				{ AKeycode.PageUp, Keycode.PAGEUP },
				{ AKeycode.PageDown, Keycode.PAGEDOWN },
				{ AKeycode.Insert, Keycode.INSERT },
				{ AKeycode.CtrlLeft, Keycode.LCTRL },
				{ AKeycode.CtrlRight, Keycode.RCTRL },
				{ AKeycode.ShiftLeft, Keycode.LSHIFT },
				{ AKeycode.ShiftRight, Keycode.RSHIFT },
				{ AKeycode.AltLeft, Keycode.LALT },
				{ AKeycode.AltRight, Keycode.RALT },
				{ AKeycode.MetaLeft, Keycode.LGUI },
				{ AKeycode.MetaRight, Keycode.RGUI },
				{ AKeycode.CapsLock, Keycode.CAPSLOCK },
				{ AKeycode.NumLock, Keycode.NUMLOCKCLEAR },
				{ AKeycode.ScrollLock, Keycode.SCROLLLOCK },
				{ AKeycode.Sysrq, Keycode.PRINTSCREEN },
				{ AKeycode.Break, Keycode.PAUSE },
				{ AKeycode.Menu, Keycode.MENU },
				{ AKeycode.Minus, Keycode.MINUS },
				{ AKeycode.Equals, Keycode.EQUALS },
				{ AKeycode.LeftBracket, Keycode.LEFTBRACKET },
				{ AKeycode.RightBracket, Keycode.RIGHTBRACKET },
				{ AKeycode.Backslash, Keycode.BACKSLASH },
				{ AKeycode.Semicolon, Keycode.SEMICOLON },
				{ AKeycode.Apostrophe, Keycode.QUOTE },
				{ AKeycode.Comma, Keycode.COMMA },
				{ AKeycode.Period, Keycode.PERIOD },
				{ AKeycode.Slash, Keycode.SLASH },
				{ AKeycode.Grave, Keycode.BACKQUOTE },
				{ AKeycode.NumpadAdd, Keycode.KP_PLUS },
				{ AKeycode.NumpadSubtract, Keycode.KP_MINUS },
				{ AKeycode.NumpadMultiply, Keycode.KP_MULTIPLY },
				{ AKeycode.NumpadDivide, Keycode.KP_DIVIDE },
				{ AKeycode.NumpadDot, Keycode.KP_PERIOD },
				{ AKeycode.NumpadComma, Keycode.KP_COMMA },
				{ AKeycode.NumpadEquals, Keycode.KP_EQUALS },
			};

			// Contiguous ranges: A-Z, 0-9, F1-F12, numpad 0-9
			for (var i = 0; i < 26; i++)
				map[(AKeycode)((int)AKeycode.A + i)] = (Keycode)((int)Keycode.A + i);

			for (var i = 0; i < 10; i++)
				map[(AKeycode)((int)AKeycode.Num0 + i)] = (Keycode)((int)Keycode.NUMBER_0 + i);

			for (var i = 0; i < 12; i++)
				map[(AKeycode)((int)AKeycode.F1 + i)] = (Keycode)((int)Keycode.F1 + i);

			// OpenRA keypad digits follow SDL_Keycode: KP_1..KP_9 are contiguous, KP_0 is separate
			for (var i = 1; i < 10; i++)
				map[(AKeycode)((int)AKeycode.Numpad1 + (i - 1))] = (Keycode)((int)Keycode.KP_1 + (i - 1));

			map[AKeycode.Numpad0] = Keycode.KP_0;

			return map;
		}
	}
}
