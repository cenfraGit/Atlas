using System.Text.Json;

namespace Atlas;

/// <summary>undo by snapshot rather than by paired do/undo methods. a board is
/// a few kilobytes, so copying one is free, and a snapshot cannot drift out of
/// step with the action the way a hand-written inverse can. it also means any
/// future action - images included - is undoable without writing anything.</summary>
public sealed class History
{
    const int Depth = 60;

    readonly List<string> _past = [];
    readonly List<string> _future = [];

    static readonly JsonSerializerOptions Options = new();

    public bool CanUndo => _past.Count > 0;
    public bool CanRedo => _future.Count > 0;

    /// <summary>call immediately BEFORE mutating the board.</summary>
    public void Record(Board board)
    {
        _past.Add(JsonSerializer.Serialize(board, Options));
        if (_past.Count > Depth) _past.RemoveAt(0);
        _future.Clear();
    }

    public bool Undo(Board board) => Step(board, _past, _future);
    public bool Redo(Board board) => Step(board, _future, _past);

    static bool Step(Board board, List<string> from, List<string> to)
    {
        if (from.Count == 0) return false;
        to.Add(JsonSerializer.Serialize(board, Options));

        var snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        var restored = JsonSerializer.Deserialize<Board>(snapshot, Options);
        if (restored is null) return false;

        // the board object is referenced all over, so refill it in place
        board.Name = restored.Name;
        board.Items.Clear();
        board.Items.AddRange(restored.Items);
        return true;
    }

    public void Clear()
    {
        _past.Clear();
        _future.Clear();
    }
}
