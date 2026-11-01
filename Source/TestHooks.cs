using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustSplitMap;

// for testing without touching the game, all off unless the environment variable is set:
// JSM_TEST_LOAD=<save>   loads that save from the main menu and pauses it (JSM_TEST_RUN=1 lets time run)
// JSM_TEST_SHOTS=<dir>   then opens split screen with both halves on the same cells, pans, zooms and switches,
//                        then puts the current map on both halves and switches again,
//                        with a screenshot after each in dir,
//                        and quits
public static class TestHooks
{
    private static readonly string loadName = Environment.GetEnvironmentVariable("JSM_TEST_LOAD");
    private static readonly string shotDir = Environment.GetEnvironmentVariable("JSM_TEST_SHOTS");
    private static bool loadStarted;
    private static bool lockBefore;
    private static int playingFrames;

    public static void Tick()
    {
        if (loadName == null)
        {
            return;
        }
        if (!loadStarted && Current.ProgramState == ProgramState.Entry && !LongEventHandler.AnyEventNowOrWaiting)
        {
            loadStarted = true;
            GameDataSaveLoader.LoadGame(loadName);
            return;
        }
        bool playing = Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null && !LongEventHandler.AnyEventNowOrWaiting;
        if (!playing)
        {
            return;
        }
        playingFrames++;
        if (playingFrames == 5)
        {
            Find.TickManager.CurTimeSpeed = Environment.GetEnvironmentVariable("JSM_TEST_RUN") == "1" ? TimeSpeed.Normal : TimeSpeed.Paused;
            Log.Message($"[JustSplitMap] test save loaded, {Find.Maps.Count} maps");
        }
        if (shotDir == null)
        {
            return;
        }
        CameraDriver driver = Find.CameraDriver;
        Vector3 root = Find.CurrentMap.rememberedCameraPos.rootPos;
        switch (playingFrames)
        {
            case 30:
                Directory.CreateDirectory(shotDir);
                Shot("0-before");
                break;
            case 60:
                SplitView.Start();
                break;
            case 90:
                // the other half on the same cells as the current one, where things could leak from one to the other
                SplitView.SetOtherView(root, driver.ZoomRootSize);
                break;
            case 150:
                Shot("1-split-same-cells");
                break;
            case 180:
                driver.SetRootPosAndSize(root + new Vector3(8f, 0f, 0f), driver.ZoomRootSize);
                break;
            case 240:
                Shot("2-panned");
                break;
            case 270:
                driver.SetRootPosAndSize(root + new Vector3(0f, 0f, 8f), driver.ZoomRootSize * 1.3f);
                break;
            case 330:
                Shot("3-panned-zoomed");
                break;
            case 360:
                SplitView.SwitchToOther();
                // the frame the switch happens in
                Shot("4a-switch-frame");
                break;
            case 420:
                Shot("4-switched");
                break;
            case 450:
                SplitView.SwitchToOther();
                break;
            case 510:
                Shot("5-switched-back");
                break;
            case 520:
                // a real click through the game's UI on the middle of the other half
                Click(SplitView.MouseOverOtherSideCenter);
                break;
            case 525:
                Log.Message($"[JustSplitMap] after the click: map {Find.CurrentMap}, other half {SplitView.OtherMap}");
                break;
            case 530:
                Shot("5b-clicked-other");
                break;
            case 540:
                // the current map on both halves
                SplitView.Show(Find.CurrentMap);
                SplitView.SetOtherView(root + new Vector3(-20f, 0f, 0f), driver.ZoomRootSize);
                break;
            case 600:
                Shot("6-same-map");
                break;
            case 630:
                SplitView.SwitchToOther();
                Shot("7a-same-map-switch-frame");
                break;
            case 690:
                Shot("7-same-map-switched");
                break;
            case 720:
                // the picker, then the home map put on the right half from it
                Find.WindowStack.Add(new MapPicker(new Vector2(UI.screenWidth / 2f, UI.screenHeight - 60f)));
                break;
            case 750:
                Shot("8-picker");
                break;
            case 760:
                Find.WindowStack.WindowOfType<MapPicker>()?.Close();
                SplitView.SetMap(1, Find.Maps[0]);
                break;
            case 820:
                Shot("9-home-on-right");
                break;
            case 850:
                // like clicking a pawn of the right half's map on the colonist bar, lock off: it goes to the left
                JustSplitMapMod.Settings.lockSides = false;
                JumpToPawnOn(Find.Maps[0]);
                break;
            case 910:
                Shot("10-jump-unlocked");
                break;
            case 920:
                SplitView.SetMap(0, Find.Maps[1]);
                break;
            case 950:
                // lock on: it goes to the right half, which has that map
                JustSplitMapMod.Settings.lockSides = true;
                JumpToPawnOn(Find.Maps[0]);
                break;
            case 1010:
                Shot("11-jump-locked");
                JustSplitMapMod.Settings.lockSides = false;
                break;
            case 1040:
                // a real click on a colonist bar portrait over the other half: the bar jumps, the half doesn't switch
                // first. The left half plays here, so a portrait on the right
                ClickColonistBarOnOther();
                break;
            case 1100:
                Shot("12-colonist-bar-click");
                break;
            case 1130:
                // the split button: closes, then opens with the map you're on on both halves
                lockBefore = JustSplitMapMod.Settings.lockSides;
                PressButton(0);
                PressButton(0);
                break;
            case 1190:
                Shot("13-button-same-map");
                break;
            case 1200:
                // what clicking the other map's square does: it goes on the half you play. Fake clicks can't
                // reach inside a window, so it's called straight
                Map other = Find.Maps.First(m => m != Find.CurrentMap);
                SplitView.SetMap(SplitView.ActiveSide, other);
                break;
            case 1260:
                Shot("14-square-clicked");
                break;
            case 1270:
                // right click on the split button locks
                PressButton(1);
                break;
            case 1330:
                Shot("15-locked");
                break;
            case 1340:
                // back to how it was, a frame after the shot since the game takes it at the end of the frame
                JustSplitMapMod.SetLock(lockBefore);
                break;
            case 1360:
                // the line moved to 30% of the screen, then the halves switched
                JustSplitMapMod.Settings.splitRatio = 0.3f;
                break;
            case 1420:
                Shot("16-line-at-30");
                break;
            case 1430:
                SplitView.SwitchToOther();
                break;
            case 1490:
                Shot("17-line-at-30-switched");
                break;
            case 1500:
                // the keys through the UI: Q plays the other half, E locks
                Key(KeyCode.Q);
                break;
            case 1560:
                Shot("18-key-q");
                break;
            case 1570:
                Key(KeyCode.E);
                break;
            case 1575:
                // the line back first, E saves the settings
                JustSplitMapMod.Settings.splitRatio = 0.5f;
                Key(KeyCode.E);
                break;
            case 1600:
                Log.Message("[JustSplitMap] test done");
                Root.Shutdown();
                break;
        }
    }

    private static void ClickColonistBarOnOther()
    {
        ColonistBar bar = Find.ColonistBar;
        float middle = SplitView.DividerX;
        for (int i = 0; i < bar.DrawLocs.Count; i++)
        {
            Vector2 loc = bar.DrawLocs[i];
            bool otherSide = SplitView.IsActiveSide(0) ? loc.x > middle : loc.x + bar.Size.x < middle;
            if (otherSide && bar.Entries[i].pawn != null)
            {
                Log.Message($"[JustSplitMap] clicking {bar.Entries[i].pawn} on the colonist bar ({bar.Entries[i].pawn.MapHeld}), before: left {SplitView.MapOn(0)}, right {SplitView.MapOn(1)}, playing left {SplitView.IsActiveSide(0)}");
                Click(loc + bar.Size / 2f);
                return;
            }
        }
        Log.Warning("[JustSplitMap] no colonist bar portrait over the other half");
    }

    private static void Key(KeyCode key)
    {
        bool before = SplitView.IsActiveSide(0);
        Event old = Event.current;
        try
        {
            Event.current = new Event { type = EventType.KeyDown, keyCode = key };
            Find.UIRoot.UIRootOnGUI();
        }
        finally
        {
            Event.current = old;
        }
        Log.Message($"[JustSplitMap] pressed {key}: playing left {before} -> {SplitView.IsActiveSide(0)}, locked {JustSplitMapMod.Settings.lockSides}");
    }

    private static void PressButton(int button)
    {
        Event old = Event.current;
        Event.current = new Event { type = EventType.MouseDown, button = button };
        try
        {
            DefDatabase<MainButtonDef>.GetNamed("JSM_Split").Worker.Activate();
        }
        finally
        {
            Event.current = old;
        }
        Log.Message($"[JustSplitMap] pressed the split button with {button}: split {SplitView.Active}, left {SplitView.MapOn(0)}, right {SplitView.MapOn(1)}, locked {JustSplitMapMod.Settings.lockSides}");
    }

    private static void JumpToPawnOn(Map map)
    {
        Pawn pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
        Log.Message($"[JustSplitMap] jumping to {pawn} on {map}, before: left {SplitView.MapOn(0)}, right {SplitView.MapOn(1)}, playing left {SplitView.IsActiveSide(0)}");
        CameraJumper.TryJumpAndSelect(pawn);
    }

    // moves the game's mouse there and sends a left click through the play UI, like the player clicking
    private static void Click(Vector2 uiPos)
    {
        UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
        {
            Log.Warning("[JustSplitMap] no mouse to click with");
            return;
        }
        Vector2 screen = new Vector2(uiPos.x * Prefs.UIScale, (UI.screenHeight - uiPos.y) * Prefs.UIScale);
        AccessTools.Method(mouse.position.GetType(), "SetValue").Invoke(mouse.position, new object[] { screen });
        Log.Message($"[JustSplitMap] clicking at {uiPos}, mouse over other half {SplitView.MouseOverOther}, window there {Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted)}");
        Event old = Event.current;
        try
        {
            foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
            {
                Event.current = new Event { type = type, button = 0, clickCount = 1, mousePosition = uiPos };
                Find.UIRoot.UIRootOnGUI();
                Log.Message($"[JustSplitMap] {type} ended as {Event.current.type}, map {Find.CurrentMap}");
            }
        }
        finally
        {
            Event.current = old;
        }
    }

    private static void Shot(string name)
    {
        CameraDriver driver = Find.CameraDriver;
        Log.Message($"[JustSplitMap] halves: left {SplitView.MapOn(0)}, right {SplitView.MapOn(1)}, playing left {SplitView.IsActiveSide(0)}");
        Log.Message($"[JustSplitMap] shot {name}: map {Find.CurrentMap}, root {Find.CurrentMap.rememberedCameraPos.rootPos} size {driver.ZoomRootSize}, camera {Find.Camera.transform.position} ortho {Find.Camera.orthographicSize}, view {driver.CurrentViewRect}");
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
    }
}

[HarmonyPatch(typeof(Root_Entry), nameof(Root_Entry.Update))]
public static class Root_Entry_Update_Patch
{
    public static void Postfix() => TestHooks.Tick();
}

[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
public static class Root_Play_Update_Patch
{
    public static void Postfix() => TestHooks.Tick();
}
