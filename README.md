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

Or start it with no folder at all: it opens an empty workspace with **open
folder...** and the folders you opened lately, one click each. The
workspace (`Tab`) has *open folder...* too, to switch repo without
restarting.

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

The map follows the disk while it is open: change, add or delete a file and
it is rescanned a second later, in the background, without moving the camera
or making the rest of the map flicker. Build output and the other hidden
folders are not watched.

Whatever is skipped is counted and said out loud. A map that quietly omits
part of a repo is worse than one that shows something ugly.

### What Atlas writes, and where

**In the repo you are looking at: `.atlas/`.** Boards (with their tours)
and annotations. Every path in it is repo-relative, so it works wherever the repo
is cloned. **Commit this folder** - that is how the rest of your team gets
what you wrote. Nothing needs adding to that repo's `.gitignore`.

**In Atlas's own folder: `data/recent.json`.** The folders you opened lately,
for the empty workspace to list - the one machine-specific thing Atlas
writes, and this repo's `.gitignore` already excludes it. Opening a folder
never writes anything into it: an `.atlas` appears only once you make a
board or a note.

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
looking at to the board you last had open. `Tab` opens the **workspace** from
anywhere: Home - the map - at the top, then your boards by group. Select one
and click *open*; long names wrap. Every side panel - the workspace, the
tour, the commits - drags wider from its inner edge, and the bars, toggles
and dialogs move over to stay clear of whatever is open.

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
Home, in the workspace (`Tab`, then `H`), goes back to the map, and the
camera returns exactly where you left it.

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
does not author them.

An annotation is attached to the code, not to a line number, so it stays put
when something is inserted above it and finds its way back after a rename.

**Or keep it to one board.** *Annotate for this board only* writes a note
that shows on the board you are building and nowhere else. "This is the hot
path" is about the code and belongs everywhere; "this is step 2 of what I am
explaining here" is about the board, and would be noise on the map and on
everyone else's boards. A note can be moved between the two at any time.

`L` lists every annotation in the repo whatever its scope, worst anchor
first, which is where you repair one that has drifted - and where you select
several and make them a board's own, or send them back to everywhere, in one
go.

**Walk someone through a board.** On a board, frame the view you want and
press `M`: that is a stop. Move, `M` again, and so on. `P` plays the stops in
order like slides, flying between them - the arrows step, `Esc`
stops. `shift+M` opens the list of stops down the right: click one to look
at it, double click to play from it, drag to reorder, and rename or delete
from the buttons. A stop remembers the *region* you were looking at, so it
frames the same things in a smaller window. The tour is saved in the
board's own file.

**Review a change.** `P` lists pull requests - open ones first, then
merged - and `G` branches ahead of the base. Open pull requests come from
GitHub through the `gh` command line tool, signed in; without it you get the
merged ones only. Opening one rescans the repo *as it was at that commit* and
lights up the files it touches - green for added, red for removed. `]` and
`[` step through the commits one at a time. `Esc` returns to the working
tree.

**Saving.** There is no save. Boards, tours and notes are written into
`.atlas/` in the repo you are looking at the moment you make them, and the
canvas says `saved: ...` so you can see it happen. Commit that folder and
your team gets everything you wrote.

## Keys

| key | does |
|---|---|
| drag / wheel | pan and zoom |
| shift+wheel | pan sideways |
| click a file | fly to it |
| `/` | search file names; arrows pick, Enter flies |
| `Ctrl+F` | search what the files say; `Alt+R` regex, `Alt+W` whole word, `Enter`/`F3` next, `Shift` back |
| `P` | pull requests |
| `G` | branches ahead of the base |
| `E` | edit on or off - on a board, the top right shows edit and snap side by side, under wheel-zoom |
| `S` | wheel zooms, or wheel scrolls |
| `ctrl+Z` / `ctrl+Y` | undo / redo on a board |
| `Y` | draw an arrow on a board |
| `Esc` | cancel the innermost thing: dialog, panel, tool, selection |
| `Tab` | the workspace: Home and every board, from anywhere |
| space | spotlight: dim everything but a circle round the pointer, for explaining on a call; `Alt`+wheel sizes it, space or `Esc` turns it off |
| `Tab`, then `H` | Home: back to the map from a board |
| Backspace | same as Delete |
| `B` | freehand brush on a board; stays on until you press it again |
| `X` | eraser on a board |
| `shift+X` | eraser takes a whole stroke, or bites a hole in one |
| `[` `]` (board) | pen thickness |
| `G` (board) | snap to grid on or off, for moving and resizing alike |
| Delete (board) | remove what is picked |
| `ctrl+[` `ctrl+]` (board) | send what is picked back, or forward, one step; with `shift`, all the way |
| `1` `2` `3` | rectangle, ellipse, diamond on a board |
| `4` | label on a board |
| `ctrl+V` (board) | paste an image from the clipboard |
| `C` | gather the changed code onto one view (while reviewing) |
| `R` | while reviewing: show or hide the removed lines, in red where they were |
| `]` `[` | next / previous commit while reviewing |
| double-click (board, editing) | type into a note, shape or label; on an arrow, its label; on a window's header, its title |
| click a line | pick it (at reading zoom; on a board, while editing) |
| shift-click | extend the picked range |
| double-click | pick the whole enclosing method |
| secondary click | actions: annotate, add to a board |

| `L` | annotations, worst anchors first |
| `M` | capture the view as a tour stop (on a board) |
| `shift+M` | the board's tour: its stops, to preview, reorder, rename, delete |
| `P` | play the board's tour (on a board) |
| arrows | next / previous stop while a tour is playing |
| in the workspace | `C` new, `F2` rename, `F3` group, Delete removes; drag a board between groups, or a group heading to reorder groups; `Esc` cancels a drag |
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
context anchoring, board storage and tours, framing a range of lines, undo,
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

## Tours

A tour belongs to a board and is stored in it, as `stops`:

```json
"stops": [
  { "name": "the whole board", "x": 640, "y": 900, "w": 1500, "h": 1920 },
  { "name": "Scanner.cs", "x": 640, "y": 312, "w": 1400, "h": 744 }
]
```

Each stop is the centre and size of the region that was on screen, in board
units - not a zoom level. Playing fits that region into whatever part of the
window is free, so a stop captured full screen still frames the same things
in a small window, or with the stops panel open down the side.

While the tour panel is open, every stop is drawn on the board as a faint
dashed frame with its number in the corner, the selected one brighter, so
the list can be read against what it frames. A stop made from the command
line (`stop --frame a,b`) also remembers the items it frames, and is fitted
round them again whenever the command line saves the board.

There used to be bookmarks and tours on the map as well. They went: a
bookmark was a tour with one stop, and something worth pointing somebody at
belongs on a board, next to the notes about it.

## Review mode

`P` lists **open pull requests**, then merged ones. A merged pull request is
found by its merge commit, so it needs nothing but git; an open one has no
merge commit and nothing local says it exists, so the list asks GitHub
through `gh pr list`, and the open ones join the top when the answer comes
back. Opening one whose commits were never fetched fetches them
(`git fetch origin pull/N/head`) first. `G` lists branches.
Opening one lights up every changed file across the whole map, coloured green
through red by how much of its churn was additions. `]` and `[` walk the
commits, and the camera flies to whatever that commit touched. At reading
zoom, added lines are tinted green and **removed lines are put back as text,
tinted red, where they were** - the review's copy of each changed file has
them spliced in, so a card or window simply contains them, syntax coloured,
with the old line numbers in the gutter. `R` takes them out again. Stepping
to one commit keeps the whole change's layout and marks that commit's
removals with a thin red line, so nothing shifts under you as you step.

Reading a target - its commits, its diff, every file of its tree - happens
in the background, and so does `R`: the window stays usable while git works,
and the panel opens at once, saying it is reading, and fills in when the
answer arrives.

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
other. `C` lays out every changed file as one whole window - real code, with
the removed lines in it and the changes glowing - biggest churn first, and
opens looking at the first change rather than at line one. A file the change
deleted has no window, so it appears as a red block of its old text. `]` and
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

Secondary click acts on whatever is picked: annotate it, or add it to a
board - and the picked line range becomes the board window's range,
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
than jumping at the end - resizing too, so edges line up as well as corners, and each one carries a grip at its lower right that resizes
the **box** without touching the font. A grid shows while editing and
disappears in pan mode.

The bar along the top adds a board note, a rectangle, an arrow or a file -
`file` opens the same fuzzy search the map uses and drops the chosen file on
the board. Arrows are drawn freely: arm the tool, drag, and the line follows
the mouse; once placed, either end can be dragged elsewhere.

A press does not become a drag until the pointer has travelled a few
pixels, so clicking something to pick it never nudges it - or rewrites the
board's file for a move nobody made.

Clicking a line inside a file window picks it, shift extends the range, and
double-clicking a note, shape or label types into it where it sits. An arrow
takes a label the same way, drawn on the middle of the line, and a file
window takes a title in its header, ahead of the file name and line. A note written on a board shows its
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

Nothing needs saving. Boards, their tours and annotations are written to
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

A board item is a **file window** - a path plus a line range, drawn by the
same card code as the map, just clipped and scaled - or something drawn:
notes, labels, rectangles, ellipses, diamonds, arrows, freehand ink, images. The
same file can appear on any number of boards, and adding files to the repo
disturbs none of them. A window whose file is gone draws as `missing` rather
than vanishing.

A board changed on disk - by the command line, a pull, a teammate - is read
again while it is open, and the canvas shows it. If Atlas was about to save
over such a change, it does not: the disk wins.

Opening a board keeps the map camera, so leaving with `Esc` puts you back
exactly where you were. A board draws on an indigo background instead of the
map's blue-black, so it is obvious which one you are looking at.

### Boards from the command line, for AI agents

`Atlas.exe board` builds and edits boards without opening a window. It is
meant for a coding agent - Claude Code or anything else with a shell - to
read a codebase and leave behind a board that explains it, with a tour
through it, which a person then opens in Atlas.

It exists because a board's json is not something to write by hand. A file
window carries an anchor (the declaration it sits in, how far down it,
fingerprints of the nearby lines, a key for the file), and a box round a
method needs the exact height of that method's lines at that window's
scale. An agent writing the file would guess both, and the board would drift
the first time the code moved. The commands talk in **ids the agent picks,
files, symbols and line numbers**, and Atlas's own anchoring code works out
the rest - so the board stays on its code as the code changes, exactly as
one made by hand does.

**Setup.** Build once, then run the exe from the repo being documented (or
pass `--repo`).

```bash
dotnet build app                                    # once
app/bin/Debug/net10.0/Atlas.exe board help          # the full guide, written for an agent
```

**Pointing an agent at it.** `board help` is the whole manual - commands,
placement, sizes, colours, a checklist for a good board - and it is what an
agent should read first. Something like this in a prompt, or in the
documented repo's `CLAUDE.md` / `AGENTS.md`, is enough:

```
To document this codebase visually, use Atlas boards. Run
`<path to Atlas>/app/bin/Debug/net10.0/Atlas.exe board help` and follow it.
Render the board and look at the images before calling it done.
```

**What a script looks like.** One command per line, piped in; `#` starts a
comment, `\n` in text is a line break.

```
new --group docs
label title "How login works" --text-size 30
window login Auth.cs AuthService.Login --below title --title "Entry point"
box why --around login:51-54 --color red
note n1 "the session starts here" --on login:51 --text-size 9
window token TokenStore.cs Issue --row login
arrow a1 login token --text "issues"
stop "Entry point" --frame login,n1
stop "Where the token comes from" --frame login,token
render board.png --stops
```

```bash
Atlas.exe board "How login works" < script.txt
```

What makes it workable for a model:

- **All or nothing.** A mistake on any line saves nothing and names the line,
  and the error says what is there instead (`no "Logn" in Auth.cs - there is:
  AuthService.Login(1), AuthService.Logout(0)`), so the agent fixes the
  script and runs it again.
- **Placement without coordinates.** `--right-of`, `--below`, `--row`, or
  nothing at all; anything placed that way is nudged until it overlaps
  nothing. `--at x,y` exists as an escape hatch.
- **It can check its own work.** `show` prints every item, where it is and
  what code it shows, with warnings for overlaps and arrows crossing things.
  `render` draws the board, one stop, or chosen items to a PNG - a model that
  can see images looks at it and fixes what reads badly.
- **Reviewing boards, not only writing them.** `atlas boards check` goes
  over every board in the repo and prints one line per problem: files that
  are gone, code that moved or vanished from under a window, windows never
  anchored, drawings pinned to nothing, tour stops framing nothing, board
  files that will not load, overlaps, arrows crossing things, edges a few
  units off lining up. It exits 1 if it found anything. Then `show --code`
  on a board says what each frame covers and what each note sits beside -
  the lines, and the method they are in - as Atlas would open it, so an
  agent can judge whether the board still says something true.
- **Editing boards people made.** `set`, `move`, `rm`, `stop`, `unstop` work
  on any board, and the open app picks up the change on its own.

Status: working and tested, and tried end to end by an agent that learned it
from `board help` alone and built a ten-stop board explaining how anchoring
works; what it tripped on was fixed. Symbols are found by Roslyn, so they
work in C#; other languages use `--lines`. There is no MCP server - the
command line covers every agent with a shell.

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
adding files to the repo disturbs none of them. See Boards.

## Not built yet

- **Portals.** Jumping from a place in one file to a related place in
  another, as a first-class thing rather than a tour stop.
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
