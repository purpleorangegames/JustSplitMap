using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustSplitMap;

// in split screen, a column of squares at the outer edge of each half, in the middle, one for each map with
// colonists. Clicking one shows that map on that half, right clicking on the other half. A small square under each
// column is the lock
public static class MapSquares
{
    private const float Size = 40f;
    private const float LockSize = 26f;
    private const float Gap = 6f;
    private const float Edge = 8f;

    private static readonly List<Map> shown = new();

    private static Texture2D lockTex, unlockTex;
    private static Texture2D LockTex => lockTex ??= ContentFinder<Texture2D>.Get("UI/JustSplitMap/Lock");
    private static Texture2D UnlockTex => unlockTex ??= ContentFinder<Texture2D>.Get("UI/JustSplitMap/Unlock");

    private static bool On => SplitView.Active && JustSplitMapMod.Settings.picking == MapPicking.Squares && WorldRendererUtility.DrawingMap;

    // the maps with colonists, and the ones on the halves even without
    private static void Collect()
    {
        shown.Clear();
        foreach (Map map in Find.Maps)
        {
            if (map.mapPawns.FreeColonistsSpawnedCount > 0 || map.IsPlayerHome || map == SplitView.MapOn(0) || map == SplitView.MapOn(1))
            {
                shown.Add(map);
            }
        }
    }

    public static void OnGUI()
    {
        if (!On)
        {
            return;
        }
        Collect();
        for (int side = 0; side < 2; side++)
        {
            int column = side;
            // in a window of their own, so the clicks come to them before the alerts and the rest of the map's UI
            Rect area = SquareRect(column, 0);
            area.height = shown.Count * (Size + Gap) + LockSize;
            Find.WindowStack.ImmediateWindow(0x4A544D53 + column, area, WindowLayer.GameUI, () =>
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    DoSquare(Local(SquareRect(column, i), area), shown[i], column);
                }
                Rect last = Local(SquareRect(column, shown.Count), area);
                DoLock(new Rect(last.x + (Size - LockSize) / 2f, last.y, LockSize, LockSize));
            }, doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
        }
    }

    // the i-th square of a half's column, from the top; one past the last is where the lock goes
    private static Rect SquareRect(int side, int i)
    {
        float height = shown.Count * (Size + Gap) + LockSize;
        float top = Mathf.Round((UI.screenHeight - height) / 2f);
        float x = side == 0 ? Edge : UI.screenWidth - Edge - Size;
        return new Rect(x, top + i * (Size + Gap), Size, Size);
    }

    private static Rect Local(Rect rect, Rect window) => new Rect(rect.x - window.x, rect.y - window.y, rect.width, rect.height);

    private static void DoSquare(Rect rect, Map map, int side)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, Mouse.IsOver(rect) ? 0.85f : 0.65f));
        if (map.Parent?.ExpandingIcon is { } tex)
        {
            GUI.color = map.Parent.ExpandingIconColor;
            Widgets.DrawTextureFitted(rect.ContractedBy(7f), tex, 1f);
            GUI.color = Color.white;
        }
        // the map on this half: white when it's the half you play, gray when not
        if (SplitView.MapOn(side) == map)
        {
            if (SplitView.IsActiveSide(side))
            {
                Widgets.DrawBox(rect, 2);
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.DrawBox(rect);
                GUI.color = Color.white;
            }
        }
        TooltipHandler.TipRegion(rect, JustSplitMapMod.Label(map) + "\n\n" + "JSM_SquareTip".Translate());
        if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
        {
            if (Event.current.button == 0)
            {
                SplitView.SetMap(side, map);
            }
            else if (Event.current.button == 1)
            {
                SplitView.SetMap(1 - side, map);
            }
            Event.current.Use();
        }
    }

    private static void DoLock(Rect rect)
    {
        bool locked = JustSplitMapMod.Settings.lockSides;
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, Mouse.IsOver(rect) ? 0.85f : 0.65f));
        GUI.color = locked ? Color.white : Color.gray;
        GUI.DrawTexture(rect.ContractedBy(4f), locked ? LockTex : UnlockTex);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(rect, (locked ? "JSM_Locked" : "JSM_Unlocked").Translate() + "\n\n" + "JSM_LockDesc".Translate());
        if (Widgets.ButtonInvisible(rect))
        {
            JustSplitMapMod.SetLock(!locked);
        }
    }
}
