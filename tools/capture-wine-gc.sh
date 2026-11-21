#!/usr/bin/env bash
set -euo pipefail

# Scope startup tracing to this launcher and its children. No persistent launcher,
# Wine-prefix, runtime, or plugin configuration is modified.
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
    printf '%s\n' 'Usage: bash tools/capture-wine-gc.sh [-- launcher-command [arguments...]]'
    printf '%s\n' 'Default launcher: /usr/bin/xivlauncher-rb'
    exit 0
fi
if [[ ${1:-} == -- ]]; then shift; fi
if (( $# == 0 )); then set -- /usr/bin/xivlauncher-rb; fi

if ! command -v "$1" >/dev/null 2>&1; then
    printf 'Launcher command not found: %s\n' "$1" >&2
    exit 1
fi

# A relative name works in both native Linux .NET and Windows .NET under Wine.
# Each runtime writes in its working directory; {pid} separates their traces.
trace_file="stellar-sync-gc-$(date -u +%Y%m%dT%H%M%SZ)-{pid}.nettrace"
printf '%s\n' 'Start with the game and launcher fully closed.'
printf '%s\n' 'Reproduce one short upload with stutters, then exit the game normally.'
printf 'Trace filename pattern: %s\n' "$trace_file"
printf '%s\n' 'Look in the game directory and launcher/Dalamud working directories.'
printf '%s\n' 'Native launcher processes may produce additional traces with different PIDs.'
printf '%s\n' 'GC events only; 64 MiB internal buffer. Streaming output has no fixed file-size cap.'

exec env \
    DOTNET_EnableEventPipe=1 \
    DOTNET_EventPipeConfig='Microsoft-Windows-DotNETRuntime:1:4' \
    DOTNET_EventPipeOutputPath="$trace_file" \
    DOTNET_EventPipeCircularMB=40 \
    DOTNET_EventPipeOutputStreaming=1 \
    "$@"
