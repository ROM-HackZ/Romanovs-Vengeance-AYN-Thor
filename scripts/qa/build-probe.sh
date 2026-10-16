#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
sdk="${ANDROID_HOME:-$HOME/Android/Sdk}"
out="${THOR_QA_BUILD:-$HOME/.cache/romanovs-thor/qa}"
mkdir -p "$out/classes" "$root/artifacts"
javac -source 8 -target 8 -classpath "$sdk/platforms/android-36/android.jar" -d "$out/classes" "$root/scripts/qa/ThorInputProbe.java"
"$sdk/build-tools/36.0.0/d8" --lib "$sdk/platforms/android-36/android.jar" --output "$out/probe.jar" "$out/classes/org/romanovsthor/qa/ThorInputProbe.class"
cp "$out/probe.jar" "$root/artifacts/thor-input-probe.jar"
