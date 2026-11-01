using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustSplitMap;

// the panel the "second map" button opens above the mouse. In split screen it has a column for each half with a
// button for every map, the current one too, so the map is picked for that side; in window mode one column for
// the window. And a button to close when it's open
public class MapPicker : Window
{
    private const float RowHeight = 32f;
    private const float ColumnWidth = 240f;
    private const float Gap = 8f;

    private readonly Vector2 anchor;

    public static void Toggle()
    {
        MapPicker open = Find.WindowStack.WindowOfType<MapPicker>();
        if (open != null)
        {
            open.Close();
        }
        else
        {
            Find.WindowStack.Add(new MapPicker(UI.MousePositionOnUIInverted));
        }
    }

    public MapPicker(Vector2 anchor)
    {
        this.anchor = anchor;
        layer = WindowLayer.GameUI;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        doCloseX = false;
        onlyOneOfTypeAllowed = true;
        soundAppear = null;
    }

    protected override float Margin => 6f;

    private static bool Split => JustSplitMapMod.Settings.mode == ViewMode.Split;

    private static bool ViewOpen => Split ? SplitView.Active : SecondMapWindow.Instance != null;

    private int Columns => Split ? 2 : 1;

    // maps, the column names and the lock in split screen, and close
    private int Rows => Find.Maps.Count + (Split ? 2 : 0) + (ViewOpen ? 1 : 0);

    public override Vector2 InitialSize => new Vector2(Columns * ColumnWidth + (Columns - 1) * Gap + Margin * 2f, Rows * RowHeight + Margin * 2f);

    protected override void SetInitialSizeAndPosition()
    {
        Vector2 size = InitialSize;
        windowRect = new Rect(anchor.x - size.x / 2f, anchor.y - size.y - 12f, size.x, size.y);
        if (windowRect.y < 0f)
        {
            windowRect.y = anchor.y + 12f;
        }
        windowRect.x = Mathf.Clamp(windowRect.x, 0f, UI.screenWidth - size.x);
        windowRect.y = Mathf.Clamp(windowRect.y, 0f, UI.screenHeight - size.y);
        windowRect = windowRect.Rounded();
    }

    public override void DoWindowContents(Rect inRect)
    {
        // maps can come and go while it's open, and the mode can change
        if (!Mathf.Approximately(windowRect.height, InitialSize.y) || !Mathf.Approximately(windowRect.width, InitialSize.x))
        {
            SetInitialSizeAndPosition();
        }
        List<Map> mapList = Find.Maps.ToList();
        float y = inRect.y;
        if (Split)
        {
            for (int side = 0; side < 2; side++)
            {
                Rect column = new Rect(inRect.x + side * (ColumnWidth + Gap), inRect.y, ColumnWidth, inRect.height);
                Rect header = new Rect(column.x, column.y, column.width, RowHeight - 2f);
                string label = (side == 0 ? "JSM_Left" : "JSM_Right").Translate();
                if (SplitView.IsActiveSide(side))
                {
                    label = "JSM_ActiveSide".Translate(label);
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = SplitView.IsActiveSide(side) ? Color.white : Color.gray;
                Widgets.Label(header, label);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                float rowY = header.yMax + 2f;
                foreach (Map map in mapList)
                {
                    int picked = side;
                    if (MapRow(new Rect(column.x, rowY, column.width, RowHeight - 2f), map, SplitView.MapOn(side) == map))
                    {
                        SplitView.SetMap(picked, map);
                        Close();
                    }
                    rowY += RowHeight;
                }
                y = rowY;
            }
        }
        else
        {
            foreach (Map map in mapList)
            {
                if (MapRow(new Rect(inRect.x, y, inRect.width, RowHeight - 2f), map, SecondMapWindow.Instance?.Map == map))
                {
                    SecondMapWindow.Open(map);
                    Close();
                }
                y += RowHeight;
            }
        }
        if (Split)
        {
            Rect lockRow = new Rect(inRect.x, y, inRect.width, RowHeight - 2f);
            bool locked = JustSplitMapMod.Settings.lockSides;
            Widgets.CheckboxLabeled(lockRow.ContractedBy(4f, 0f), "JSM_Lock".Translate(), ref locked);
            TooltipHandler.TipRegion(lockRow, "JSM_LockDesc".Translate());
            if (locked != JustSplitMapMod.Settings.lockSides)
            {
                JustSplitMapMod.SetLock(locked);
            }
            y += RowHeight;
        }
        if (ViewOpen && Widgets.ButtonText(new Rect(inRect.x, y, inRect.width, RowHeight - 2f), "JSM_Close".Translate()))
        {
            JustSplitMapMod.Close();
            Close();
        }
    }

    // a map's icon and name, true when clicked
    private static bool MapRow(Rect row, Map map, bool shown)
    {
        Widgets.DrawOptionBackground(row, shown);
        Rect icon = new Rect(row.x + 4f, row.y + (row.height - 24f) / 2f, 24f, 24f);
        if (map.Parent?.ExpandingIcon is { } tex)
        {
            GUI.color = map.Parent.ExpandingIconColor;
            Widgets.DrawTextureFitted(icon, tex, 1f);
            GUI.color = Color.white;
        }
        string label = JustSplitMapMod.Label(map);
        if (map == Find.CurrentMap)
        {
            label = "JSM_ThisMap".Translate(label);
        }
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(icon.xMax + 8f, row.y, row.xMax - icon.xMax - 12f, row.height), label.Truncate(row.width - 44f));
        Text.Anchor = TextAnchor.UpperLeft;
        return Widgets.ButtonInvisible(row);
    }
}
