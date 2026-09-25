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

**The README moves with the feature.** Anything a user can see or type -
a key, a command, a behaviour - goes into `README.md` in the same commit
that lands it, and anything it says that stops being true comes out. It
fell behind for a whole run of features once, still describing a change view
and a "not built yet" list from weeks before; the TODO list here is not a
substitute, because nobody reading the README sees it.

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

**Real boards exist now.** Atlas is in use, so `.atlas/` has boards and
annotations nobody wants to redraw. Changing a format is still allowed when
it buys robustness - but an old board must still open where it was, and the
way to know is to load one and compare against the previous commit, not to
reason about it. That is how the end-anchor change found it was collapsing
old windows. A fallback of one `??` for a field older boards lack is fine; a
parallel code path for an old format is not.

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
- [x] Drag a wall to change one dimension (`Scene.EdgeAt`,
      `ResizeEdge`). A corner moves two edges, which is a nuisance when
      only one of them matters. A label has sides only - its height is its
      words.
- [x] A file window's walls **clip** it rather than resize it
      (`Scene.ClipTo`): the top and bottom drag the line range, so a window
      onto a three thousand line file can be narrowed to the one method you
      care about. Nothing is scaled - the lines that stay are the size they
      were - so it is cheaper to draw as well as shorter, and the code
      under the wall stays put while the wall moves through it, which is
      why dragging the top moves the item's Y with it. No side handles: a
      window's width scales the whole card, so one would be a zoom.
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
- [x] Every stored file reference carries a fingerprint: windows, bookmarks
      and the fly-to for an annotation, which had one and ignored it.
- [x] `.atlas/` is written with LF - see the storage contract.

### Board tours

Replaced "presentation boards", and answered its open question: a slide is
a saved view of one board. A tour used to be a list of *map* bookmarks that
knew nothing about boards, and nothing let you see, order or prune its
stops.

- [x] Stops on a board (`M`), stored in the board's own json as
      `Board.Stops`. A stop is the *region* on screen - centre, width and
      height in board units - not a zoom, so it frames the same things in
      any window. With the panel open the region is the uncovered part, and
      it plays back into the uncovered part too.
- [x] `P` plays them with `Flight`; space and the arrows step, clamping at
      both ends, and Escape stops. Leaving or switching boards ends it.
- [x] `TourPanel`, `shift+M`: click to look, double click to play from
      there, drag to reorder - the rows move as you drag, and Escape mid-drag
      puts the order back. Delete is the panel's button only: the panel stays
      open while you work, and the key belongs to what is picked.
- [x] Map bookmarks and tours are gone, with `bookmarks.json`. What survived
      is `Places` - framing a range of lines - which search, annotations
      and "add this view" use. Ids come from `BoardStore.NewId`.
- [x] A faint dashed frame per stop, numbered, while the tour panel is open
      (`Scene.StopsShown`); the selected stop is brighter. Hidden while a
      tour plays. `atlas board render --stops` draws them too.

### Boards panel

- [x] A drag rearranges the list as it goes, so the rows are the preview -
      the same way the tour panel does it. Onto a heading means the top of
      that group.
- [x] Escape mid-drag puts every board's group and order back, and the group
      order; only the next Escape closes the panel.
- [x] Groups are dragged by their heading. The order lives in
      `.atlas/groups.json` - beside `boards/`, since anything in it is read
      as a board. A group not listed goes after the listed ones, ungrouped
      first then by name, which is what every repo looked like before.
- [x] Headings are set apart: capitals, accent colour, a count, a rule above.
      They are never left selected, and up and down step over them.
- [x] It is the **workspace** now: Home (the map) pinned first - selectable,
      never dragged or dropped on - then the boards. `Tab` toggles it from
      anywhere, taken by the window in the tunnel phase (`App.WireKeys`) so a
      focused list cannot read it as "next control"; not inside a TextBox.
      Long names wrap, and a grip on the right edge drags the width.
      `Space` is the spotlight, and hold-space-to-pan is gone.
- [x] Both panels drew only their first rows: a virtualizing list handed its
      rows while hidden never realised the rest. A panel of a few dozen rows
      uses a plain `StackPanel` for its items. **Any new list in a panel that
      is shown with `Reveal` wants the same.**

### Copy, paste and search on boards

- [x] ctrl+C and ctrl+V, for every kind of item. The copy listed fields by
      hand and dropped everything added since; it clones through json now.
      Arrows copied with their ends stay tied to the copies; alone, they
      become loose lines in the shape they were drawn.
- [x] ctrl+F reaches every window onto a file, not only the first one made.
      A match is listed once per window showing its line.
- [x] Wall handles are drawn small; a file window keeps its long clip bars.

### Anchoring, under a run of edits

`EditScenarioTests` makes eleven edits in a row and re-anchors after each,
because a rule that survives one edit can still drift on the second.

- [x] A cropped window's end is anchored, to the end of the declaration it
      sits in, so a method that grows is still shown to its closing brace.
- [x] A shape's bottom follows the declaration it is in, not the one its
      top is in, so a box round two methods grows with the second.
- [x] The fingerprint is a hash per line, so adding a line right next to a
      marked one no longer breaks it.
- [ ] Old single-hash fingerprints in `annotations.json` are never
      rewritten - nothing re-anchors an annotation and saves it. They still
      match whole, as before, but miss the per-line tolerance until the
      annotation is made again.
- [ ] Snapping drawings to a character grid over file windows was proposed
      as a way to make pinning reliable. Not done: the failures the scenario
      test found were about which *line* is which after an edit, and a grid
      only changes where inside a line something sits - that part (`Dy`) has
      never been the problem. Revisit only if a drawing is found landing a
      fraction of a line off.

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
- [x] Side panels drag wider from their inner edge (`PanelGrip`), and
      everything else moves clear of them (`App.MakeRoom`): each control's
      margin is its own plus the width open on each side, recomputed on
      `Reveal.Changed` and on a width change. **A new overlay goes in the
      `MakeRoom` list, and a new side panel gets a grip and goes in its
      side's list.**
- [x] `Layers` owns dismissal and the window sees Escape in the tunnel phase,
      so a dialog can no longer strand itself by losing focus. **Any new
      overlay must be registered in `BuildLayers` or it inherits the old
      bug.**
- [x] Escape closes things; it does not leave a board. Leaving is going
      somewhere, not closing something, and when one key did both, Escape
      on a dialog threw you off the board with it. `alt+Left` leaves, and
      the bar says so - it used to offer "back to map  esc" while Escape
      did nothing, which is what made the key look broken.
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

- [x] `P` lists **open** pull requests as well as merged ones. Merged ones
      are found by their merge commit; an open one has none, so nothing in
      the local history says it exists. They come from `gh pr list`, off the
      UI thread, and join the top of the list when it answers - no `gh`, or
      not signed in, and the merged list stands with a toast saying why. An
      open one whose commits are not here is fetched with the user's own git
      (`pull/N/head`, no ref made) before it opens; libgit2 would need the
      credentials handed to it. Reviewed from its merge base, like a branch.
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
- [x] The gathered change view is lit the way the map is: zoomed out past
      readable text a window is veiled and its changed runs glow, drawn by
      the same `DrawGlowBands`. Close in it keeps the tint, as the map does.
- [x] What a change *removed* is shown as text, where it was - on the map
      and in the change view alike. Review mode already draws a temporary
      scan of the commit's tree, so each changed file's text in it gets the
      whole change's removed lines put back (`Splice`), and the cards
      really contain them. Nothing that draws a card or a window needed to
      know about rows that are not lines; only the numbers are translated -
      the change set's lines to rows (`Splice.Remap`) and the gutter's rows
      to the numbers a reader expects (`Splice.Numbers`, old numbers on the
      removed rows). Removed rows are tinted and lit red like added ones
      green, and syntax coloured for free, since they are tokenised with the
      file; ctrl+F finds them too.
      - `GitReview.ReadHunks` keeps the text (`FileChange.RemovedText`: one
        `RemovedBlock` per run, with the new-file line that sits where it
        was and the old-file line it started at).
      - Built once, for the whole change. Stepping to one commit keeps it
        rather than rebuilding a layout that would shift at every step: that
        commit's removals show as thin markers, and the whole change's rows
        stay faintly red so they never pass for current code.
      - The change view's windows are whole files again; the text has the
        removed lines in it. It was cut into a stack of windows with red
        blocks between them for a day - a row map in one window had been
        rejected as touching ~25 line-to-position sites - and splicing the
        review text made both unnecessary. A file the change deleted has no
        card and no window, so it is still a `"removed"` block of its old
        text, with a header naming it.
      - `R` on the review map or in the change view puts the text back as it
        is, without moving the camera. On by default.
- [x] The change view's glow did not glow: it was drawn inside a window's
      clip, which cut off the halo that spills past the edge on the map. It
      is drawn after the clip is lifted, each run trimmed to the window's
      lines instead.
- [ ] Annotations and picking on spliced rows: a review's text has rows the
      file does not, and anything that records a line number there is
      counting rows. Annotating is not offered in review mode today; if it
      is, it wants the unspliced line.
- [x] The debug readout sits bottom right, above the hint bar and under
      every panel; top left it fought the "map" button and the workspace.
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
- [x] A shape's bottom edge is anchored too, so a rectangle drawn round a
      method is still round it after something is added inside. Relative to
      the *declaration's end* rather than a fingerprint of the closing
      line: that fingerprint is the line and its neighbours, and the line
      above a closing brace is exactly what changes. Roslyn knows where the
      declaration ends now; that is the thing to ask.
- [ ] A drawing is pinned by its top left corner, so one that spans two
      windows, or hangs off the bottom of one, follows only the line under
      that corner. Good enough for a mark on a method; wrong for a frame
      drawn round two of them.
- [x] The scan refreshes while Atlas is open (`SceneView.WatchRepo`): a
      change to a path the scanner would include rescans a second after the
      last one, off the UI thread, keeps the camera, drops only the changed
      files' text, and moves the open board's windows onto their code. It
      waits out a drag and a commit under review.
- [ ] A rescan reads every file again. Fine for the repos it has met; if a
      save starts to lag on a big one, reuse unchanged files' `FileRec`s
      by size and write time.
- [ ] Only shapes grow. A note over a method keeps its height, because a
      note's height is its words - which is right, but it means a note
      cannot be used to bracket something the way a rectangle can.
- [ ] Show each file as it was *at that commit*, so the code under the
      marks is the code the commit changed rather than today's. The
      snapshot already exists (`ShowSnapshot`); what is missing is doing it
      per commit as you step, and the marks would then want to be
      see-through so the lines under them still read.
- [x] Opening a review target reads the commits, the diff and the tree off
      the UI thread (`OpenTarget`, `BuildTree`). `GitReview` locks every
      public method, so a key that reaches for git meanwhile waits rather
      than sharing libgit2's handle; a result that arrives after another
      target was picked is dropped.
- [x] Toggling removed lines (`R`) reads the tree again off the UI thread,
      refused while a read is still under way (`_reading`) so it cannot
      land the other setting; the review panel opens at once and fills in
      when its list has been read.
- [x] Stepping commits reads one commit's diff off the UI thread
      (`ShowCommit`), and the whole change is kept from when the target was
      opened (`_whole`) rather than diffed again on every visit. A step
      overtaken before it starts does no work; a late one is dropped.
- [ ] A file the commit **deleted** has no card on the map, because the scan
      is of what is there now. The change view shows it now, as its old
      text; the map still has nowhere to put it - probably at the folder
      that lost it.
- [x] The mode islands sit over both side panels, which leave room at
      their top for them; in review mode they were under the commits panel
      and could not be seen. The commits list also drew only its first row
      - the same virtualising bug the tour and boards panels had.
- [x] The review panel and the commit list appear at once, saying they
      are reading, and fill in when git answers.

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
- [x] Highlighting as you type. Every occurrence on screen is marked on
      each keystroke - that costs a substring search of the lines being
      drawn and nothing else - and the one being looked at is marked more
      strongly, or two matches on a screen lose your place. The *list*
      follows after a 180ms pause, because a keystroke starts a read of
      every file not already loaded.
- [x] Previous and next: Enter and shift+Enter in the box, up and down in
      the list, F3 and shift+F3 anywhere. The matches belong to the view
      rather than the panel, so F3 still steps them once it is closed, and
      Escape clears the marks along with the selection the last jump left.
- [x] Regular expressions and whole word, `alt+R` and `alt+W` in the box.
      One pattern (`Grep.Pattern`) drives the list and the marks, so they
      cannot disagree, and it is the non-backtracking engine because the
      marks run it on every keystroke - a runaway pattern would hang the
      canvas. It refuses lookarounds and backreferences, and the hint says
      so. A pattern that matches nothing (`x*`) matches no line.

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

### Boards for agents (`BoardCli.cs`)

- [x] `Atlas.exe board "<board>" <command>`, or a script on stdin. The
      commands name code (file, symbol, 1-based lines) and ids the agent
      chooses; `Reanchor` and `PinOver` do the anchoring, so a board made
      this way drifts no more than one made by hand. The help text is the
      agent's documentation - keep it in step with the commands.
- [x] All or nothing: a script that fails saves nothing, and a board it
      created is deleted again.
- [x] `show` (outline and overlap warnings) and `render` (a PNG of the
      board or one stop), so the agent can check what it made.
- [x] From a trial where an agent documented part of Atlas with only the
      help text: `\n` in script text, `--row`, labels as wide as their
      words, `--text-size` everywhere (`--size` is a box's WxH), stops that
      remember the items they frame (`Stop.Items`) and reframe on every
      commit, and `show` warning about an arrow crossing an item.
- [x] Arrow labels: an arrow's `Text`, drawn on the middle of the shaft
      over a patch of board colour, typed into by double clicking the
      arrow, and `arrow ... --text` from the command line.
- [x] Window titles: a file window's `Text`, first in its header with the
      file and line after it, dimmer. Double click the header (or "Edit
      title"), or `window ... --title`.
- [x] The "pale grey" frame in the trial render was a shape border at its
      default alpha of 150 beside a note's solid one - by design, see
      "How solid a colour is lives in the colour". Not a bug.
- [x] The open app takes in boards changed on disk (`SceneView.WatchBoards`,
      `BoardStore.Refresh`), so an agent's board appears as it is built.
- [x] `atlas boards check`: every board's problems, one line each, exit 1
      if any - the stale kind (a file gone, code moved or vanished from under
      a window, a window never anchored, a pin to nothing, a stop framing
      nothing, a board file that will not load) and the drawn kind (overlaps,
      arrows crossing, edges nearly lined up). `show` says what code each
      window shows and each drawing covers or sits beside, `--code` prints
      it, and show, render and check all re-anchor in memory first - as Atlas
      does on opening - so they describe the board a person would see.
- [ ] An MCP server over the same commands, if a client wants one; the CLI
      covers every agent with a shell.

### Samples and fixtures

- [ ] The sample annotation on `Scanner.cs` still says the scan is "cached
      to data/scan.json"; there is no cache any more. Change the text in
      `Samples.cs` and regenerate with `--samples` (which also rewrites the
      sample boards in today's leaner format).

- [x] A third sample board, "Everything at once": every kind of item,
      overlapping, an empty frame over the lot, connectors with one end
      loose. What breaks in edit mode breaks on a board like that.
- [x] The tidy sample boards come with a tour: the whole board, then a
      stop per window. There is no other way to see a tour without making
      one by hand first.
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
data/recent.json     folders opened lately, machine specific, gitignored
.atlas/              boards (and their tours), annotations, for the repo being read
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

`Atlas.exe board help` is the command line for making boards without a
window (`BoardCli.cs`). It is single threaded, so unlike the app it may
measure text wherever it likes.

`run.cmd` opens Atlas on itself; `test.cmd` runs the unit suite. With no
argument Atlas opens an empty workspace (`Shell.ShowWelcome`) listing the
folders opened lately, and `Shell.Open` swaps one folder's whole view for
another's - so anything that outlives a view (a watcher, the git handle, a
static event) is let go of in `SceneView.Close` or the `MakeRoom` unhook.
It used to reopen whatever a scan cache last pointed at, which is how an
ignored path argument went unnoticed. A folder
passed with a trailing backslash arrives as `C:\repo"` (the backslash
escapes the closing quote), so `App.RepoFrom` cleans the argument up rather
than trusting it, and says so when a path-looking argument is not a folder -
it used to fall back to the last scan without a word. Typed unquoted into
Git Bash, a Windows path loses every backslash (`C:UsersmeRepo`);
`App.Unmangled` walks down from the drive to put them back, and takes the
answer only when exactly one folder fits.

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

**Every canvas save has its restore, and a one-window test cannot tell.**
A window's drawing saves three times - its place, its scale, its clip - and
once restored only twice, so each window left its offset under everything
drawn after it. Real boards came out scrambled, differently at each zoom
(a window off screen is skipped and so left no offset), while every test,
drawing one window with nothing after it, passed. `CanvasBalanceTests`
checks the save count after a whole board and a note drawn after two
windows; anything that changes the drawing's save and restore structure
runs against a board of several things.

**A board window draws what is on screen.** The board loop works out the
view rectangle once (`x0..y1`), skips any window or removed block outside
it, and hands a window's drawing a visible line range (`lo..hi`) - code,
review glow and removal marks all take it. Nothing was culled before, so a
window drew its whole range every frame, and the change view's whole-file
windows cost 25ms a frame on their own. Anything new drawn per line inside
a window takes the range too. `Scene.LinesDrawn` counts lines drawn, so a
test can say "about a screenful" without a clock.

**`Scene.ReadLines` does not cache, on purpose.** `_text` is filled in
alongside `_runs` by the loader, and an entry in one without the other means
`DrawCode` finds text with no syntax runs beside it and paints that whole
file in a single colour. The search reads every file in the repo, so caching
there would leave the map grey.

**How solid a colour is lives in the colour.** Eight hex digits are
AARRGGBB and six are RRGGBB; `Scene.Tinted` takes a colour that named its own
alpha at its word and gives one that did not whatever the thing drawing it
normally uses - 150 for a shape's border, which is what makes one read as a
frame round code rather than a box in front of it. "Solid", "soft" and a
typed hex all rewrite that one string rather than setting a flag beside it,
because a flag and a colour can disagree about whether something is
invisible. Covered by `ColourOpacityTests`.

**A drawing over a window belongs to the code, not to the board.** A
rectangle, note, stroke or loose arrow let go of on top of a file window is
pinned to the line under its top left corner (`Scene.PinOver`), and
`Scene.AnchorItems` moves it when that line moves. `Scene.AnchorBoard` runs
all three passes in order on open: windows first, then what is pinned to
them, then `PinLoose` picks up anything from a board made before this - where
it is now, since there is no record of where it was meant to be. Covered by
`PinnedItemTests`.

**Every gesture that changes what a drawing covers re-pins it on release.**
A pin records the line under an item and, for a shape, where its bottom
sits; reopening the board puts the item back from that record. Only a move
re-pinned: a resize by corner or wall, and dragging an arrow's end, left the
record from when the item was drawn, so reopening put the old size or the
old end back. A new gesture that reshapes an item re-pins it when it is let
go, or it has the same bug. Covered by `ResizeRepinTests`.

**`FileRec.N` is the line count from the scan, and the scan can be a second
behind.** It is retaken when files change (`WatchRepo`), but not instantly,
and not at all while a commit is being reviewed. In that gap the scan is
short. Clamping a window to that number showed
part of the file and refused to be dragged further, which reads as a limit
rather than as staleness. `Scene.LinesIn` prefers the loaded text, then the
length last read, then the scan; `Scene.NoteLength` refreshes it once when a
clip handle is grabbed, because `RangeOf` is asked on every frame and a read
there would be sixty a second. Covered by `StaleScanTests`.

**A line number is not a place either.** A board's file window stores
`Line`/`EndLine`, and inserting twenty lines above line 100 leaves the window
showing what used to be at 80-120 - different code, in the same place on the
board, with every rectangle and stroke drawn over it now pointing at the
wrong thing. A window carries the same anchor an annotation does (symbol,
offset, context fingerprint) and `Scene.AnchorWindows` moves the *range* when
a board opens, so the same code stays in the same place and the drawings need
no anchors of their own. `Scene.Reanchor(window)` records a deliberate move -
a clip, a typed range - or the next open drags it back. Covered by
`WindowDriftTests`.

The *end* of a range is anchored separately (`EndSymbol`, `EndOffset`,
`Anchors.CaptureEnd`), measured from the end of the declaration it sits in:
a window cropped to a method keeps showing its closing brace as the method
grows, and a box round two methods grows with the second. And a fingerprint
is one hash per line, matched partially - the line itself must match, and
across a whole file half its neighbours too - because the edit that happens
most is one right next to the line that was marked. Covered by
`EditScenarioTests`, which is the test to extend: it makes a run of edits,
and one edit at a time is how both of these went unnoticed.

**`Symbols.ForFile` caches a parse per path and nothing in the app used to
invalidate it.** Re-anchoring exists because the file may have changed, so it
must `Symbols.Forget` first - otherwise it resolves against the parse from
before the edit, finds the symbol at its old line, and concludes nothing
moved.

**A path is not an identity.** Board windows and annotations
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

**A press is not a drag until it has travelled.** A click on an item
wobbles by a pixel or two, and that used to move it - and the release
re-pinned and saved the board whether anything moved or not, so clicking a
box rewrote its file. A move, resize or arrow-end drag waits for
`GrabSlop` pixels from the press (`_grabbed`), and the release re-pins only
after one. A new grab gesture checks `_grabbed` the same way. Covered by
`DragSlopTests`.

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
`boards/*.json` (tours included), `groups.json` (the order of the board
groups), `annotations.json` and `images/`.

There is no save step: stores write on every change and the canvas shows
`saved: ...`. If you add state a user authors, it saves itself the same way -
and immediately, in the handler that made it, rather than leaving a dirty flag
for some later gesture to notice.

**Something else writes here too, and the disk wins.** `atlas board`, a
pull, a teammate's editor. `BoardStore` remembers the text it last read or
wrote for each file: a refresh takes in only files whose text differs from
that - the app's own saves come back as change events and must not be
mistaken for someone else's - and `Save` refuses to write over a file that
changed since, returning false. Letting go of a drag just after the command
line wrote the board used to save the app's copy over its work. A changed
board is updated in place, never replaced, because the open board, the
panel and `_lastBoard` all hold the object. Covered by `BoardReloadTests`.

**Looking at a board does not write it.** Opening one fills in what an
older or hand-made board lacks - file keys, anchors, pins - and used to
save for that alone, which turned a board someone had only opened into a
diff of every line. `SceneView.FollowCode` saves only when something moved;
what was filled in rides along with the next real edit. And a save writes
only what differs from a fresh object (`BoardStore.OmitFresh`, compared
against a new instance, so a real zero where the default is not zero is
kept), with `+` and `'` unescaped. Covered by `BoardFormatTests`.

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

**Opening a folder writes nothing into it.** Not even an empty
`.atlas/boards`: the board watcher waits for `.atlas` to appear rather than
creating it. A test that opens a missing folder must use a path inside its
own temp folder - an early version of the shell built a view onto a
missing path and the watcher created it on the real drive.

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
