using Avalonia.Threading;

namespace Atlas;

/// <summary>what happens when something throws where nobody is catching.
///
/// Atlas is a WinExe. There is no console attached, so an unhandled exception
/// prints its message to a stderr nobody is reading and the process exits:
/// from the outside the window freezes for a moment and then is gone, with no
/// dialog, no log and nothing to go on. That is exactly how it looked when
/// double-clicking a note killed the app, and the absence of a message cost
/// more than the bug did.
///
/// So: every unhandled exception is written to a file, and one that happened
/// on the UI thread is marked handled and reported in the window instead of
/// taking the process down. A bug in one event handler is not a reason to
/// throw away the board someone is working on - and unlike an exit, a
/// message can be read.</summary>
public static class Crash
{
    /// <summary>beside the user's other application data, not in the repo
    /// being read: a crash is about this machine and this install, and the
    /// repo is somewhere that gets committed.</summary>
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Atlas", "crash.log");

    static Action<string>? _report;

    /// <summary>the handlers that are safe before Avalonia exists.
    ///
    /// <b>Nothing here may touch <c>Dispatcher.UIThread</c>.</b> Reading it
    /// creates Avalonia's dispatcher singleton, and one created before
    /// `UsePlatformDetect` runs binds to no windowing platform - so the app
    /// starts, finds it has no main loop, throws PlatformNotSupportedException
    /// and exits before a window is ever shown. A crash handler that stops
    /// the program starting is worse than no crash handler, and this one did
    /// exactly that.</summary>
    public static void InstallEarly()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.ExceptionObject as Exception, "unhandled");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, "background task");
            e.SetObserved();
        };
    }

    /// <summary>the rest, once the framework is up and there is a window to
    /// report into. <paramref name="report"/> shows a line in it.</summary>
    public static void InstallOnUiThread(Action<string> report)
    {
        _report = report;

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write(e.Exception, "while handling input");
            // the window survives. Losing an hour of arranging a board
            // because one click hit a bug is a worse outcome than a wrong
            // frame, and the message says what happened either way
            e.Handled = true;
        };
    }

    public static void Note(Exception ex, string doing) => Write(ex, doing);

    static void Write(Exception? ex, string doing)
    {
        if (ex is null) return;

        var line = $"{DateTimeOffset.Now:u}  {doing}\n{ex}\n\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line);
        }
        catch { /* a crash logger that throws is worse than one that does not */ }

        Console.Error.Write(line);
        try { _report?.Invoke($"{ex.GetType().Name}: {ex.Message}"); }
        catch { }
    }
}
