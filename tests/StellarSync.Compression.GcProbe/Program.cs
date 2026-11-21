using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using K4os.Compression.LZ4.Legacy;

internal static class Probe
{
    private static byte[]? allocationSink;
    private static byte[]? compressedSink;
    private const long PressureBytes = 256L * 1024 * 1024;
    private const double PressureSeconds = 2;

    private static byte[] Input(int length, int seed)
    {
        var result = new byte[length];
        var random = new Random(seed);
        random.NextBytes(result);
        // Three quarters of each 4 KiB block repeat; one quarter stays random.
        for (int offset = 0; offset < length; offset += 4096)
            for (int i = 0; i < Math.Min(3072, length - offset); i++)
                result[offset + i] = (byte)((i % 257) ^ ((offset / 4096) % 11));
        return result;
    }

    private readonly record struct Counters(long Allocated, TimeSpan Pause, int Gen0, int Gen1, int Gen2, TimeSpan Cpu)
    {
        public static Counters Get()
        {
            using var process = Process.GetCurrentProcess();
            return new(GC.GetTotalAllocatedBytes(false), GC.GetTotalPauseDuration(),
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), process.TotalProcessorTime);
        }
    }

    private static void Pressure(ManualResetEventSlim start)
    {
        start.Wait();
        var elapsed = Stopwatch.StartNew();
        long allocated = 0;
        int count = 0;
        while (allocated < PressureBytes)
        {
            var buffer = new byte[(count++ & 1) == 0 ? 32 * 1024 : 64 * 1024];
            buffer[0] = (byte)count;
            buffer[^1] = (byte)(count >> 8);
            Volatile.Write(ref allocationSink, buffer);
            GC.KeepAlive(buffer);
            allocated += buffer.Length;
            // Pace 2 MiB bursts across two seconds instead of finishing before HC starts.
            if (allocated % (2 * 1024 * 1024) < buffer.Length)
            {
                double delay = PressureSeconds * allocated / PressureBytes - elapsed.Elapsed.TotalSeconds;
                if (delay > 0) Thread.Sleep(TimeSpan.FromSeconds(delay));
            }
        }
    }

    private static void Run(string label, string? mode, bool pressure, byte[][] inputs)
    {
        Volatile.Write(ref allocationSink, null);
        Volatile.Write(ref compressedSink, null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var start = new ManualResetEventSlim();
        Task pressureTask = pressure
            ? Task.Factory.StartNew(() => Pressure(start), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default)
            : Task.CompletedTask;
        var before = Counters.Get();
        var elapsed = Stopwatch.StartNew();
        long compressedBytes = 0;
        double compressionSeconds = 0;
        double gcPauseDuringCalls = 0;
        var calls = new List<object>();
        start.Set();
        if (mode != null)
        {
            for (int i = 0; i < 4; i++)
            {
                // Equal input/call cadence in FAST and HC; idle does not consume CPU.
                double delay = i * 0.4 - elapsed.Elapsed.TotalSeconds;
                if (delay > 0) Thread.Sleep(TimeSpan.FromSeconds(delay));
                var input = inputs[i % inputs.Length];
                var pauseBefore = GC.GetTotalPauseDuration();
                var call = Stopwatch.StartNew();
                var output = mode == "HC" ? LZ4Wrapper.WrapHC(input, 0, input.Length)
                    : LZ4Wrapper.Wrap(input, 0, input.Length);
                call.Stop();
                var pauseDelta = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
                compressionSeconds += call.Elapsed.TotalSeconds;
                gcPauseDuringCalls += pauseDelta;
                compressedBytes += output.LongLength;
                Volatile.Write(ref compressedSink, output);
                GC.KeepAlive(output);
                calls.Add(new { inputMiB = input.Length / 1048576, ms = call.Elapsed.TotalMilliseconds, gcPauseMs = pauseDelta });
            }
        }
        pressureTask.GetAwaiter().GetResult();
        elapsed.Stop();
        var after = Counters.Get();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            label, mode, pressure, pressureMiB = pressure ? PressureBytes / 1048576 : 0,
            elapsedSeconds = elapsed.Elapsed.TotalSeconds, compressionSeconds, compressedBytes,
            gcPauseMs = (after.Pause - before.Pause).TotalMilliseconds, gcPauseDuringCalls,
            gen0 = after.Gen0 - before.Gen0, gen1 = after.Gen1 - before.Gen1, gen2 = after.Gen2 - before.Gen2,
            allocatedMiB = (after.Allocated - before.Allocated) / 1048576d,
            cpuSeconds = (after.Cpu - before.Cpu).TotalSeconds, calls,
        }));
        GC.KeepAlive(inputs);
    }

    public static void Main()
    {
        var inputs = new[] { Input(16 * 1024 * 1024, 101), Input(32 * 1024 * 1024, 103) };
        var warm = Input(1024 * 1024, 107);
        for (int i = 0; i < 4; i++)
        {
            GC.KeepAlive(LZ4Wrapper.Wrap(warm, 0, warm.Length));
            GC.KeepAlive(LZ4Wrapper.WrapHC(warm, 0, warm.Length));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(),
            serverGc = GCSettings.IsServerGC, latencyMode = GCSettings.LatencyMode.ToString(),
            library = typeof(LZ4Wrapper).Assembly.FullName,
            description = "Four calls 16,32,16,32 MiB at 0,0.4,0.8,1.2 seconds; independent 256MiB short-lived 32/64KiB allocations paced over2seconds; only pre-run forced GC." }));
        Run("pressure-only", null, true, inputs);
        Run("FAST-alone", "FAST", false, inputs);
        Run("HC-alone", "HC", false, inputs);
        Run("FAST-pressure-1", "FAST", true, inputs);
        Run("HC-pressure-1", "HC", true, inputs);
        Run("HC-pressure-2", "HC", true, inputs);
        Run("FAST-pressure-2", "FAST", true, inputs);
    }
}
