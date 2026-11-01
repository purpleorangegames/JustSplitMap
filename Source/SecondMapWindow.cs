using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustSplitMap;

// a window that shows another map, view only. Drag to move around, scroll to zoom, click to make it the
// main map, and the map that was main goes into the window.
public class SecondMapWindow : Window
{
    private const float BarHeight = 26f;
    private const float DragThreshold = 4f;

    public static SecondMapWindow Instance;

    private Map map;
    // the camera driver's root position and size for the window's map, and the camera size it turned into
    private Vector3 center;
    private float size = 24f;
    private float ortho = 24f;
    private Map lastCurrent;
    private RenderTexture texture;
    private float lastRender = -1f;
    private bool mouseDown, dragging;
    private Vector2 downPos;

    // opens the window on that map, or changes the map of the one that's open
    public static void Open(Map map)
    {
        SecondMapWindow window = Instance;
        if (window == null)
        {
            window = new SecondMapWindow();
            Find.WindowStack.Add(window);
        }
        window.Show(map);
    }

    public SecondMapWindow()
    {
        layer = WindowLayer.GameUI;
        draggable = true;
        resizeable = true;
        doCloseX = true;
        preventCameraMotion = false;
        absorbInputAroundWindow = false;
        closeOnClickedOutside = false;
        closeOnAccept = false;
        closeOnCancel = false;
        focusWhenOpened = false;
        onlyOneOfTypeAllowed = true;
        soundAppear = null;
        soundClose = null;
    }

    public override Vector2 InitialSize => new Vector2(480f, 360f);

    protected override float Margin => 6f;

    protected override void SetInitialSizeAndPosition()
    {
        Rect saved = JustSplitMapMod.Settings.windowRect;
        if (saved.width > 0f && saved.height > 0f)
        {
            windowRect = saved;
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, UI.screenWidth - windowRect.width);
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, UI.screenHeight - windowRect.height);
        }
        else
        {
            Vector2 initialSize = InitialSize;
            windowRect = new Rect(10f, UI.screenHeight - initialSize.y - 80f, initialSize.x, initialSize.y);
        }
    }

    public override void PostOpen()
    {
        base.PostOpen();
        Instance = this;
    }

    public override void PostClose()
    {
        base.PostClose();
        if (Instance == this)
        {
            Instance = null;
        }
        JustSplitMapMod.Settings.windowRect = windowRect;
        JustSplitMapMod.Settings.Write();
        if (texture != null)
        {
            texture.Release();
            Object.Destroy(texture);
            texture = null;
        }
    }

    // from Map.MapUpdate of the current map, after it drew, so the texture is ready for this frame's GUI
    public void RenderIfDue()
    {
        if (!WorldRendererUtility.DrawingMap || texture == null)
        {
            return;
        }
        UpdateMap();
        if (map == null)
        {
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (now - lastRender < 1f / JustSplitMapMod.Settings.updatesPerSecond)
        {
            return;
        }
        lastRender = now;
        MapRender.Render(map, center, size, texture);
        ortho = MapRender.LastOrthoSize;
    }

    // keeps the window on a map that isn't the current one
    private void UpdateMap()
    {
        Map current = Find.CurrentMap;
        if (lastCurrent != null && current != lastCurrent && current == map && Find.Maps.Contains(lastCurrent))
        {
            // the player went to this window's map some other way, show the one they left
            Show(lastCurrent);
        }
        lastCurrent = current;
        if (map == null || !Find.Maps.Contains(map))
        {
            // its map went away, another one or the current map takes its place
            Map other = Find.Maps.FirstOrDefault(m => m != current) ?? current;
            if (other == null)
            {
                map = null;
            }
            else
            {
                Show(other);
            }
        }
    }

    // the map the window shows, it can be the current one too
    public Map Map => map;

    public void Show(Map m)
    {
        map = m;
        RememberedCameraPos remembered = m.rememberedCameraPos;
        if (remembered != null && remembered.rootSize > 0f)
        {
            center = remembered.rootPos;
            size = remembered.rootSize;
        }
        else
        {
            center = m.Center.ToVector3Shifted();
        }
        ClampView();
        lastRender = -1f;
    }

    public override void DoWindowContents(Rect inRect)
    {
        UpdateMap();
        Rect bar = new Rect(inRect.x, inRect.y, inRect.width - 30f, BarHeight);
        Rect view = new Rect(inRect.x, bar.yMax + 4f, inRect.width, inRect.height - BarHeight - 4f);

        if (map == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(view, "JSM_NoOtherMap".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }
        if (!MapRender.Available)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(view, "JSM_NoRenderer".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        Rect switchRect = new Rect(bar.xMax - 90f, bar.y, 90f, bar.height);
        Rect mapRect = new Rect(bar.x, bar.y, Mathf.Min(220f, switchRect.x - bar.x - 6f), bar.height);
        if (Widgets.ButtonText(mapRect, JustSplitMapMod.Label(map)))
        {
            List<FloatMenuOption> options = Find.Maps.Select(m => new FloatMenuOption(JustSplitMapMod.Label(m), () => Show(m))).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
        if (Widgets.ButtonText(switchRect, "JSM_Switch".Translate()))
        {
            SwitchTo(center);
        }
        TooltipHandler.TipRegion(switchRect, "JSM_SwitchTip".Translate());

        EnsureTexture(view);
        HandleInput(view);
        GUI.DrawTexture(view, texture);
        Widgets.DrawBox(view);
    }

    private void EnsureTexture(Rect view)
    {
        int width = Mathf.Max(16, Mathf.RoundToInt(view.width * Prefs.UIScale));
        int height = Mathf.Max(16, Mathf.RoundToInt(view.height * Prefs.UIScale));
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
        lastRender = -1f;
    }

    private void HandleInput(Rect view)
    {
        Event e = Event.current;
        float cellsPerPixel = ortho * 2f / view.height;
        if (Mouse.IsOver(view))
        {
            if (e.type == EventType.ScrollWheel)
            {
                FloatRange sizeRange = Find.CameraDriver.config.sizeRange;
                size = Mathf.Clamp(size * (1f + e.delta.y * 0.06f), sizeRange.min, sizeRange.max);
                lastRender = -1f;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 2))
            {
                mouseDown = true;
                dragging = false;
                downPos = e.mousePosition;
                e.Use();
            }
        }
        if (!mouseDown)
        {
            return;
        }
        if (e.type == EventType.MouseDrag)
        {
            if ((e.mousePosition - downPos).magnitude > DragThreshold)
            {
                dragging = true;
            }
            if (dragging)
            {
                center += new Vector3(-e.delta.x, 0f, e.delta.y) * cellsPerPixel;
                ClampView();
                lastRender = -1f;
            }
            e.Use();
        }
        else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
        {
            mouseDown = false;
            if (!dragging && e.button == 0 && Mouse.IsOver(view))
            {
                SwitchTo(MapPosAt(e.mousePosition, view));
            }
            e.Use();
        }
    }

    private Vector3 MapPosAt(Vector2 mouse, Rect view)
    {
        float cellsPerPixel = ortho * 2f / view.height;
        Vector2 offset = mouse - view.center;
        return new Vector3(center.x + offset.x * cellsPerPixel, 0f, center.z - offset.y * cellsPerPixel);
    }

    private void ClampView()
    {
        if (map == null)
        {
            return;
        }
        center.x = Mathf.Clamp(center.x, 2f, map.Size.x - 2f);
        center.z = Mathf.Clamp(center.z, 2f, map.Size.z - 2f);
        center.y = 0f;
    }

    // the window's map becomes the main one, looking at focus with the window's zoom, and the map that
    // was main goes into the window where its camera was
    private void SwitchTo(Vector3 focus)
    {
        Map current = Find.CurrentMap;
        Map target = map;
        if (current == null || target == null)
        {
            return;
        }
        // the camera driver keeps these up to date for the current map every frame
        Vector3 oldCenter = current.rememberedCameraPos.rootPos;
        float oldSize = current.rememberedCameraPos.rootSize;
        float newSize = size;
        // with the same map in the window only the views change places, so the selection stays
        if (target != current)
        {
            Current.Game.CurrentMap = target;
        }
        Find.CameraDriver.SetRootPosAndSize(new Vector3(focus.x, 0f, focus.z), newSize);
        MapRender.ApplyCamera();
        MapRender.RedrawCurrentMap();
        lastCurrent = target;
        map = current;
        center = oldCenter;
        size = oldSize;
        ClampView();
        lastRender = -1f;
    }
}
