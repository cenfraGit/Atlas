namespace Atlas;

/// <summary>smooth zoom-and-pan between two camera positions, using van Wijk &amp;
/// Nuij's interpolation (the one d3.interpolateZoom implements). a naive lerp
/// of x/y/zoom drags the viewport across the world at full magnification and
/// feels awful; this arcs out, travels, and arcs back in, at roughly constant
/// perceived velocity.</summary>
public sealed class Flight
{
    const double Rho = 1.4, Rho2 = Rho * Rho, Rho4 = Rho2 * Rho2;

    readonly double _ux0, _uy0, _w0, _dx, _dy, _d1, _s, _r0;
    readonly bool _straight;
    readonly float _vw;

    public double DurationMs { get; }

    Flight(float vw, float x0, float y0, float s0, float x1, float y1, float s1)
    {
        _vw = vw;
        _ux0 = x0; _uy0 = y0;
        // work in viewport width expressed in world units, not in zoom factor
        _w0 = vw / s0;
        double w1 = vw / s1;

        _dx = x1 - x0;
        _dy = y1 - y0;
        double d2 = _dx * _dx + _dy * _dy;
        _d1 = Math.Sqrt(d2);

        if (d2 < 1e-9)
        {
            _straight = true;
            _s = Math.Log(w1 / _w0) / Rho;
        }
        else
        {
            double b0 = (w1 * w1 - _w0 * _w0 + Rho4 * d2) / (2 * _w0 * Rho2 * _d1);
            double b1 = (w1 * w1 - _w0 * _w0 - Rho4 * d2) / (2 * w1 * Rho2 * _d1);
            _r0 = Math.Log(Math.Sqrt(b0 * b0 + 1) - b0);
            double r1 = Math.Log(Math.Sqrt(b1 * b1 + 1) - b1);
            _s = (r1 - _r0) / Rho;
        }

        DurationMs = Math.Clamp(Math.Abs(_s) * 420, 260, 1600);
    }

    /// <summary>null when the move is too small to be worth animating.</summary>
    public static Flight? To(float vw, float x0, float y0, float s0, float x1, float y1, float s1)
    {
        if (vw < 2 || s0 <= 0 || s1 <= 0) return null;
        bool samePlace = Math.Abs(x1 - x0) < 0.5f && Math.Abs(y1 - y0) < 0.5f;
        bool sameZoom = Math.Abs(s1 / s0 - 1) < 0.01f;
        if (samePlace && sameZoom) return null;
        return new Flight(vw, x0, y0, s0, x1, y1, s1);
    }

    /// <summary>returns false once the flight is over.</summary>
    public bool Sample(double elapsedMs, out float x, out float y, out float s)
    {
        double t = Math.Clamp(elapsedMs / DurationMs, 0, 1);
        bool running = t < 1;
        // soften the start and stop; van Wijk keeps the middle even
        t = t * t * (3 - 2 * t);

        double w;
        if (_straight)
        {
            x = (float)(_ux0 + t * _dx);
            y = (float)(_uy0 + t * _dy);
            w = _w0 * Math.Exp(Rho * t * _s);
        }
        else
        {
            double st = t * _s;
            double coshR0 = Math.Cosh(_r0);
            double u = _w0 / (Rho2 * _d1) * (coshR0 * Math.Tanh(Rho * st + _r0) - Math.Sinh(_r0));
            x = (float)(_ux0 + u * _dx);
            y = (float)(_uy0 + u * _dy);
            w = _w0 * coshR0 / Math.Cosh(Rho * st + _r0);
        }
        s = (float)(_vw / w);
        return running;
    }
}
