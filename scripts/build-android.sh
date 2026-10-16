#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="${THOR_DOTNET_ROOT:-$HOME/.local/share/thor-dotnet}:$PATH"
if [[ "${THOR_LINUX_BUILD:-0}" != 1 ]]; then
  python3 scripts/prepare-engine.py
  if [[ "$PWD" == /mnt/* ]]; then
    echo 'From Windows, use: python scripts/build-android.py'
    echo 'The build stages inputs onto the Linux filesystem before invoking .NET.'
    exit 1
  fi
fi
mkdir -p artifacts logs
if [[ ! -f android/native/libs/arm64-v8a/liblua51.so || ! -f android/native/libs/arm64-v8a/libfreetype6.so || ! -f android/native/libs/arm64-v8a/libsoft_oal.so ]]; then
  python3 scripts/fetch-native-sources.py
  bash android/native/build-natives.sh
fi
# Regenerate Android's Java/JNI bindings together. Reusing a partially generated
# typemap after a failed compile can produce an APK whose Activity cannot start.
python3 - <<'PY'
from pathlib import Path
import shutil
root = Path.cwd().resolve()
for name in ('bin', 'obj'):
    target = (root / 'android' / 'OpenRA.Android' / name).resolve()
    if not target.is_relative_to(root):
        raise SystemExit('Build output escapes project root')
    if target.exists():
        shutil.rmtree(target)
PY
dotnet build android/OpenRA.Android/OpenRA.Android.csproj -c Release -m:1 --disable-build-servers \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="${JAVA_HOME:-/usr/lib/jvm/java-17-openjdk-amd64}" \
  -p:RuntimeIdentifier=android-arm64 "$@" > logs/build-android.log 2>&1 || { tail -n 70 logs/build-android.log; exit 1; }
cp android/OpenRA.Android/bin/Release/net10.0-android/android-arm64/org.romanovsthor.app-Signed.apk artifacts/Romanovs-Thor.apk
python3 scripts/record-build.py
echo "APK: artifacts/Romanovs-Thor.apk"
