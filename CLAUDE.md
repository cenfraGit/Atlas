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

Pushing is the user's. Do not push.

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
- [ ] The gathered change view (`C`) has never been driven by hand - the logic
      is tested, the wiring is not. Needs a look on a repo with real merge
      commits.

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

**A path is not an identity.** Board windows and annotations reference files by
path *and* a content fingerprint (`FileKeys`). `Scene.ResolveFile` tries the
path, then a uniquely named file, then the fingerprint. Anything that stores a
file reference must store the key too.

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
`saved: ...`. If you add state a user authors, it saves itself the same way.

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

`uitest.ps1` remains for what a headless test cannot see: a real window, real
keystrokes, and the real Windows clipboard. Run it with
`powershell -STA -File uitest.ps1`.

`app/*Test.cs` are the older in-process self-checks (`dotnet run -- --gittest`
and friends). They print PASS/FAIL but **do not set an exit code**, so a
failure is invisible to any script that runs them. The xunit suite now covers
the same ground; prefer adding to it.
