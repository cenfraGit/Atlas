using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Atlas.Tests.HeadlessApp))]

namespace Atlas.Tests;

/// <summary>a real Avalonia application, running off screen.
///
/// Everything in Program.cs that answers a click was untestable before this:
/// the tests could call the same functions the handlers call, but nothing
/// checked that a handler was wired to them, or that what it did to a control
/// was legal. That gap is how an inline editor that killed the process on
/// every double click got shipped.
///
/// Controls need a theme to have a template at all - a TextBox with no
/// template measures to nothing - so the Fluent theme is loaded here exactly
/// as the app loads it.</summary>
public class HeadlessApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    /// <summary>real Skia drawing rather than the stub. The stub renderer
    /// measures and arranges but never actually draws, and a control that
    /// lays out fine and dies when something tries to paint it is exactly
    /// the sort of thing these tests are for.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
