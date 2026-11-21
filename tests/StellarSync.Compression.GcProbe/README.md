# Standalone GC-pressure probe

Run from the client repository root:

```bash
dotnet restore --source /tmp tests/StellarSync.Compression.GcProbe/StellarSync.Compression.GcProbe.csproj
dotnet run -c Release --no-restore --project tests/StellarSync.Compression.GcProbe
```

The offline restore assumes the client's K4os 1.3.8 packages are already cached.
Otherwise use the normal NuGet source. This is a standalone synthetic experiment;
it does not attach to the game or modify plugin GC settings.

The probe warms both legacy FAST and high-compression encoders, then compares
pressure-only, encoder-only, and encoder-plus-pressure runs. Four calls use
16/32/16/32 MiB inputs at a fixed cadence. An independent task allocates 256 MiB
of short-lived 32/64 KiB arrays in bursts across about two seconds. Explicit
collections happen only before runs, outside the measured interval.

Each result reports GC pause duration, generation counts, allocation totals,
CPU time, and encoding-call durations. Pressure-only and counterbalanced repeats
help distinguish compression cost from allocation pressure. FAST is an experimental
comparison here; the plugin continues to use its existing high-compression level.

The Linux/.NET 10 experiment did not reproduce the multi-second GC pauses from
the game logs. This does not rule out suspension or collection problems with the
game's runtime, data, other plugins, or workload. See [results.jsonl](results.jsonl)
for a recorded standalone run. The findings do not establish that allocation reuse
eliminates game stutters.
