using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustSplitMap;

// the screen split in two, a map on each half. The current map is drawn by the game as always, with its camera
// moved so the map sits in the middle of its half. The other map is drawn into a texture that goes into the
// scene as a quad over the other half, so the game's post processing and ReShade see both halves the same.
// Clicking or scrolling the other half makes it the current map first, and the click or scroll goes on to it.
public static class SplitView
{
    public static bool Active { get; private set; }

    // the maps on the left (0) and right (1) halves, and the camera of the one that isn't current
    private static readonly Map[] maps = new Map[2];
    private static readonly Vector3[] views = new Vector3[2];
    private static readonly float[] sizes = new float[2];
    private static int mainSide;
    private static Map lastCurrent;
    private static Game game;
    private static RenderTexture texture;
    private static Material material;
    private static float lastRender = -1f;

    private static int OtherSide => 1 - mainSide;

    public const float MinRatio = 0.15f;
    public const float MaxRatio = 0.85f;
    private const float Grab = 5f;
    private static bool dragging;

    // where the line between the halves is, as a part of the screen's width
    public static float Ratio => Mathf.Clamp(JustSplitMapMod.Settings.splitRatio, MinRatio, MaxRatio);

    public static float DividerX => Mathf.Round(UI.screenWidth * Ratio);

    // a half's part of the screen's width, and where its middle is, also as a part of the width
    private static float WidthOf(int side) => side == 0 ? Ratio : 1f - Ratio;

    private static float MiddleOf(int side) => side == 0 ? Ratio / 2f : (1f + Ratio) / 2f;

    // the part of the texture's width the other half uses
    private static float TexturePart => WidthOf(OtherSide) / (1f - MinRatio);

    // how far the game's camera moves so the current map's view sits in the middle of its half
    public static float CameraOffset(Camera cam) => -(MiddleOf(mainSide) - 0.5f) * 2f * cam.orthographicSize * cam.aspect;

    // the current map's half, in cells: its middle and half its width
    public static void ActiveCells(Camera cam, out float middle, out float halfWidth)
    {
        middle = cam.transform.position.x - CameraOffset(cam);
        halfWidth = WidthOf(mainSide) * cam.orthographicSize * cam.aspect;
    }

    public static void Toggle()
    {
        if (Active)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    // the map on the other half, null when the split is closed
    public static Map OtherMap => Active ? maps[OtherSide] : null;

    // puts a map on the other half, opening the split if it's closed. It can be the current map too, then both
    // halves look at the same map
    public static void Show(Map map)
    {
        if (Active)
        {
            SetSide(OtherSide, map);
        }
        else
        {
            Start(map);
        }
    }

    public static void Start(Map other = null)
    {
        Map current = Find.CurrentMap;
        if (current == null)
        {
            return;
        }
        other ??= Find.Maps.FirstOrDefault(m => m != current) ?? current;
        if (!MapRender.Available)
        {
            Messages.Message("JSM_NoRenderer".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }
        // the map being opened goes on the left
        Active = true;
        // closing and opening the split again keeps what was seen, a new game doesn't
        if (seenGame != Current.Game)
        {
            seen.Clear();
            seenGame = Current.Game;
        }
        game = Current.Game;
        mainSide = 1;
        maps[1] = current;
        SetSide(0, other);
        lastCurrent = current;
        lastRender = -1f;
        MapRender.ApplyCamera();
    }

    public static void Stop()
    {
        Active = false;
        maps[0] = maps[1] = null;
        lastCurrent = null;
        game = null;
        if (texture != null)
        {
            texture.Release();
            Object.Destroy(texture);
            texture = null;
        }
        MapRender.ApplyCamera();
    }

    // the map's camera from when it was last current, or the first time it's on a half, on its colonists or its
    // middle, since the remembered spot is often the edge where a caravan came in
    private static void SetSide(int side, Map map)
    {
        maps[side] = map;
        (views[side], sizes[side]) = ViewFor(map);
        lastRender = -1f;
    }

    // maps already shown on a half in this game
    private static readonly System.Collections.Generic.HashSet<Map> seen = new();
    private static Game seenGame;

    private static (Vector3 pos, float size) ViewFor(Map map)
    {
        RememberedCameraPos remembered = map.rememberedCameraPos;
        bool first = seen.Add(map) && map != Find.CurrentMap;
        if (!first && remembered != null && remembered.rootSize > 0f)
        {
            return (remembered.rootPos, remembered.rootSize);
        }
        float size = Find.CameraDriver.ZoomRootSize;
        var colonists = map.mapPawns.FreeColonistsSpawned;
        if (colonists.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (Pawn pawn in colonists)
            {
                sum += pawn.DrawPos;
            }
            Vector3 middle = sum / colonists.Count;
            return (new Vector3(middle.x, 0f, middle.z), size);
        }
        return (map.Center.ToVector3Shifted(), size);
    }

    // for the test hooks: the other half looking at a spot
    public static void SetOtherView(Vector3 pos, float size)
    {
        views[OtherSide] = pos;
        sizes[OtherSide] = size;
        lastRender = -1f;
    }

    // for the test hooks: the middle of the other half, in UI coordinates
    public static Vector2 MouseOverOtherSideCenter => new Vector2(UI.screenWidth * MiddleOf(OtherSide), UI.screenHeight * 0.5f);

    // true when something drawn from xMin to xMax on the screen would cross into the other half
    public static bool CrossesOther(float xMin, float xMax)
    {
        if (!Active || MapRender.Rendering)
        {
            return false;
        }
        float middle = DividerX;
        return mainSide == 0 ? xMax > middle : xMin < middle;
    }

    public static bool MouseOverOther
    {
        get
        {
            float x = UI.MousePositionOnUIInverted.x;
            float middle = DividerX;
            return mainSide == 0 ? x >= middle : x < middle;
        }
    }

    // a click over the other half that should make it the one you play: not on a window, and not on the colonist
    // bar, which acts on the mouse coming up so the press gets through to here
    public static bool ClickSwitches
    {
        get
        {
            Vector2 mouse = UI.MousePositionOnUIInverted;
            return Active && MouseOverOther && Find.WindowStack.GetWindowAt(mouse) == null && !OverColonistBar(mouse);
        }
    }

    private static bool OverColonistBar(Vector2 pos)
    {
        ColonistBar bar = Find.ColonistBar;
        if (bar == null || !Find.PlaySettings.showColonistBar)
        {
            return false;
        }
        Vector2 size = bar.Size;
        foreach (Vector2 loc in bar.DrawLocs)
        {
            // the portrait, its name under it and a bit around for the group frame
            if (new Rect(loc.x, loc.y, size.x, size.y + 20f).ExpandedBy(6f).Contains(pos))
            {
                return true;
            }
        }
        return false;
    }

    // from Map.MapUpdate of the current map, after it drew
    public static void Update(Map current)
    {
        if (!Active)
        {
            return;
        }
        if (Current.Game != game)
        {
            Stop();
            return;
        }
        if (current != lastCurrent)
        {
            // went to another map some other way, like the colonist bar. It shows on the half you clicked last,
            // or with the lock on, on the half that already has that map
            if (JustSplitMapMod.Settings.lockSides && current == maps[OtherSide] && lastCurrent != null && Find.Maps.Contains(lastCurrent))
            {
                SetSide(mainSide, lastCurrent);
                mainSide = OtherSide;
            }
            maps[mainSide] = current;
            lastCurrent = current;
        }
        Map other = maps[OtherSide];
        if (other == null || !Find.Maps.Contains(other))
        {
            // its map went away, another one or the current map takes its place
            other = Find.Maps.FirstOrDefault(m => m != current) ?? current;
            SetSide(OtherSide, other);
        }
        if (!WorldRendererUtility.DrawingMap)
        {
            return;
        }
        EnsureTexture();
        float now = Time.realtimeSinceStartup;
        if (now - lastRender >= 1f / JustSplitMapMod.Settings.updatesPerSecond)
        {
            lastRender = now;
            MapRender.Render(other, views[OtherSide], sizes[OtherSide], texture, TexturePart);
        }
        DrawQuad();
    }

    private static void EnsureTexture()
    {
        // as wide as the other half can get, it's drawn into its left part, so moving the line doesn't make a new one
        int width = Mathf.Max(16, Mathf.RoundToInt(UI.screenWidth * Prefs.UIScale * (1f - MinRatio)));
        int height = Mathf.Max(16, Mathf.RoundToInt(UI.screenHeight * Prefs.UIScale));
        if (texture != null && texture.width == width && texture.height == height)
        {
            return;
        }
        if (texture != null)
        {
            texture.Release();
            Object.Destroy(texture);
        }
        texture = new RenderTexture(width, height, 24);
        // drawn last, after the transparent things like the black past the map's edges. Shadows and other see-through
        // things leave the texture's alpha low in places and the cutout shader drops pixels under half, so the alpha
        // is scaled up until every pixel stays
        material ??= new Material(ShaderDatabase.Cutout) { renderQueue = 4500, color = new Color(1f, 1f, 1f, 1000f) };
        material.mainTexture = texture;
        lastRender = -1f;
    }

    // over the other half, right in front of the camera, above everything the current map draws there, even
    // the black past the map's edges
    private static void DrawQuad()
    {
        Camera cam = Find.Camera;
        float screenCells = 2f * cam.orthographicSize * cam.aspect;
        Vector3 camPos = cam.transform.position;
        float y = camPos.y - cam.nearClipPlane - 0.5f;
        Vector3 center = new Vector3(camPos.x + (MiddleOf(OtherSide) - 0.5f) * screenCells, y, camPos.z);
        Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(WidthOf(OtherSide) * screenCells, 1f, cam.orthographicSize * 2f));
        material.mainTextureScale = new Vector2(TexturePart, 1f);
        Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
    }

    // the other half's map becomes current with exactly the view it had, and the current one keeps its view
    // on its half. With the same map on both, only the views change places, so the selection stays
    public static void SwitchToOther()
    {
        Map current = Find.CurrentMap;
        int side = OtherSide;
        Map target = maps[side];
        if (current == null || target == null)
        {
            return;
        }
        Vector3 pos = views[side];
        float size = sizes[side];
        SetSide(mainSide, current);
        mainSide = side;
        lastCurrent = target;
        if (target != current)
        {
            Current.Game.CurrentMap = target;
        }
        Find.CameraDriver.SetRootPosAndSize(pos, size);
        AfterCameraChange();
    }

    private static void AfterCameraChange()
    {
        // now, so this click lands on the cell under the mouse in the new map
        MapRender.ApplyCamera();
        // and this frame shows the new state instead of the old map through the new camera
        MapRender.RedrawCurrentMap();
        lastRender = -1f;
        if (WorldRendererUtility.DrawingMap && texture != null)
        {
            lastRender = Time.realtimeSinceStartup;
            MapRender.Render(maps[OtherSide], views[OtherSide], sizes[OtherSide], texture, TexturePart);
            DrawQuad();
        }
    }

    // the left (0) or right (1) half's map, null when the split is closed
    public static Map MapOn(int side) => Active ? maps[side] : null;

    public static bool IsActiveSide(int side) => Active && side == mainSide;

    // the half you play, 0 left and 1 right
    public static int ActiveSide => mainSide;

    // puts a map on a half. On the active half it becomes the current map, at the view it had last time; with the
    // split closed it opens with the current map on the other half
    public static void SetMap(int side, Map map)
    {
        Map current = Find.CurrentMap;
        if (current == null)
        {
            return;
        }
        if (!Active)
        {
            Start(map);
            if (Active && side != 0)
            {
                // Start puts the picked map on the left
                SwitchSides();
            }
            return;
        }
        if (side != mainSide)
        {
            SetSide(side, map);
            return;
        }
        if (map == current)
        {
            return;
        }
        // where to look, worked out while it isn't the current map yet
        (Vector3 pos, float size) = ViewFor(map);
        maps[mainSide] = map;
        lastCurrent = map;
        Current.Game.CurrentMap = map;
        Find.CameraDriver.SetRootPosAndSize(pos, size);
        AfterCameraChange();
    }

    // the halves trade places, each keeping its map and view
    private static void SwitchSides()
    {
        (maps[0], maps[1]) = (maps[1], maps[0]);
        (views[0], views[1]) = (views[1], views[0]);
        (sizes[0], sizes[1]) = (sizes[1], sizes[0]);
        mainSide = OtherSide;
        AfterCameraChange();
    }

    // the mouse on the line, not on a window or the colonist bar
    public static bool MouseOverDivider
    {
        get
        {
            Vector2 mouse = UI.MousePositionOnUIInverted;
            return Active && WorldRendererUtility.DrawingMap && Mathf.Abs(mouse.x - DividerX) <= Grab && Find.WindowStack.GetWindowAt(mouse) == null && !OverColonistBar(mouse);
        }
    }

    // from the start of the play UI, so the press on the line doesn't reach the map
    public static void HandleDragInput()
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && MouseOverDivider)
        {
            if (e.clickCount == 2)
            {
                // a double click puts it back in the middle
                dragging = false;
                JustSplitMapMod.Settings.splitRatio = 0.5f;
                JustSplitMapMod.Settings.Write();
                lastRender = -1f;
            }
            else
            {
                dragging = true;
            }
            e.Use();
        }
    }

    // at the start of the frame, so the camera, the view and the other half all use the same place
    public static void UpdateDrag()
    {
        if (!dragging)
        {
            return;
        }
        if (!Active || !Input.GetMouseButton(0))
        {
            dragging = false;
            lastRender = -1f;
            JustSplitMapMod.Settings.Write();
            return;
        }
        JustSplitMapMod.Settings.splitRatio = Mathf.Clamp(UI.MousePositionOnUIInverted.x / UI.screenWidth, MinRatio, MaxRatio);
        lastRender = -1f;
    }

    // a thin line between the halves, lighter on the side of the half you play
    public static void DrawDivider()
    {
        if (Active && Event.current.type == EventType.Repaint && WorldRendererUtility.DrawingMap)
        {
            float middle = DividerX;
            Widgets.DrawBoxSolid(new Rect(middle - 1f, 0f, 2f, UI.screenHeight), new Color(0f, 0f, 0f, 0.7f));
            Widgets.DrawBoxSolid(new Rect(mainSide == 0 ? middle - 3f : middle + 1f, 0f, 2f, UI.screenHeight), new Color(1f, 1f, 1f, 0.35f));
            if (dragging || MouseOverDivider)
            {
                Widgets.DrawBoxSolid(new Rect(middle - 2f, 0f, 4f, UI.screenHeight), new Color(1f, 1f, 1f, 0.6f));
            }
        }
    }
}

// the game's camera moves so its map sits in the middle of its half
[HarmonyPatch(typeof(CameraDriver), "ApplyPositionToGameObject")]
public static class CameraDriver_ApplyPosition_Patch
{
    public static void Postfix(CameraDriver __instance)
    {
        if (!SplitView.Active || MapRender.Rendering)
        {
            return;
        }
        Camera cam = __instance.MyCamera;
        if (cam == null)
        {
            return;
        }
        Vector3 pos = cam.transform.position;
        pos.x += SplitView.CameraOffset(cam);
        cam.transform.position = pos;
    }
}

// only the current map's half counts as its view, so its labels and meshes stay out of the other half
[HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect), MethodType.Getter)]
public static class CameraDriver_CurrentViewRect_Patch
{
    public static void Postfix(ref CellRect __result)
    {
        if (!SplitView.Active || MapRender.Rendering || Find.Camera == null)
        {
            return;
        }
        Camera cam = Find.Camera;
        SplitView.ActiveCells(cam, out float middle, out float halfWidth);
        __result.minX = Mathf.Max(__result.minX, Mathf.FloorToInt(middle - halfWidth - 1f));
        __result.maxX = Mathf.Min(__result.maxX, Mathf.CeilToInt(middle + halfWidth));
    }
}

// a click that reaches the map on the other half switches to it first
[HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
public static class MapInterface_HandleMapClicks_Patch
{
    public static void Prefix()
    {
        if (Event.current.type == EventType.MouseDown && SplitView.ClickSwitches)
        {
            SplitView.SwitchToOther();
        }
    }
}

// so does zooming or dragging the camera over it
[HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI))]
public static class CameraDriver_OnGUI_Patch
{
    public static void Prefix()
    {
        Event e = Event.current;
        if ((e.type == EventType.ScrollWheel || e.type == EventType.MouseDown && e.button == 2) && SplitView.ClickSwitches)
        {
            SplitView.SwitchToOther();
        }
    }
}

[HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
public static class MapInterface_OnGUI_Patch
{
    public static void Postfix()
    {
        SplitView.DrawDivider();
        MapSquares.OnGUI();
    }
}

// the current map's labels (stack counts, quality, names) are drawn over the map by the UI, they're left out where
// they'd go over the other half
[HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawThingLabel), typeof(Vector2), typeof(string), typeof(Color))]
public static class GenMapUI_DrawThingLabel_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Vector2 screenPos, string text)
    {
        if (!SplitView.Active)
        {
            return true;
        }
        GameFont font = Text.Font;
        Text.Font = GameFont.Tiny;
        float half = Text.CalcSize(text).x / 2f + 6f;
        Text.Font = font;
        return !SplitView.CrossesOther(screenPos.x - half, screenPos.x + half);
    }
}

[HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel), typeof(Pawn), typeof(Rect), typeof(float), typeof(float), typeof(System.Collections.Generic.Dictionary<string, string>), typeof(GameFont), typeof(bool), typeof(bool))]
public static class GenMapUI_DrawPawnLabel_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Rect bgRect)
    {
        return !SplitView.CrossesOther(bgRect.xMin, bgRect.xMax);
    }
}

[HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawText))]
public static class GenMapUI_DrawText_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Vector2 worldPos)
    {
        if (!SplitView.Active)
        {
            return true;
        }
        float x = new Vector3(worldPos.x, 0f, worldPos.y).MapToUIPosition().x;
        return !SplitView.CrossesOther(x - 20f, x + 20f);
    }
}

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class UIRoot_Play_UIRootOnGUI_Patch
{
    public static void Prefix()
    {
        SplitView.HandleDragInput();
    }
}

[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
public static class Root_Play_Update_Drag_Patch
{
    public static void Prefix()
    {
        SplitView.UpdateDrag();
    }
}
