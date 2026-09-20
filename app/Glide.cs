namespace Atlas;

/// <summary>eases the camera toward where the wheel asked it to go.
///
/// A wheel notch used to move the camera the whole way on the frame it
/// arrived, so reading down a file was a series of jumps and there was
/// nothing to track with your eye between them. The wheel now moves a target
/// and the camera chases it.
///
/// Only the wheel glides. A drag is one to one and always has been: easing
/// something the hand is already holding reads as lag, not smoothness.
///
/// The step is frame-rate independent - an exponential approach, not a fixed
/// fraction per frame - so it settles in the same wall-clock time at 60 and
/// at 144 frames a second.</summary>
public sealed class Glide
{
    /// <summary>seconds to close about 63% of the remaining distance. Short
    /// enough to feel like a direct response, long enough to be followed.</summary>
    public const float Tau = 0.075f;

    /// <summary>close enough to stop, in world units and in relative zoom.
    /// Without a floor the approach never quite arrives and the canvas
    /// redraws forever.</summary>
    public const float SettleDistance = 0.4f;
    public const float SettleZoom = 0.001f;

    public float X, Y, S;
    public bool Running { get; private set; }

    /// <summary>aim somewhere. Called with the target rather than a delta so a
    /// second notch mid-glide adds to where we were already heading instead of
    /// to where the camera happens to have got to.</summary>
    public void To(float x, float y, float s)
    {
        X = x;
        Y = y;
        S = s;
        Running = true;
    }

    public void Stop() => Running = false;

    /// <summary>one frame. Returns false once there is nothing left to do.</summary>
    public bool Step(Scene scene, float dt)
    {
        if (!Running) return false;
        if (dt <= 0) return true;

        scene.CamX = Ease(scene.CamX, X, dt);
        scene.CamY = Ease(scene.CamY, Y, dt);
        // zoom eases geometrically: halving and doubling have to take the same
        // time, or zooming out feels slower than zooming in
        scene.CamS = EaseZoom(scene.CamS, S, dt);

        if (Settled(scene.CamX, X, scene.CamY, Y, scene.CamS, S))
        {
            scene.CamX = X;
            scene.CamY = Y;
            scene.CamS = S;
            Running = false;
        }
        return Running;
    }

    public static float Ease(float from, float to, float dt, float tau = Tau) =>
        to + (from - to) * MathF.Exp(-dt / tau);

    public static float EaseZoom(float from, float to, float dt, float tau = Tau)
    {
        if (from <= 0 || to <= 0) return to;
        return MathF.Exp(Ease(MathF.Log(from), MathF.Log(to), dt, tau));
    }

    /// <summary>true when the remaining move is too small to see.</summary>
    public static bool Settled(float x, float tx, float y, float ty, float s, float ts)
    {
        // distance is judged on screen, not in the world: a tenth of a world
        // unit is invisible zoomed out and a mile zoomed in
        float scale = Math.Max(s, 0.0001f);
        return Math.Abs(x - tx) * scale < SettleDistance &&
               Math.Abs(y - ty) * scale < SettleDistance &&
               Math.Abs(s - ts) / Math.Max(ts, 0.0001f) < SettleZoom;
    }
}
