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
- [ ] Pen colour and weight from the toolbar. `PenColor` and
      `BoardItem.Weight` exist and are honoured; nothing sets them yet. Weight
      wants `[` and `]` on a board as well as a picker, since it is the thing
      you change most while drawing.
- [ ] Eraser modes: whole-stroke (what it does now) and a split mode that
      takes a bite out of a stroke and leaves the two halves as separate
      strokes. Brush strokes only - it must not touch notes or windows.
      `Strokes.Points` makes the split easy; the work is the mode switch and
      not fragmenting into hundreds of two-point strokes.
- [ ] Resizing a stroke. `Scene.Resizable` excludes strokes today because the
      grip would have to scale every point.
- [ ] Shape primitives beyond rectangle and arrow: ellipse, diamond,
      standalone text.
- [ ] Connectors that attach to elements and follow them when dragged. Bigger
      than free arrows; second pass.
- [ ] Replay strokes from an `SKPicture` once boards hold hundreds of them.
      Boards redraw every item every frame, which is fine at tens and not at
      thousands; the map already solved this.

### Canvas and interaction

- [x] Smooth scrolling (`Glide.cs`). The wheel moves a target and the camera
      eases toward it. A drag stays one to one - easing something the hand is
      already holding reads as lag.
- [x] Cursors outside edit mode: open and closed hands (`Cursors.cs`). CSS
      calls them grab and grabbing; Avalonia has neither, so they are drawn.
- [ ] Kinetic flick: a fast drag released should carry on and slow down.
      `Glide` is the mechanism; only the wheel uses it so far.

### Dialogs

- [x] `Layers` owns dismissal and the window sees Escape in the tunnel phase,
      so a dialog can no longer strand itself by losing focus. **Any new
      overlay must be registered in `BuildLayers` or it inherits the old
      bug.**
- [ ] The same treatment for the secondary-click menu, which Avalonia owns and
      which is not in the stack.

### Review mode

- [ ] The gathered change view (`C`) has never been driven by hand - the logic
      is tested, the wiring is not. Needs a look on a repo with real merge
      commits.

### What counts as a file

- [ ] Invert the scanner's rule. It has an allowlist of extensions, so an
      `.org` file, a `.el`, a `.nix` or anything else a bit unusual is simply
      absent from the map - and absent without saying so, which is worse than
      being wrong. The rule should be: show every text file, skip binaries by
      extension and by sniffing for NUL bytes, and skip the noisy directories
      (`.git`, `node_modules`, `bin`, `obj`) by default with a toggle to show
      them. `.atlas` should stay hidden even then: watching your own notes
      appear as cards is a hall of mirrors.
- [ ] A count of what was skipped, somewhere visible. The current silence is
      the actual bug; a map that says "1,200 files, 43 skipped" can be argued
      with.

### Samples and fixtures

- [ ] A third sample board that is deliberately messy - many items,
      overlapping, a long file window - for exercising edit mode.
- [ ] Sample annotations currently span a fixed eight lines from a
      declaration's opening line (`Samples.cs`), which is why they stop
      mid-class. Make them span the declaration Roslyn actually found.

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
