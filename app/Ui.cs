using Avalonia.Media;

namespace Atlas;

/// <summary>shared colours and the one font, for the panels that float over
/// the canvas.</summary>
public static class Ui
{
    /// <summary>Consolas is a windows font. the rest of the list is what the
    /// other platforms call their fixed width face, so code still lines up
    /// somewhere without Consolas installed.</summary>
    public static readonly FontFamily Mono =
        new("Consolas, Cascadia Mono, DejaVu Sans Mono, Menlo, Liberation Mono, monospace");

    public static readonly IBrush PanelBg = new SolidColorBrush(Color.FromArgb(0xF2, 0x07, 0x12, 0x1c));
    public static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x1b, 0x4c, 0x66));
    public static readonly IBrush Fore = new SolidColorBrush(Color.FromRgb(0x9f, 0xd4, 0xea));
    public static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0x35, 0x70, 0x8f));
    public static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xff, 0xd1, 0x66));
}
