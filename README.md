# Atlas

**Entirely done by Opus 5.**

A zoomable canvas for reading a codebase. Every file is a card laid out by
directory; zoom out for the shape of the whole repo, zoom in for real
syntax-highlighted source.

C# / .NET 10, Avalonia for the window and input, SkiaSharp for the canvas,
TextMate grammars for highlighting. No other toolchain - clone and run.

## Running

Double-click `run.cmd` and Atlas opens **on itself** - this repo carries its
own `.atlas/`, so there are boards and annotations to look at immediately.
Drag any other folder onto `run.cmd` to open that repo instead. `test.cmd`
runs the tests. Or from a shell:

```bash
cd app && dotnet run -- ../path/to/some/repo
```

You point it at a folder - the root of a project - and that is the whole
setup. If the folder is a git repo, review mode turns itself on; if it is
not, `P` says so and everything else works the same.

### What lands on the map

Every text file, whatever it is called. There is no list of blessed
extensions, because a list is always missing somebody's `.org`, `.el` or
`.nix`, and missing them silently.

Out: binaries, by extension and by sniffing for a NUL byte in the first few
kilobytes, which is what git itself goes on and is right about the formats no
list anticipated. Anything over 2MB. And `.git` and `.atlas`, at any setting,
because reading your own notes about a repo as cards in that repo is a hall
of mirrors.

Hidden but a keypress away: **whatever the repo's own `.gitignore` says is
not its source**, which is the only answer that is right about a repo nobody
anticipated. Nested ignore files and negations come along with it, because
the question is put to libgit2 rather than reimplemented. A file that is
already tracked is never hidden, whatever the patterns say - that is what
lets a repo commit its `.atlas/` folder.

Hidden too: build output and dependencies (`node_modules`, `bin`, `obj`,
`target`, ...) for repos that never got round to ignoring them, other dot
directories, secrets (`.env`, `*.pem`, `id_rsa`) and OS litter.

**`.`** shows the lot and rescans - `node_modules` is not a few extra cards,
it is most of the map, so the layout is rebuilt around whatever is now on it.

Whatever is skipped is counted and said out loud. A map that quietly omits
part of a repo is worse than one that shows something ugly.

### What Atlas writes, and where

**In the repo you are looking at: `.atlas/`.** Boards, annotations and
bookmarks. Every path in it is repo-relative, so it works wherever the repo
is cloned. **Commit this folder** - that is how the rest of your team gets
what you wrote. Nothing needs adding to that repo's `.gitignore`.

**In Atlas's own folder: `data/scan.json`.** The cached layout, so
`dotnet run` with no argument reopens the last repo. It records the absolute
path of the folder it scanned - the one machine-specific thing Atlas writes -
and this repo's `.gitignore` already excludes it. A cache written on another
machine is noticed and replaced rather than drawn, so a fresh clone opens on
Atlas itself instead of failing.

## Using it

A walk through the things you will actually do, in the order you will do
them. Every key mentioned is in the table further down.

**Find your way around.** The wheel zooms, dragging pans. What you see
changes with the zoom rather than just getting bigger: zoomed right out the
repo is one block per folder, labelled with as much of the path as
fits. Zoom in and each file becomes a card, then coloured bars for its lines,
then real syntax-highlighted source. `F` fits the whole map again when you
are lost.

**Get somewhere specific.** `/` searches by file name; the arrows move
through the hits and `Enter` flies the camera there, easing in and out rather
than cutting. Clicking any card flies to it the same way.

**Pick code.** At reading zoom, click a line to select it. Shift-click
extends the selection to a range, and double-click takes the whole enclosing
method. This is the unit everything else works on.

**Put it on a board.** The map is laid out for you and never rearranged by
hand; a board is where you arrange things yourself. Secondary click your
selection and pick *Add file to board*, or press `A` to send whatever you are
looking at to the board you last had open. `O` lists your boards: select one
and click *open*.

**Work on a board.** Press `E` for edit mode. Drag windows and notes around,
drag an empty patch to sweep up several at once, and resize from any corner -
the one you hold follows the pointer and the opposite one stays put.

`1` `2` `3` arm a rectangle, an ellipse or a diamond and `4` a label; none of
them appear until you drag out the box you want, because the size and the
place are the two things about a shape that are yours. The button stays lit
while the tool is armed, and `Esc` backs out. Nothing clamps a shape's
height, so a long thin divider is as available as a square.

`N` adds a note, `Y` an arrow, `ctrl+V` pastes an image, and `G` toggles
snapping. `ctrl+Z` and `ctrl+Y` undo and redo.
Hold space to pan without leaving edit mode. The `<` button top left goes
back to the map, and the camera returns exactly where you left it.

**`Esc` cancels, it does not leave.** It closes whatever is innermost - a
dialog, then a panel, then an armed tool, then the selection - and never the
board itself, because cancelling is what you want from it far more often than
leaving is.

**Draw on it.** `B` is a freehand brush and stays on until you press it
again, because you draw several strokes in a row and reaching for the key
between each one is what makes a drawing tool unusable. `[` and `]` change
the thickness and a row of swatches sets the colour, both of which appear
only while you are drawing. With something picked they recolour and rethicken
that instead, which is what a selection makes them mean anyway.

`X` is an eraser, and pressing it again walks its modes: whole elements, then
splitting strokes, then off. Whole elements takes anything it passes over.
Splitting bites a hole in a stroke and leaves the pieces as strokes in their
own right - which matters because a long line drawn in one gesture is a
single stroke, and taking the lot because you touched the end of it is not
erasing, it is undo. Splitting only ever touches ink.

`[` and `]` size whatever is in your hand, the pen or the eraser.

**Join things up.** Every element has four places a connector can meet it -
top, right, bottom and left - and they light up while you are drawing one. An
arrow dropped **on one of those nodes** ties there, either end, and follows
the box when you drag or resize it. Ending anywhere else, including inside
the element, leaves that end loose where you put it: a line crossing a box is
often just a line crossing a box.

Four points and no others is the whole reason it reads: an end free to sit
anywhere along an edge slides about as either box moves, and a diagram of
sliding lines is not a diagram.

A ring instead of a dot marks a tied end. Drag it to another side to move it,
or onto empty canvas to let it loose. Two picked items can be joined straight
from the menu.

**Annotate code.** Notes about code are written on boards, where the
surrounding code gives them their meaning. On a board, secondary click a line
inside a file window and choose *Annotate this line*. The note then appears
beside that code everywhere - including on the map, which shows notes but
does not author them. `L` lists every annotation in the repo, worst anchor
first, which is also where you repair one that has drifted.

**Leave yourself a way back.** `M` bookmarks the current view under a name.
`B` lists bookmarks, `Enter` flies to one, `Delete` removes it. `R` starts
recording a tour: bookmark a few views in order, press `R` again, and the
tour plays back with space and the arrow keys.

**Review a change.** `P` lists merged pull requests, `G` branches ahead
of the base. Opening one rescans the repo *as it was at that commit* and
lights up the files it touches - green for added, red for removed. `]` and
`[` step through the commits one at a time. `Esc` returns to the working
tree.

**Saving.** There is no save. Boards, notes and bookmarks are written into
`.atlas/` in the repo you are looking at the moment you make them, and the
canvas says `saved: ...` so you can see it happen. Commit that folder and
your team gets everything you wrote.

## Keys

| key | does |
|---|---|
| drag / wheel | pan and zoom |
| shift+wheel | pan sideways |
| click a file | fly to it |
| `/` or `Ctrl+F` | search; arrows pick, Enter flies |
| `P` | pull requests |
| `G` | branches ahead of the base |
| `E` | edit on or off |
| `S` | wheel zooms, or wheel scrolls |
| `ctrl+Z` / `ctrl+Y` | undo / redo on a board |
| space (board) | hold to pan while editing |
| `Y` | draw an arrow on a board |
| `Esc` | cancel the innermost thing: dialog, panel, tool, selection |
| `<` or alt+left | back to the map from a board |
| Backspace | same as Delete |
| `B` | freehand brush on a board; stays on until you press it again |
| `X` | eraser on a board |
| `shift+X` | eraser takes a whole stroke, or bites a hole in one |
| `[` `]` (board) | pen thickness |
| `G` (board) | snap to grid on or off |
| Delete (board) | remove what is picked |
| `1` `2` `3` | rectangle, ellipse, diamond on a board |
| `4` | label on a board |
| `ctrl+V` (board) | paste an image from the clipboard |
| `C` | gather the changed code onto one view (while reviewing) |
| `]` `[` | next / previous commit while reviewing |
| click a line | pick it (at reading zoom; on a board, while editing) |
| shift-click | extend the picked range |
| double-click | pick the whole enclosing method |
| secondary click | actions: annotate, bookmark, add to a board |

| `L` | annotations, worst anchors first |
| `M` | bookmark the current view |
| `R` | start / finish recording a tour |
| `B` | bookmarks and tours; Enter goes, Delete removes |
| space, arrows | next / previous stop while a tour is playing |
| `O` | boards panel; `C` new, `F2` rename, `F3` group, Delete removes |
| `A` | add the current file to the last opened board |
| `N` | add a board note (while on a board) |
| `F` | fit the whole map, or the whole board |
| `D` | folder outlines on or off |
| `.` | show or hide build output, dependencies and dotfiles |
| `F9` | run the benchmark |

Also `--bench`, `--stress`, `--goto x,y,scale` on the command line.

## Tests

`test.cmd` runs the suite, or from a shell:

```bash
dotnet test tests/Atlas.Tests
```

It is hermetic. Every test builds what it needs - a synthetic repo, a scratch
directory, or a real git repository with a merged pull request and an unmerged
branch - so nothing is asserted against whatever this repo happens to contain
today. That matters most for review mode: the old self-check could only run
against the repo it was handed, and printed `SKIP` when that repo had no merge
commits, which is the case for Atlas itself.

Covered: scanning and layout, search ranking, camera flights, symbol and
context anchoring, board storage, bookmark anchoring and framing, undo,
file-reference resolution across renames, image storage and pruning,
concurrent tokenising, and review mode end to end.

An end-to-end test starts the app and drives it with real keystrokes, saving
a screenshot of each step:

```powershell
powershell -STA -File uitest.ps1
```

It checks the two things a headless test still cannot see: a real window, and
the real Windows clipboard. The clipboard paste was once broken while every
self-check passed.

Some of it renders for real and reads the pixels back, because a few rules -
that a card's contents stay inside the card, for one - are about what lands
on the canvas rather than about any one number.

## How it stays smooth

Four level-of-detail tiers:

| zoom | tier | what is drawn |
|---|---|---|
| < 0.055 | folders | one rect + label per directory |
| < 0.45 | cards | one rect per file |
| < 3.2 | bars | one coloured bar per line, like a minimap |
| >= 3.2 | text | real glyphs, only the lines on screen |

Bar geometry per file is recorded once into an `SKPicture` and replayed, so
pan and zoom are a pure canvas transform with no geometry rebuild.
Construction is budgeted to 14 cards per frame so flinging into unseen
territory never blocks. Source text and TextMate tokens are produced
together on a background thread.

Camera moves use van Wijk & Nuij zoom-and-pan interpolation (`Flight.cs`),
the same curve `d3.interpolateZoom` implements. A linear lerp of x/y/zoom
drags the viewport across the world at full magnification and feels awful;
this arcs out, travels, and arcs back in.

Tokenising is serialised behind one lock. TextMate's registry and grammars
are not thread safe - tokenising two files at once corrupts the shared rule
registry into unbounded recursion, which takes the process down with a stack
overflow that cannot be caught. `--tokentest` covers it.

Redraws are driven by input, not a free-running loop. Anything that finishes
off-frame - a file load, a partially built view - calls `Scene.RequestRedraw`.
A flag does not work here: nothing watches one between frames.

## Measured

Continuous fling across the world, per-frame draw cost. 16.6ms is the 60fps
budget.

| data | tier | median | p95 | worst |
|---|---|---|---|---|
| 1,656 files | bars @ 0.5x | 1.60ms | 2.28ms | 2.67ms |
| 1,656 files | text @ 4.5x | 1.16ms | 2.04ms | 5.67ms |
| 14,904 files | bars @ 0.5x | 2.12ms | 2.88ms | 3.77ms |
| 14,904 files | text @ 4.5x | 1.19ms | 1.83ms | 2.73ms |

Syntax highlighting costs essentially nothing: the text tier draws one
`DrawText` per coloured run, and a column maps straight to an x offset
because the font is monospace.

A web prototype (Pixi/WebGL) was built and benchmarked alongside this one.
It performed the same. It was dropped because everything else - Roslyn
in-process, the language the team already works in, cloning and running with
no extra toolchain - favours .NET. Performance did not decide it; the LOD
architecture is what makes it smooth, not the stack.

## Bookmarks and tours

A bookmark is saved to the **scanned repo**, in `.atlas/bookmarks.json`, so
it travels with the code through git. It anchors to a file and a line rather
than to camera coordinates:

```json
{ "id": "14b90c60", "name": "where a frame is drawn",
  "file": "app/Scene.cs", "line": 446, "endLine": 470,
  "x": 12824, "y": 13995.2, "s": 4.08 }
```

The camera is stored too, but only as a fallback. On load the anchor is
resolved against the current layout, so a bookmark still lands correctly
after files are added, removed or moved and the map is laid out afresh. A
bookmark whose file is gone is reported as `MISSING` in the list instead of
flying somewhere arbitrary. A bookmark taken while zoomed out has no file and
keeps its camera - that is how you save a view of the whole map.

A bookmark captures a **region**, not just a point: `line`..`endLine` are
whatever was on screen when you pressed `M`. Zoom onto one method and the
bookmark is that method, with no selection step to learn. Flying back frames
those lines and marks them with an amber band, so a tour can point at
`OnStartup` rather than at the 900-line file that happens to contain it.

Framing a region is capped by readable line width - past a point, zooming
closer would run code off the side. When a card ends up wider than the
window its left edge is pinned rather than centred, because that is where
lines start. Both are covered by `--bookmarktest`.

A tour is an ordered list of bookmark ids. Record one with `R`, save a stop
at each place with `M`, finish with `R`. Playing it flies between stops with
the name shown as a caption. Deleting a bookmark removes it from every tour
that used it.

`--bookmarktest` covers the part that matters: that an anchored bookmark
still resolves to the right place after the whole layout shifts.

## Review mode

`P` lists **branches that are ahead of the base**, then merged pull requests.
Opening one lights up every changed file across the whole map, coloured green
through red by how much of its churn was additions. `]` and `[` walk the
commits, and the camera flies to whatever that commit touched. At reading
zoom, added lines get a green band and deletions a red tick.

The commits of whatever is open are listed in a panel down the right: click a
commit to switch to it, or step with `[` and `]`. Its sha, author, date and
counts live there too. Details deliberately do **not** go in a line at the
bottom of the canvas - that was unreadable once there was more than one fact
to show.

Annotations are read-only while a commit snapshot is showing. They belong to
the working tree, not to somebody else's branch.

**`C` gathers the changed code onto one view.** The map answers where a change
landed, which is the question worth asking first; it does not answer what the
change said, and on a large repo the changed files are nowhere near each
other. `C` lays out just the parts that were touched - one window per hunk,
context either side, nearby hunks merged, biggest churn first - and `]` and
`[` walk the commits without leaving. It is generated and read only: it is not
in the boards panel, nothing is written to `.atlas/`, and `C` again puts the
map back exactly where you left it.

Review mode gets **one colour channel: change**. A veil mutes the base map so
folder hues stop competing with green and red, and a changed file is drawn
as a solid block forced to at least nine pixels - at map zoom a card is
thinner than a pixel and would flicker in and out as you pan. Close in, where
the code is readable, a changed file gets an outline drawn on top of the text
rather than a wash of colour over it.

Folder outlines are hairlines (Skia stroke width 0), so they stay one pixel
at every zoom instead of shimmering in and out below a pixel.

**Pull requests come from merge commits, not from a host API.** A merge
commit's first parent is what it merged into and its second is the branch
head, so `Merge pull request #3054 from ...` yields the base, the head, and
the commit list with no token, no network, and no GitHub dependency - it
works for any host, and offline.

Limits worth knowing:

- Branches are compared against their **merge base** with the base branch, so
  a long-lived branch shows everything it has ever diverged by.
- A branch that predates a directory restructure changes paths that no longer
  exist. Those changes have nowhere on the map to land, so the caption says
  so outright - `none of these paths exist in the current scan` - rather than
  showing a blank map.
- Opening a target **rescans the repo as it was at that commit**, so such a
  branch draws against its own paths: the map itself rebuilds, folders and
  all, around folders that no longer exist. The caption says
  `[tree at this commit]` while this is in effect, and `Esc` restores the
  working tree.
- The snapshot is taken at the target's head, so stepping through individual
  commits still uses that tree. Line marks for an early commit can sit a
  little off if a later commit in the same branch moved them.
- Files the scanner skips (binaries, excluded extensions, deletions) have
  nowhere to land, so the caption reports how many of the changes are on the
  map whenever it is not all of them.

## Picking code with the mouse

At reading zoom the line under the cursor lights up, a click picks it,
shift-click extends the range, and a double-click takes the whole enclosing
declaration (Roslyn decides where it starts and ends). Below reading zoom a
click still flies to the file, so navigation and picking never fight.

An annotation keeps a permanent tint across **every line it covers**, so a
note about a method does not read as a note about its first line. The same
tint shows inside a board's file windows.

Secondary click acts on whatever is picked: annotate it, bookmark it, or add
it to a board - and the picked line range becomes the board window's range,
so a board shows the method you chose rather than the whole file. The menu
also edits and deletes annotations under the pick, and offers *New board...*
so a board can be started from the code you are looking at.

Left-handed mice need no special handling: Windows swaps the buttons before
Avalonia sees them, so "secondary" is whichever button you treat as secondary.

## Board editing

Boards are grouped in the panel. `F3` names a group (a group exists by being
named, and a blank name means ungrouped), and a board can be dragged onto
another row to reorder it or onto a heading to move it between groups.

Edit mode on a board behaves like a canvas app. A drag on empty space sweeps a
rubberband that fades out when released. The band owns its result: shrink it
back off something and that something is let go again, while ctrl or shift
keeps whatever was already picked before the drag started. Everything picked moves together, snaps to the grid live as you drag rather
than jumping at the end, and each one carries a grip at its lower right that resizes
the **box** without touching the font. Hold space to pan without leaving edit
mode. A grid shows while editing and disappears in pan mode.

The bar along the top adds a board note, a rectangle, an arrow or a file -
`file` opens the same fuzzy search the map uses and drops the chosen file on
the board. Arrows are drawn freely: arm the tool, drag, and the line follows
the mouse; once placed, either end can be dragged elsewhere.

Clicking a line inside a file window picks it, shift extends the range, and
double-clicking a note opens its text. A note written on a board shows its
words there, not only a tint. Secondary click offers only what applies to
*everything* picked - colour, copy, remove - plus
editing a note's text or a file window's line range when exactly one is
picked. Copy and paste work on any selection; a copied file window is a
reference to the code, not a copy of it.

## Mouse modes

`H`, or the toolbar in the bottom left, cycles **edit**, **pan** and
**scroll**. Pan never moves anything - a drag that starts on a board item
still pans, line picking is off, and the cursor becomes a hand. Scroll turns
the wheel into vertical movement for reading, with ctrl to zoom and shift for
horizontal. Edit is the interactive one.

Dragging locks to an axis the way a touch scroll does: once a gesture clearly
commits to horizontal or vertical it stays there until you let go, so a long
read down a file does not wander sideways.

## Identity

A **board** is identified by an id, never by its name, so renaming one is
safe: the file on disk is renamed for legibility and nothing points at it by
name.

A **file** is a harder problem, because board windows and notes reference it
by path and paths change. Every reference also stores a fingerprint - a hash
of its first forty meaningful lines with whitespace stripped - so a reference
that cannot find its path falls back to a uniquely named file, and then to
matching content. A file that was moved is found by name; one that was renamed
is found by its content; references then quietly repoint themselves. Only a
file that is genuinely gone reports as gone.

## Saving

Nothing needs saving. Bookmarks, boards and annotations are written to
`.atlas/` in the scanned repo the moment you make them, and the canvas says
`saved: ...` each time so it is visible rather than merely true.

This repo has its own `.atlas/`, committed: two sample boards and a handful
of annotations, all of them about Atlas itself. Open Atlas on its own folder
and you get a tour of the code you are looking at. Regenerate them with
`dotnet run -- --samples <repo>`; they are built through the real anchoring
code, so their symbols and fingerprints are genuine.

## Where notes come from

Notes are **written on boards**, never on the map. Open a board, secondary
click a line inside a file window, and the menu resolves that point down to a
source line and offers a note there - along with editing or deleting any note
already covering it.

The map still *shows* notes, because they are attached to code and that is
where the code is, but it cannot author them: its menu lists them read-only.
The `L` panel stays as it was, for finding notes and repairing orphans -
without it a note whose board was deleted would be unreachable.

This keeps symbol anchoring exactly as it was. A note is still anchored to a
declaration plus a fingerprint and still survives edits; only the place you
write it has moved.

## Annotations

A note attached to a place in code, stored in the scanned repo at
`.atlas/annotations.json`:

```json
{ "text": "the camera decides what a click means",
  "file": "app/Scene.cs",
  "symbol": "Atlas.Scene", "offset": 5,
  "line": 29, "context": "FD04472DB562" }
```

The symbol comes from Roslyn, parsed **syntax only** - no compilation, no
MSBuild workspace. An annotation needs a declaration's name and line range,
not semantic binding, and parsing one file takes milliseconds. Files are
parsed on demand, never all 1,650 up front.

Resolution runs in this order, and reports which one won:

| kind | meaning |
|---|---|
| Symbol | declaration found, and the exact lines found inside it |
| Context | declaration gone (renamed?), but the same lines found elsewhere |
| Line | nothing moved |
| Drifted | declaration still there, the annotated lines are not |
| Orphan | neither survives |

`Drifted` exists because "the method is still here but your lines are gone"
is worth more than throwing the annotation away. Orphans and drifts sort to
the top of the `L` panel.

The fingerprint ignores whitespace, so reindenting does not break an anchor.
Languages Roslyn cannot parse get no symbol and anchor by context alone,
which still survives edits above them.

`--annotationtest` covers the cases that matter: 21 lines inserted above, a
sibling method added, reindentation, a rename, deletion, and a Python file.

## Boards

The map is auto-laid-out and nothing on it is ever dragged. Hand arrangement
lives on **boards**: small canvases that *reference* files rather than
containing them, stored one JSON file per board under `.atlas/boards/` so
two people editing different boards never conflict.

A board item is either a **file window** - a path plus a line range, drawn by
the same card code as the map, just clipped and scaled - or a **note**. The
same file can appear on any number of boards, and adding files to the repo
disturbs none of them. A window whose file is gone draws as `missing` rather
than vanishing.

Opening a board keeps the map camera, so leaving with `Esc` puts you back
exactly where you were. A board draws on an indigo background instead of the
map's blue-black, so it is obvious which one you are looking at.

### Images

A board takes images too: `ctrl+V` pastes whatever is on the clipboard, and
"Add image..." picks a file. Either way the bytes are copied into
`.atlas/images/` so they travel with the repo, and the item stores only the
file name.

Nothing is stored as it arrived. An image is scaled down to 2000px on its
long side, encoded as PNG, and re-encoded as JPEG when the PNG is over 400KB
and the image has no transparency - a screenshot that lands as four megabytes
of clipboard DIB usually comes to rest a few hundred kilobytes. Resizing
keeps the aspect ratio, because a squashed screenshot is never what anyone
wanted.

Deleting an image from a board does not delete the file straight away: undo
could still bring the item back. Files nothing references are swept when you
leave the board, and when a board is deleted, both points where the undo
history is already gone.

## Folders

A folder is one directory. Each gets a hue from the golden-ratio sequence,
so neighbours never collide. The hue is drawn twice: as the outline and label
far out, and as the tint on every card's header bar at any zoom. That second
channel is what tells you where you are once the outline is off-screen.
`D` hides the outlines; the tint stays.

The map is never rearranged by hand. Auto-layout and manual placement cannot
both own one surface - auto-layout has to regenerate when the repo changes,
and manual placement breaks the moment it does. Arbitrary arrangement is
meant to live on *boards*: small hand-made canvases that reference files
rather than containing them, so the same file can appear on many boards and
adding files to the repo disturbs none of them. Not built yet.

## Not built yet

- **Watching the disk.** Edits made while Atlas is open are not noticed. The
  scan and the highlighting are from when you opened it; annotations
  re-anchor on the next launch, so nothing is lost, but the view goes stale.
- **Portals.** Jumping from a place in one file to a related place in
  another, as a first-class thing rather than a bookmark.
- **Drawing.** A board has rectangles and arrows, not a diagram tool.
- **Other languages.** The map, search, highlighting and boards work for
  every extension the scanner reads. Symbol anchoring is C# only - Roslyn
  parses the declarations. Elsewhere a note falls back to its surrounding
  context, which survives edits but not a move to another file.
- **Other platforms.** Every dependency is cross-platform - .NET 10, Avalonia,
  SkiaSharp, Roslyn, LibGit2Sharp - and there is no Windows-only target
  framework. Two things are not: pasting an image reads the Windows clipboard
  through user32, and returns nothing elsewhere ("Add image..." still works);
  and the `.cmd` and `.ps1` scripts are Windows shells for `dotnet run`.
  Fonts fall back through Cascadia Mono, DejaVu Sans Mono, Menlo and
  Liberation Mono when Consolas is missing. None of this has been run on
  Linux or macOS yet, so treat it as "should build and run", not "does".
