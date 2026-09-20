namespace Atlas;

/// <summary>run: dotnet run -- --flighttest</summary>
public static class FlightTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    public static void Run()
    {
        const float vw = 1400;

        // a long traverse at the same zoom should arc out and come back
        var far = Flight.To(vw, 0, 0, 4f, 20000, 15000, 4f)!;
        far.Sample(0, out var x0, out var y0, out var s0);
        far.Sample(far.DurationMs, out var x1, out var y1, out var s1);
        far.Sample(far.DurationMs / 2, out _, out _, out var sMid);

        Check(Math.Abs(x0) < 1 && Math.Abs(y0) < 1, $"starts at origin, got {x0},{y0}");
        Check(Math.Abs(s0 - 4f) < 0.01f, $"starts at zoom 4, got {s0}");
        Check(Math.Abs(x1 - 20000) < 1 && Math.Abs(y1 - 15000) < 1, $"ends on target, got {x1},{y1}");
        Check(Math.Abs(s1 - 4f) < 0.01f, $"ends at zoom 4, got {s1}");
        Check(sMid < s0 * 0.5f, $"zooms out on the way, mid zoom {sMid} vs {s0}");

        // pure zoom, no travel
        var zoom = Flight.To(vw, 500, 500, 0.05f, 500, 500, 5f)!;
        zoom.Sample(zoom.DurationMs, out var zx, out var zy, out var zs);
        Check(Math.Abs(zx - 500) < 0.5f && Math.Abs(zy - 500) < 0.5f, $"stays put, got {zx},{zy}");
        Check(Math.Abs(zs - 5f) / 5f < 0.01f, $"reaches zoom 5, got {zs}");

        // monotonic progress, never jumps backwards
        float prev = -1;
        bool monotonic = true;
        for (int i = 0; i <= 50; i++)
        {
            far.Sample(far.DurationMs * i / 50.0, out var px, out _, out _);
            if (px < prev - 0.5f) monotonic = false;
            prev = px;
        }
        Check(monotonic, "x advances without reversing");

        Check(far.DurationMs is >= 260 and <= 1600, $"duration clamped, got {far.DurationMs}");
        Check(Flight.To(vw, 10, 10, 2f, 10, 10, 2f) is null, "no-op move returns null");

        // a flight must always terminate
        Check(!far.Sample(far.DurationMs + 1, out _, out _, out _), "reports finished past duration");

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}
