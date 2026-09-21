# CLAUDE.md

Guidance for working in this repo. `README.md` is the user-facing document and
explains *what* Atlas does; this file is about *how the code is put together*
and what to be careful of.

## Working here

**Answer the whole message first.** A message usually carries several ideas at
once - features, bugs, questions, things to explore. Before writing any code,
list every one of them as bullets, and be thorough about catching them all.
Then implement. The list is what stops a request being half-answered.

Say which items are being implemented now and which are being planned or
deferred, rather than quietly dropping one.

**Commit freely as you work.** No need to ask. Make real commits - one per
coherent change, with a message that says why - rather than snapshots. Adding
a test suite is one commit; deleting what it replaced is another. Always
co-author:

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

**Pull before you start.** Another session works on this repo and pushes to
the same branch, so the working tree is not where you left it. `git fetch`
and `git pull --ff-only` first, then **read the new commits** - the messages
carry the reasoning, and this file carries the rules they established. Doing
that is how you avoid re-fixing something already fixed, or building on a
function that has been replaced.

Run the suite after pulling, before writing anything. A red test on a
freshly pulled tree is the other session's, and it is yours to fix now:
`ImageStore.Prune` arrived with a test for a bug nobody had fixed yet.

**Push as well.** Push to `origin/main` once the work is committed and the
suite passes - the user cannot push from where they are reading this. Fetch
again before pushing, because the branch may have moved while you worked.

Pushing is publishing: it goes to a repository other people can see and it
cannot be taken back cleanly. So push finished work, not a checkpoint, and
never force push or rewrite anything already pushed without being asked.

**Never name another repository.** Atlas gets opened on private and
work-owned codebases, and a bug found while looking at one is still just a
bug. Nothing that goes into this repo - commit messages, comments, tests,
docs, sample data - may name or hint at where it was noticed: not the repo,
not its projects, files, branches or types. Describe the defect and the rule
it breaks, which is the part worth keeping anyway. Fixtures are synthetic
(`SampleRepo`), so a test never needs a real path to make its point.

## TODO

Everything outstanding, so nothing is lost when several things are in flight
at once. Tick an item off in the same commit that lands it, and delete ticked
items once a few have built up. Add to this list whenever a message raises
something that is not being done immediately.

**Nothing on disk needs preserving.** There are no real Atlas projects yet, so
`.atlas/` and `data/scan.json` can change shape freely - rename a field,
delete the files, regenerate them. Do not add compatibility shims for formats
nobody has.

### Boards as a diagramming surface

The big one. Boards are currently "a place to arrange code"; they should also
be "a place to design before there is code" - flowcharts, layouts, how a thing
will work. Boards start empty, grouped in the sidebar (design boards vs
documentation boards). No stored boards predate this, so the data model can
change freely.

- [x] Freehand vector strokes (`Strokes.cs`, `B`). `BoardItem.Points` holds a
      flat x,y array; `X/Y/W/H` are kept in step as the ink's bounds so every
      other part of the board goes on treating a stroke like a box.
- [x] Stroke hit-testing: point-to-polyline distance, bounds as the cheap
      rejection first.
- [x] Eraser (`X`), stroke-erase rather than pixel-erase.
- [x] Pen colour and weight (`PenBar.cs`), shown only while drawing. `[` and
      `]` step a ladder of widths; swatches set the colour. With a selection
      they change that too, which is what a selection makes them mean anyway.
- [x] Eraser modes (`shift+X`): a whole stroke, or a bite out of one leaving
      the surviving pieces as strokes in their own right.
- [x] Resizing a stroke, by scaling every sample into the new box
      (`Strokes.ScaleInto`). Maps from the current bounds so repeated drags
      compose; the pen scales with the smaller axis, clamped at both ends.
- [x] Shape primitives: ellipse, diamond and standalone labels. The three
      outlined shapes are one box with a different path traced round it; a
      label is words with no panel behind them, which is what makes it a
      heading rather than a note.
- [x] Connectors. `BoardItem.From`/`To` tie an arrow's ends to items; a tied
      end has no stored position, so dragging a box needs no update anywhere.
      Ends land on one of **four anchors** - top, right, bottom, left - and
      nowhere else. An endpoint free to sit anywhere on an edge slides about
      as either box moves, which is what made the first attempt unreadable.
- [x] Shapes are exactly as tall as they were made - the floor of 40 and the
      default of 240 are gone, so a long thin divider is possible.
- [x] Resize from any of the four corners. `GripAt` returns which one and
      `Scene.Resize` moves the two edges that corner owns, which for a top or
      left grip means the origin moves too.
- [x] Fill and border. `BoardItem.Fill` is null for a wash of the border
      colour, `"none"` for genuinely empty, or a colour of its own - one
      field rather than a colour plus a flag that could disagree with it. An
      empty shape keeps its hitbox, so it works as a frame round other
      things; picking has always gone by the box and now there is a test
      saying it must.
- [x] Typing into an item where it sits (`InlineEditor.cs`), on double
      click or from the menu. A note used to be edited through the prompt at
      the top of the window: you double-click something in the middle of the
      canvas, look elsewhere to type, and look back to see what happened -
      and on a diagram the thing you are naming is usually one of several
      similar boxes, so the dialog takes away the context that tells them
      apart. Enter commits, shift+Enter starts a line, Escape puts back what
      was there, clicking away commits. `Scene.EditingItem` keeps the canvas
      from drawing the same words underneath the box. A new note or label
      opens straight into it, and a label left empty is dropped rather than
      left on the board as an invisible thing to trip over.
- [x] Words inside a shape, centred and wrapped, which is what most of a
      flowchart is. Writing one used to mean laying a separate label over a
      rectangle and moving the two together for ever afterwards.
- [x] A note's border goes all the way round. A three unit bar down the
      left edge reads as a quote in a document rather than as a card on a
      canvas, and the other three sides are where a note meets what it
      overlaps.
- [x] Type size on anything with words in it, from a "Text size" menu. 0
      means the default, which is the one value that cannot be a real size,
      and `Scene.LineStep` moves the lines apart as the type grows.
- [x] That default is `Scene.CodeSize` for every kind: the size source
      comes out at inside a board's file window. The three kinds used to
      have three defaults, a label at 34 on the grounds that a heading is
      large, and on a board that is mostly file windows the result was text
      towering over the code it was written about. A heading is still a
      heading - it is one because you set its size, not because of what
      kind it is.
- [x] Line width. A shape's border and an arrow's shaft were a hairline -
      one screen pixel at any zoom, the one width that says nothing about
      what it outlines. They now share the stroke's `Weight`, so `[`, `]`
      and the "Line width" menu mean one thing rather than three, and an
      arrow's head grows with its shaft.
- [x] Drag to place a shape, the way an arrow does. Arming shows a ghost and
      does nothing until you drag out the box; the button stays lit until the
      shape is drawn, and `Esc` cancels. Rectangles, ellipses, diamonds and
      labels; a note (`N`) still lands in the middle of the view.
- [x] Ink is recorded into an `SKPicture` and replayed. Measured first: a
      thousand strokes cost 37ms a frame rebuilt every time, past the 60fps
      budget on its own. The cache is keyed on a per-stroke signature
      computed each frame - bounds, sample count, weight, colour - which is
      O(strokes) not O(samples) and cannot be forgotten at a call site the
      way an explicit invalidation can. `Scene.StrokeRebuilds` counts
      re-recordings, which is what the tests assert on rather than a clock.

### Boards, reviewed end to end

A read of the whole board subsystem, and what it turned up. Each of these has
a test that was checked to fail against the old code first.

- [x] The UI thread no longer measures a board's words. `LastHeight` reads
      what the draw loop left behind; nine call sites went through
      `ItemHeight`, which wraps a note's text with SkiaSharp.
- [x] A tied arrow is asked where it is drawn, not where it was tied. The
      eraser hunted with the stored pair and so rubbed out the place a line
      used to be; `ArrowAt` had it right and they share the test now.
- [x] `Scene.Remove` unties an arrow from an item on its way out.
- [x] `BoundsOf`, so an arrow is as big as the line it draws.
- [x] Undo records when something changes, not when it is clicked.
- [x] A finished stroke is saved on the spot, and gives the pointer capture
      back like the branches beside it.
- [x] Escape leaves a board again - see Dialogs.
- [x] Every stored file reference carries a fingerprint: windows, bookmarks
      and the fly-to for an annotation, which had one and ignored it.
- [x] `.atlas/` is written with LF - see the storage contract.

### Presentation boards

Not started, and not next. A board that is a sequence rather than a
surface: slides you step through with the arrow keys, for walking someone
through how a change was made or how a part of the system works. Each
slide is a camera position and a set of items, so much of it already
exists - `Tour` steps between bookmarks and `Flight` does the movement
between them. The open questions are whether a slide is a board, a group
of boards, or a saved view of one board, and what a slide does that a
tour stop does not.

- [ ] Presentation boards: slides, stepped with the arrows.

### Canvas and interaction

- [x] Smooth scrolling (`Glide.cs`). The wheel moves a target and the camera
      eases toward it. A drag stays one to one - easing something the hand is
      already holding reads as lag.
- [x] Cursors outside edit mode: open and closed hands (`Cursors.cs`). CSS
      calls them grab and grabbing; Avalonia has neither, so they are drawn.
- [x] Kinetic flick. A pan tracks a smoothed velocity and a release with
      speed on it throws the canvas, on a longer time constant than a wheel
      notch. A release more than 90ms after the last movement does not throw:
      resting the hand means you meant to stop.

### Dialogs

- [x] Panels arrive rather than appearing (`Reveal.cs`). Flipping
      `IsVisible` puts a panel on screen between one frame and the next,
      which reads as a jump cut. A side panel slides in from the edge it is
      anchored to; the ones in the middle of the top drop a few pixels and
      fade. **Open is a `Reveal.Showing` question now, not an `IsVisible`
      one** - a panel on its way out is still visible and must not answer
      Escape. Any new overlay calls `Reveal.Attach` in its constructor.
- [x] `Layers` owns dismissal and the window sees Escape in the tunnel phase,
      so a dialog can no longer strand itself by losing focus. **Any new
      overlay must be registered in `BuildLayers` or it inherits the old
      bug.**
- [x] The board itself is a layer. It was left out when dismissal moved
      here, and HandleKey refuses Escape outright, so Esc on a board did
      nothing at all while the bar still offered "back to map  esc".
      **A place you can be inside needs a layer, not just a dialog.**
- [x] The secondary-click menu is in the stack, innermost of all. Its flag is
      cleared from the menu's own `Closed` event, since clicking away from it
      closes it without anyone here being told.

### Annotations

Notes and annotations are different things. A **note** is a board item: it
sits on one board and belongs to it. An **annotation** is attached to code -
to a symbol plus a context fingerprint, never to a line number - so it stays
on the right line when something is inserted above it, survives a rename via
the fingerprint, and reports drift or orphanhood when it cannot. That much is
covered by `AnchorTests` and works.

- [x] Scope. `Annotation.Board` is the board it belongs to, or null for
      everywhere - the id *is* the scope, rather than a flag beside one that
      could disagree with it. `Scene.AnchorsFor` filters on the way out and
      `AllAnchorsFor` does not, so changing a scope needs no re-anchoring and
      the list can still show a note you cannot currently see.
- [x] Set it when the note is made (a second menu entry, only on a board) and
      change it afterwards (the menu, or the `L` panel).
- [x] The `L` panel multi-selects, so a run of notes becomes a board's own in
      one gesture. `K` keeps, `G` globalises.

### Review mode

- [x] `G` lists every branch. The base and anything fully merged into it
      have no change set, and leaving them out made the panel look broken -
      you open the list of branches and the one you are on is not in it. A
      branch with nothing ahead of the base is shown as its own recent
      history instead, and the detail line says which case a row is.
- [x] The map lights the diff rather than the file it is in. A changed
      file was painted solid edge to edge, and since size on the map is
      size of *file*, a two line fix in a long file looked like the
      biggest commit in the repo. Changed lines are merged into runs,
      floored to a few pixels so they survive map zoom, and the card
      behind them is a dim silhouette saying "touched". Removals glow too.
- [x] The gathered change view (`C`) shows whole files, not hunks. Windows
      there are real syntax-coloured code, and cutting them to three lines
      of context threw away the one thing the view has that a diff does
      not. The changed lines glow inside the window, which they did not
      before - the view showed the right code and no sign of what about it
      had changed - and it opens looking at the first change rather than at
      line one of a three thousand line file.
- [ ] The gathered change view still has not been driven by hand on a repo
      with real merge commits.
- [x] The gathered view rebuilds whenever the change set does. Picking a
      commit in the panel used to leave the previous commit's windows up,
      so only the files both commits happened to touch appeared to change.
      Every route to a different change set goes through `ShowChanges`, so
      that is the only place that can rebuild it - and a commit with
      nothing on the map now shows an empty board rather than a stale one.
- [x] The read-only board is not deaf. Its key block returned on anything
      it did not handle, so `S` could not switch the wheel between zoom and
      scroll once you were inside it.
- [x] Up and down step the commits, which is what a list of commits down
      the side looks like it does. Not while a tour is running: those are
      its arrows.
- [ ] A file the commit **deleted** has no card on the map, because the scan
      is of what is there now, so it cannot be shown at all. It should
      appear somewhere - probably at the folder that lost it.
- [ ] The mode islands sit behind a side panel, so in review mode - where
      the commits panel is always open - the wheel-mode indicator cannot be
      seen at all. `S` toasts now, which covers it, but the islands should
      not be under a panel in the first place.
- [ ] Opening a panel or the commit list takes a visible moment with no sign
      that anything is happening, so it reads as broken until it appears.
      Needs a spinner, or the list up front and its contents filled in.

### Finding things

- [x] `ctrl+F` searches what the files *say* (`Grep.cs`, `GrepOverlay.cs`).
      `/` still searches what they are *called*, which is a different
      question - `ctrl+F` used to do that one. On a board the search covers
      the files that board has windows onto, because a board is a chosen
      subset and searching the whole repo from one answers nothing. Walking
      the results flies to each match and highlights the line; the camera is
      pushed down so the match lands below the results panel rather than
      under it.
- [x] The lines come from a provider rather than from disk, so the same code
      searches the working copy, a commit's tree and a couple of arrays in a
      test. It runs off the UI thread and takes a token, so a superseded
      search stops.
- [ ] Search as you type. It runs on Enter because a keystroke would
      re-read every file that is not already loaded; it wants a cache of its
      own, which `Scene.ReadLines` deliberately is not.
- [ ] Regular expressions, and whole-word. Plain case-insensitive substring
      for now.

### What counts as a file

- [x] Inverted the scanner's rule: every text file is in, binaries are out by
      extension and by NUL sniff, and the noisy directories are behind the `.`
      toggle. `.git` and `.atlas` stay hidden at any setting.
- [x] A count of what was skipped, reported rather than swallowed.
- [x] Respect `.gitignore`, asked of libgit2 (`GitIgnore.cs`) rather than
      reimplemented, so nested ignore files, negations and
      tracked-beats-pattern come along with it. The hardcoded `Noise` list
      stays as a fallback for repos that never got round to ignoring their
      build output, and for folders that are not repos at all.

### Samples and fixtures

- [x] A third sample board, "Everything at once": every kind of item,
      overlapping, an empty frame over the lot, connectors with one end
      loose. What breaks in edit mode breaks on a board like that.
- [x] Sample bookmarks, and a tour through them. A tour is the reason
      bookmarks are worth having and there was no way to see one without
      recording it by hand first. Both kinds are represented: six anchored
      to declarations, and one free camera position.
- [x] `--samples` is deterministic. Every id is derived from a name rather
      than generated, and a board's file is named after the id it ends up
      with rather than the random one `Create` handed out - which is why
      regenerating used to rename all three boards and how sample boards
      got deleted from the repo once already.
- [x] Sample annotations span the declaration Roslyn found, and point at
      methods rather than classes - a note is tinted across what it covers,
      and a nine hundred line class is not a thing a sentence is about.

## What this is

A zoomable canvas for reading a codebase. Every file is a card laid out by
directory; zoom out for the shape of the repo, zoom in for real
syntax-highlighted source. It is a desktop app, not a library.

## Layout

```
app/                 the whole application, one flat folder, no sub-projects
tests/Atlas.Tests/   xunit suite (hermetic - builds its own fixtures)
data/scan.json       layout cache, machine specific, gitignored
.atlas/              boards, annotations, bookmarks for the repo being read
```

`app/` is deliberately flat. Files are named after the thing they do
(`Boards.cs` is storage, `BoardOverlay.cs` is the panel, `BoardBar.cs` is the
toolbar). Keep that convention rather than introducing folders.

Two files hold most of the app: `Program.cs` (~2,000 lines, input handling and
window wiring) and `Scene.cs` (~1,400 lines, the canvas and everything drawn
on it). Everything else is small and single-purpose.

## Build and run

```bash
cd app && dotnet run -- ../path/to/some/repo    # open Atlas on a repo
dotnet test tests/Atlas.Tests                   # the unit suite
```

`run.cmd` opens Atlas on itself; `test.cmd` runs the unit suite. With no
argument Atlas reopens whatever `data/scan.json` last pointed at.

C# / .NET 10, Avalonia for the window and input, SkiaSharp for the canvas,
TextMate grammars for highlighting, Roslyn for symbols, LibGit2Sharp for git.
No other toolchain - clone and run.

## Things that will bite you

**TextMate is not thread safe.** Tokenising two files at once corrupts the
shared rule registry into unbounded recursion and takes the process down with
a stack overflow that *cannot be caught*. `Highlighter` serialises everything
behind one lock. Do not "optimise" that lock away; give each worker its own
`Registry` if tokenising ever shows up as a bottleneck.
Covered by `HighlighterTests.TokenisingManyFilesAtOnceDoesNotCorruptTheRegistry`.

**`Scene.Draw` runs on the render thread.** It is called from an Avalonia
`ICustomDrawOperation`, not from `SceneView.Render`. Anything in `Program.cs`
runs on the UI thread, so **the UI thread must never call a `Scene` method
that measures text** - `ItemHeight` on a note, `Wrap`, `LabelHeight`. Two
threads in SkiaSharp's text path does not throw: the process disappears, with
no dialog and no exception, exactly the way TextMate takes it down. Sizing the
inline editor did this and killed the app on every double click. The rule is
that the draw loop leaves numbers behind (`Scene.EditingHeight`) and the UI
thread reads them: `Scene.LastHeight` is that reader, and every call
off the draw loop - picking, the band, the eraser, resizing, fitting,
clamping - goes through it. `ItemHeight` is for the draw loop.
`Scene.TextMeasures` counts measurements so a test can assert nobody
measured. Covered by `SceneThreadingTests`.

**Nothing may touch `Dispatcher.UIThread` before `AppBuilder` runs.**
Reading it creates Avalonia's dispatcher singleton, and one made before
`UsePlatformDetect` binds to no windowing platform - the app then starts,
finds it has no main loop, throws `PlatformNotSupportedException` and exits
before a window appears. `Crash.InstallEarly` is the pre-Avalonia half for
this reason; the dispatcher handler goes on in
`OnFrameworkInitializationCompleted`.

**An unhandled exception used to be invisible.** Atlas is a `WinExe` with no
console. `Crash.cs` logs to `%LOCALAPPDATA%/Atlas/crash.log`, reports into the
window, and marks UI-thread exceptions handled so a bug in one event handler
does not throw away the board someone is working on.

**A checkout may be CRLF.** This machine has `core.autocrlf=true`, another
may not, so a raw string literal in a test is whatever git wrote. A test that
cuts lines out of one with `
` matches nothing on a CRLF checkout and fails
there and nowhere else. Normalise the literal once (`AnchorTests.Source`)
rather than assuming an ending.

**Redraws are driven by input, not a loop.** Anything that finishes off-frame
- a file load, a partially built view - must call `Scene.RequestRedraw`.
Setting a flag does not work: nothing watches one between frames.

**Per-frame state is written by the draw loop.** `Scene.Tier` is one of these.
Headless code (and tests) must set it via `Scene.TierFor(camS)` rather than
expecting `Rebuild()` to have done it.

**Layout lives in `Scanner`, not in the view**, so a scan is reproducible and
diffable. Do not compute card positions in drawing code.

**Bar geometry is recorded once into an `SKPicture` and replayed.** Pan and
zoom are a pure canvas transform with no geometry rebuild. Construction is
budgeted to 14 cards per frame so flinging into unseen territory never blocks.
If you add per-frame geometry work, you have broken this.

**`Scene.ReadLines` does not cache, on purpose.** `_text` is filled in
alongside `_runs` by the loader, and an entry in one without the other means
`DrawCode` finds text with no syntax runs beside it and paints that whole
file in a single colour. The search reads every file in the repo, so caching
there would leave the map grey.

**A path is not an identity.** Board windows, annotations and bookmarks
reference files by path *and* a content fingerprint (`FileKeys`).
`Scene.ResolveFile` tries the path, then a uniquely named file, then the
fingerprint. Anything that stores a file reference must store the key too, and
must resolve with `ResolveFile` rather than `IndexOfPath` - the rule was
written here first and then three of the four places quietly broke it, which
only showed up as a board full of `missing:` after somebody renamed a file.
`Scene.KeyFor` makes a key; `Scene.EnsureKeys` fills in what older boards are
missing when they open. Covered by `FileReferenceTests`.

**An arrow is not a box.** Its `W` is whatever the default was when it was
made and its `H` is nothing, so its stored rectangle is six hundred units of
empty canvas beside the tail. Ask `Scene.BoundsOf` how big anything is - it
resolves an arrow through its two ends, and a tied end through the box it is
tied to. Fitting the view and the rubberband both read the phantom before it
existed.

**Deleting goes through `Scene.Remove`.** An arrow tied to an item that is
gone falls back to coordinates from whenever the tie was made, so removing a
box used to fling its connectors across the board. `Remove` cuts such a tie
and writes the end's current position back first. Four call sites became one;
keep it that way.

**Undo records at the first mutation, not on press.** `Mutating()` takes one
snapshot per gesture, the first time a handler is about to change something.
Recording on press meant selecting three things left three undo steps that
undid nothing.

## Conventions

- Comments explain *why*, not what. Existing ones are lower case, in prose, and
  often name the failure that motivated the code. Match that.
- XML doc comments on types and non-obvious public members; the first line
  says what the thing is for.
- File-scoped namespaces, implicit usings, nullable enabled, collection
  expressions (`[]`), target-typed `new`.
- Repo-relative paths always use forward slashes, everywhere, including on
  Windows. The scanner normalises; keep it that way so scan paths, git paths
  and stored references compare directly.
- Anything written into `.atlas/` is repo-relative so it travels through git.
  Anything machine-specific goes in `data/`, which is gitignored.

## Storage contract

`.atlas/` in the repo being read is **meant to be committed** - that is how a
team shares boards and notes. Never add it to a `.gitignore`. It holds
`boards/*.json`, `annotations.json`, `bookmarks.json` and `images/`.

There is no save step: stores write on every change and the canvas shows
`saved: ...`. If you add state a user authors, it saves itself the same way -
and immediately, in the handler that made it, rather than leaving a dirty flag
for some later gesture to notice.

**What the app writes is what git stores.** `.gitattributes` pins the
repository to LF (`*.cmd` and `*.bat` excepted - cmd.exe is the one thing
still entitled to CRLF), and the JSON stores set `NewLine = "\n"` because
`System.Text.Json` otherwise writes this machine's newline. Without that,
opening Atlas and touching nothing leaves every board it saved looking
modified, which teaches you to ignore what git says about the one folder you
are meant to commit.

## Tests

`tests/Atlas.Tests` is an xunit project. It is **hermetic**: every test builds
its own fixture - a synthetic repo (`SampleRepo`), a scratch directory
(`TempDir`), or a real git repository with a merged pull request and an
unmerged branch (`GitFixture`). Nothing is asserted against this repo's own
contents, so adding a file cannot change a result.

When adding tests:

- Build a fixture, do not point at `..`. A test that depends on the repo it
  runs in silently stops testing anything.
- Name the test after the rule it defends, in prose
  (`AFileThatWasRenamedIsFoundByItsContent`), matching the existing style.
- `ImageStore` and `Highlighter` hold process-wide state; their tests sit in
  the `images` and `highlighter` collections so they do not run alongside
  anything that would disturb them.

**The GUI runs from here.** `Atlas.exe <repo>` started with `Start-Process`
from PowerShell opens a real window, and it can be driven with `SendKeys`
and `mouse_event` and screenshotted with `CopyFromScreen` - see the driver
scripts pattern in `uitest.ps1`. Do not conclude the app cannot be launched
from a failure to launch it; check the crash log first, because a startup
bug looks exactly like a missing desktop.

**Driving the app writes to `.atlas/`.** The sample boards are committed, so
a note added while testing lands in a tracked file. Check `git status` after
driving and `--samples` to regenerate.

`Avalonia.Headless` gives the suite a real window, off screen, with real Skia
drawing (`Support/HeadlessApp.cs`, `[AvaloniaFact]`). Everything in
`Program.cs` that answers a click was untestable before it. Use it for
anything that touches a control.

`uitest.ps1` remains for what a headless test cannot see: a real window, real
keystrokes, and the real Windows clipboard. Run it with
`powershell -STA -File uitest.ps1`.

`app/*Test.cs` are the older in-process self-checks (`dotnet run -- --gittest`
and friends). They print PASS/FAIL but **do not set an exit code**, so a
failure is invisible to any script that runs them. The xunit suite now covers
the same ground; prefer adding to it.
