#!/bin/bash
# Builds the native libraries OpenRA needs on Android (Lua 5.1, FreeType, OpenAL-soft)
# for arm64-v8a using the NDK. Output: android/native/libs/<abi>/lib*.so
set -euo pipefail
cd "$(dirname "$0")"

SDK="${ANDROID_HOME:-$HOME/Android/Sdk}"
NDK="${ANDROID_NDK_HOME:-$SDK/ndk/27.2.12479018}"
CMAKE="${CMAKE:-$HOME/.local/bin/cmake}"
API=24
SRC=src
OUT=libs

# Request 16 KB alignment explicitly, independent of NDK defaults.
PAGESIZE_FLAG="-Wl,-z,max-page-size=16384"

for ABI in arm64-v8a; do
	mkdir -p "$OUT/$ABI"

	# --- Lua 5.1 ---
	case "$ABI" in
		arm64-v8a) TARGET=aarch64-linux-android ;;
		x86_64)    TARGET=x86_64-linux-android ;;
	esac
	CC="$NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/${TARGET}${API}-clang"
	LUASRC=$(ls "$SRC"/lua-5.1.5/src/*.c | grep -v -e '/lua\.c$' -e '/luac\.c$' -e '/print\.c$')
	"$CC" -O2 -fPIC -shared -DLUA_USE_POSIX $PAGESIZE_FLAG -o "$OUT/$ABI/liblua51.so" $LUASRC -lm -ldl

	# --- FreeType ---
	FTBUILD="build/freetype-$ABI"
	"$CMAKE" -S "$SRC/freetype-2.13.2" -B "$FTBUILD" -G Ninja \
		-DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
		-DCMAKE_BUILD_TYPE=Release \
		-DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
		-DCMAKE_EXE_LINKER_FLAGS="$PAGESIZE_FLAG" \
		-DCMAKE_SHARED_LINKER_FLAGS="$PAGESIZE_FLAG" \
		-DANDROID_ABI="$ABI" -DANDROID_PLATFORM="android-$API" \
		-DBUILD_SHARED_LIBS=ON \
		-DFT_DISABLE_ZLIB=TRUE -DFT_DISABLE_BZIP2=TRUE -DFT_DISABLE_PNG=TRUE \
		-DFT_DISABLE_HARFBUZZ=TRUE -DFT_DISABLE_BROTLI=TRUE
	"$CMAKE" --build "$FTBUILD"
	cp "$FTBUILD/libfreetype.so" "$OUT/$ABI/libfreetype6.so"

	# --- OpenAL-soft ---
	OALBUILD="build/openal-$ABI"
	"$CMAKE" -S "$SRC/openal-soft-1.23.1" -B "$OALBUILD" -G Ninja \
		-DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
		-DCMAKE_BUILD_TYPE=Release \
		-DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
		-DCMAKE_EXE_LINKER_FLAGS="$PAGESIZE_FLAG" \
		-DCMAKE_SHARED_LINKER_FLAGS="$PAGESIZE_FLAG" \
		-DANDROID_ABI="$ABI" -DANDROID_PLATFORM="android-$API" \
		-DLIBTYPE=SHARED \
		-DALSOFT_UTILS=OFF -DALSOFT_EXAMPLES=OFF -DALSOFT_TESTS=OFF \
		-DALSOFT_INSTALL=OFF -DALSOFT_INSTALL_CONFIG=OFF
	"$CMAKE" --build "$OALBUILD"
	cp "$OALBUILD/libopenal.so" "$OUT/$ABI/libsoft_oal.so"
done

echo "Native libs built:"
find "$OUT" -name '*.so' | sort

# Verify 16 KB (0x4000) LOAD segment alignment for all produced libraries.
READELF="$NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf"
FAIL=0
for so in $(find "$OUT" -name '*.so' | sort); do
	ALIGN=$("$READELF" -lW "$so" | awk '$1 == "LOAD" { print $NF }' | sort -u | tr '\n' ' ')
	echo "$so LOAD alignments: $ALIGN"
	for a in $ALIGN; do
		if [ "$((a < 16384))" -eq 1 ]; then
			echo "ERROR: $so has LOAD alignment $a < 16384"
			FAIL=1
		fi
	done
done
[ "$FAIL" -eq 0 ] && echo "All native libs are 16 KB page-aligned." || exit 1
