using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustSplitMap;

public enum ViewMode
{
    Split,
    Window
}

public enum MapPicking
{
    Squares,
    Panel
}

public class JustSplitMapSettings : ModSettings
{
    public ViewMode mode = ViewMode.Split;
    public MapPicking picking = MapPicking.Squares;
    public int updatesPerSecond = 60;
    public bool lockSides;
    public float splitRatio = 0.5f;
    public Rect windowRect;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref mode, "mode", ViewMode.Split);
        Scribe_Values.Look(ref picking, "picking", MapPicking.Squares);
        Scribe_Values.Look(ref updatesPerSecond, "updatesPerSecond", 60);
        Scribe_Values.Look(ref lockSides, "lockSides");
        Scribe_Values.Look(ref splitRatio, "splitRatio", 0.5f);
        Scribe_Values.Look(ref windowRect, "windowRect");
    }
}

public class JustSplitMapMod : Mod
{
    public static JustSplitMapSettings Settings;

    public JustSplitMapMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<JustSplitMapSettings>();
        new Harmony("tiagocc0.JustSplitMap").PatchAll();
        LongEventHandler.ExecuteWhenFinished(ButtonIcon.Refresh);
    }

    public override string SettingsCategory() => "JustSplitMap";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard list = new Listing_Standard();
        list.Begin(inRect);
        ViewMode old = Settings.mode;
        if (list.RadioButton("JSM_ModeSplit".Translate(), Settings.mode == ViewMode.Split, tooltip: "JSM_ModeSplitDesc".Translate()))
        {
            Settings.mode = ViewMode.Split;
        }
        if (list.RadioButton("JSM_ModeWindow".Translate(), Settings.mode == ViewMode.Window, tooltip: "JSM_ModeWindowDesc".Translate()))
        {
            Settings.mode = ViewMode.Window;
        }
        if (Settings.mode != old)
        {
            // whatever was open was the other mode
            Close();
        }
        list.Gap();
        list.Label("JSM_Picking".Translate());
        if (list.RadioButton("JSM_PickSquares".Translate(), Settings.picking == MapPicking.Squares, tooltip: "JSM_PickSquaresDesc".Translate()))
        {
            Settings.picking = MapPicking.Squares;
        }
        if (list.RadioButton("JSM_PickPanel".Translate(), Settings.picking == MapPicking.Panel, tooltip: "JSM_PickPanelDesc".Translate()))
        {
            Settings.picking = MapPicking.Panel;
        }
        list.Gap();
        list.Label("JSM_UpdatesPerSecond".Translate(Settings.updatesPerSecond));
        Settings.updatesPerSecond = Mathf.RoundToInt(list.Slider(Settings.updatesPerSecond, 1f, 60f));
        list.Label("JSM_UpdatesPerSecondDesc".Translate());
        list.End();
    }

    public static void Close()
    {
        if (SplitView.Active)
        {
            SplitView.Stop();
        }
        SecondMapWindow.Instance?.Close();
    }

    public static string Label(Map map)
    {
        return map.Parent?.LabelCap ?? map.ToString();
    }

    // the split, or the window, with the map you're on, or closes it
    public static void ToggleView()
    {
        if (Settings.mode == ViewMode.Window)
        {
            if (SecondMapWindow.Instance != null)
            {
                SecondMapWindow.Instance.Close();
            }
            else if (Find.CurrentMap != null)
            {
                SecondMapWindow.Open(Find.CurrentMap);
            }
        }
        else if (SplitView.Active)
        {
            SplitView.Stop();
        }
        else
        {
            SplitView.Start(Find.CurrentMap);
        }
    }

    // the keys, after the windows so typing in a text box doesn't use them. Q and E also turn buildings, so not
    // while placing something
    public static void HandleKeys()
    {
        if (Find.DesignatorManager.SelectedDesignator != null)
        {
            return;
        }
        if (KeyDefs.JSM_ToggleSplit.KeyDownEvent)
        {
            ToggleView();
            Event.current.Use();
        }
        else if (KeyDefs.JSM_ToggleLock.KeyDownEvent)
        {
            SetLock(!Settings.lockSides);
            Event.current.Use();
        }
        else if (KeyDefs.JSM_SwitchHalf.KeyDownEvent && SplitView.Active)
        {
            SplitView.SwitchToOther();
            Event.current.Use();
        }
    }

    public static void SetLock(bool locked)
    {
        Settings.lockSides = locked;
        Settings.Write();
        ButtonIcon.Refresh();
        Messages.Message((locked ? "JSM_Locked" : "JSM_Unlocked").Translate(), MessageTypeDefOf.SilentInput, historical: false);
    }
}

// left click splits the screen with the map you're on on both halves, or closes it (with the panel option it opens
// the panel), right click locks the maps to their half
public class MainButtonWorker_SecondMap : MainButtonWorker
{
    public override void Activate()
    {
        if (Event.current != null && Event.current.button == 1)
        {
            JustSplitMapMod.SetLock(!JustSplitMapMod.Settings.lockSides);
            return;
        }
        if (JustSplitMapMod.Settings.picking == MapPicking.Panel)
        {
            MapPicker.Toggle();
        }
        else
        {
            JustSplitMapMod.ToggleView();
        }
    }

    // the game's own bottom bar only clicks with the left button
    public override void DoButton(Rect rect)
    {
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(rect))
        {
            JustSplitMapMod.SetLock(!JustSplitMapMod.Settings.lockSides);
            Event.current.Use();
        }
        base.DoButton(rect);
    }
}

// the button's icon, the halves filled in when they're locked. JustDesktop keeps the icons it found, so its lists
// get the new one too
public static class ButtonIcon
{
    private static readonly AccessTools.FieldRef<MainButtonDef, Texture2D> icon = AccessTools.FieldRefAccess<MainButtonDef, Texture2D>("icon");

    public static void Refresh()
    {
        MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("JSM_Split");
        if (def == null)
        {
            return;
        }
        Texture2D tex = ContentFinder<Texture2D>.Get(JustSplitMapMod.Settings.lockSides ? "UI/JustSplitMap/SplitLocked" : "UI/JustSplitMap/Split");
        icon(def) = tex;
        System.Type style = AccessTools.TypeByName("JustDesktop.Style");
        foreach (string name in new[] { "dockIcons", "taskbarIcons" })
        {
            if (style != null && AccessTools.Field(style, name)?.GetValue(null) is System.Collections.IDictionary icons)
            {
                // drawn as a line icon, in the dock's colors
                icons[def] = (tex, true);
            }
        }
    }
}

[HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
public static class Map_MapUpdate_Patch
{
    public static void Postfix(Map __instance)
    {
        if (__instance != Find.CurrentMap)
        {
            return;
        }
        SplitView.Update(__instance);
        SecondMapWindow.Instance?.RenderIfDue();
    }
}

[DefOf]
public static class KeyDefs
{
    public static KeyBindingDef JSM_SwitchHalf;
    public static KeyBindingDef JSM_ToggleLock;
    public static KeyBindingDef JSM_ToggleSplit;

    static KeyDefs()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(KeyDefs));
    }
}

[HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput))]
public static class MapInterface_HandleLowPriorityInput_Patch
{
    public static void Prefix()
    {
        if (Find.CurrentMap != null && WorldRendererUtility.DrawingMap)
        {
            JustSplitMapMod.HandleKeys();
        }
    }
}
