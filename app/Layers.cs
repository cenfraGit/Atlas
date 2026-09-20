namespace Atlas;

/// <summary>what Escape can close, innermost first.
///
/// Dialogs used to strand themselves. Each one handled Escape inside its own
/// text box, so the key only worked while that box held focus - and focus is
/// posted a frame late, can be taken by a panel opened on top, and is gone
/// entirely once anything else is clicked. Miss that window and the dialog
/// was stuck open with no key that would shut it.
///
/// So dismissal is not the dialog's job any more. Everything closable is
/// registered here in the order Escape should peel it off, the window watches
/// for Escape on the way *down* to whatever has focus, and one layer closes.
/// A new dialog is safe the moment it is registered; it cannot forget to
/// handle a key it no longer handles.</summary>
public sealed class Layers
{
    readonly List<(string Name, Func<bool> IsOpen, Action Close)> _layers = [];

    /// <summary>register a layer. Order is the order Escape peels them off, so
    /// add the innermost - a prompt over a panel - first.</summary>
    public void Add(string name, Func<bool> isOpen, Action close) =>
        _layers.Add((name, isOpen, close));

    /// <summary>close the innermost open layer. Returns what was closed, or
    /// null when there was nothing to close.</summary>
    public string? Dismiss()
    {
        foreach (var (name, isOpen, close) in _layers)
        {
            if (!isOpen()) continue;
            close();
            return name;
        }
        return null;
    }

    /// <summary>every open layer, innermost first. Nothing depends on this but
    /// it is what makes the stack inspectable when something does get stuck.</summary>
    public IEnumerable<string> Open =>
        _layers.Where(l => l.IsOpen()).Select(l => l.Name);

    public bool AnyOpen => _layers.Any(l => l.IsOpen());

    /// <summary>shut everything, for leaving a place entirely.</summary>
    public void DismissAll()
    {
        // a layer's close can open another - a panel restoring its parent -
        // so go round until it settles rather than once through
        for (int guard = 0; guard < 16 && Dismiss() is not null; guard++) { }
    }

    public int Count => _layers.Count;
}
