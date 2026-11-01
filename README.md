# JustSplitMap

Split the screen in two and see two maps at the same time, or two places of the same map.

Click the split button at the bottom bar and the screen splits, with the map you're on on both halves. You play on one half and the other is only to look at. Click or scroll on the other half and you play there instead.

Each half has a column of squares at its edge, one for each map with colonists. Click one to show that map on that half, right click to show it on the other half. The square with a white border is the map you play.

When you click someone on the colonist bar they show on the half you play. If you lock it, they show on the half that already has their map instead, and someone on a third map changes the half you play. Right click the split button or click the little lock under the squares to lock or unlock, the button is filled in while it's locked.

You can drag the line between the halves to make one bigger, double click it to put it back in the middle.

Keys: Q plays the other half, E locks or unlocks and X splits the screen. You can change them in the game's keyboard options.

It works with Camera+ and with ReShade for RimWorld, both halves look the same.

In the options you can have a panel from the split button instead of the squares, and the other map in a window instead of half the screen.

You can add it mid playthrough.

The icons are SVGs in Art, Art/render.sh makes the PNGs.

To use it download the zip from Releases and extract it into your RimWorld/Mods folder. Needs Harmony.

The game only draws the map you're on, so each update the mod makes the other map the current one for a moment, puts the game's camera on it so camera mods treat it the same, and draws it into a texture. In split screen that texture goes into the scene over the other half, before the post processing, that's why ReShade works on both. The drawing part is kept apart from the split and the window so JustDesktop or ReShade for RimWorld can use it later.

To build it run dotnet build Source -c Release.

To test it without playing, start the game with JSM_TEST_LOAD=<save name> and JSM_TEST_SHOTS=<folder>. It loads the save, opens split screen, pans, zooms and switches, clicks the other half through the game UI, does the same with the current map on both halves, opens the map panel and picks from it, uses the split button and the squares, locks with a right click, moves the line to 30%, presses Q and E, jumps to a pawn with the lock off and on, clicks a colonist bar portrait over the other half, takes a screenshot after each step and quits. JSM_TEST_RUN=1 lets time run.

CC0, you can do whatever you want with it, no need to give credits.

AI was used to help code/fix issues
