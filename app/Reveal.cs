using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Atlas;

/// <summary>showing and hiding an overlay, with the movement that says where
/// it came from.
///
/// Every panel used to appear by having `IsVisible` flipped, which puts it on
/// screen between one frame and the next. That reads as a jump cut: nothing
/// tells you whether a thing arrived or was always there, and a side panel
/// that appears over the canvas looks like the canvas changed rather than
/// like something opened in front of it. A short slide from the edge it
/// belongs to answers both.
///
/// Panels anchored to an edge come in from that edge. The ones that sit in
/// the middle of the top have no edge to come from, so they drop a few pixels
/// and fade, which is enough to read as arriving without being a journey.
///
/// <b>Open is a Reveal question, not an `IsVisible` one.</b> A panel on its
/// way out is still visible for the length of the fade, and during that time
/// it must not answer Escape or swallow an arrow key - so callers ask
/// <see cref="Showing"/> rather than reading the property.</summary>
public static class Reveal
{
    /// <summary>where a panel comes from. Fade is for something with no edge
    /// of its own.</summary>
    public enum Edge { Fade, Left, Right, Top }

    /// <summary>quick enough not to be a wait. Opening is given a little
    /// longer than closing: arriving is the part worth seeing, and a slow
    /// dismissal is a panel arguing about it.</summary>
    public const int InMs = 140;
    public const int OutMs = 95;

    sealed class State
    {
        public Edge Edge;
        public bool Shown;
        /// <summary>bumped on every show or hide, so a hide that is overtaken
        /// by a show does not switch the panel off after the fact.</summary>
        public int Turn;
    }

    // weak, so a control that goes away takes its state with it rather than
    // being held alive by the table that remembers how to open it
    static readonly ConditionalWeakTable<Control, State> Known = [];

    /// <summary>set a control up to be revealed, and start it hidden. Call it
    /// once, from the control's constructor, in place of `IsVisible = false`.</summary>
    public static void Attach(Control c, Edge edge = Edge.Top)
    {
        Known.Remove(c);
        Known.Add(c, new State { Edge = edge });

        c.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(InMs),
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(InMs),
                Easing = new CubicEaseOut(),
            },
        ];

        c.Opacity = 0;
        c.RenderTransform = Offscreen(c, edge);
        c.IsVisible = false;
    }

    /// <summary>whether the panel is open. False the moment it starts closing,
    /// even though it is on screen for another frame or two.</summary>
    public static bool Showing(Control? c) =>
        c is not null && Known.TryGetValue(c, out var s) && s.Shown;

    public static void Set(Control c, bool shown)
    {
        if (shown) Show(c); else Hide(c);
    }

    public static void Show(Control c)
    {
        if (!Known.TryGetValue(c, out var state)) { c.IsVisible = true; return; }
        if (state.Shown) return;
        state.Shown = true;
        // bumping the turn is what cancels a hide that is still in flight
        int turn = ++state.Turn;

        // put it where it starts from without animating that part, or the
        // panel slides out to the edge before sliding back in
        var keep = c.Transitions;
        c.Transitions = null;
        c.Opacity = 0;
        c.RenderTransform = Offscreen(c, state.Edge);
        c.IsVisible = true;
        c.IsHitTestVisible = true;
        c.Transitions = keep;

        // a frame later, so the transition has a value to move away from.
        // Setting both in one go is a change the layout pass never sees
        Dispatcher.UIThread.Post(() =>
        {
            if (state.Turn != turn || !state.Shown) return;
            c.Opacity = 1;
            c.RenderTransform = TransformOperations.Identity;
        }, DispatcherPriority.Render);
    }

    public static void Hide(Control c)
    {
        if (!Known.TryGetValue(c, out var state)) { c.IsVisible = false; return; }
        // already closing, or never open: asking twice must not push the
        // moment it actually switches off any further into the future
        if (!state.Shown) return;
        state.Shown = false;
        int turn = ++state.Turn;

        // stops taking clicks straight away: it is closed as far as anyone
        // asking is concerned, and only still drawn
        c.IsHitTestVisible = false;
        c.Opacity = 0;
        c.RenderTransform = Offscreen(c, state.Edge);

        DispatcherTimer.RunOnce(() =>
        {
            if (state.Turn != turn || state.Shown) return;
            c.IsVisible = false;
        }, TimeSpan.FromMilliseconds(OutMs));
    }

    /// <summary>where the panel sits before it opens. An edge-anchored panel
    /// starts its own width out past that edge, so it is genuinely offscreen
    /// rather than half overlapping what it is about to cover.</summary>
    static TransformOperations Offscreen(Control c, Edge edge)
    {
        const float Drop = 10f;
        double w = double.IsNaN(c.Width) ? c.Bounds.Width : c.Width;
        if (w <= 0) w = 360;

        var b = TransformOperations.CreateBuilder(1);
        switch (edge)
        {
            case Edge.Left: b.AppendTranslate(-w, 0); break;
            case Edge.Right: b.AppendTranslate(w, 0); break;
            case Edge.Top: b.AppendTranslate(0, -Drop); break;
            default: b.AppendTranslate(0, 0); break;
        }
        return b.Build();
    }
}
