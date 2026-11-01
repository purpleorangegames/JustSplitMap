using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using UnityStubs.Rendering;
using Verse;
using RimWorldWaylith.Rendering;
using Waylith.Rendering;
using Camera = UnityEngine.Camera;
using RenderTexture = UnityEngine.RenderTexture;
using Shader = UnityEngine.Shader;
using Material = UnityEngine.Material;

namespace JustSplitMap;

// draws a map that isn't the current one into a texture. The game only draws the current map, so for a
// moment the other map is made current (only the index, so the map UI and sounds don't notice) and the
// game's own camera is put on the other map's view, so camera mods like Camera+ place and zoom it the same
// way they do the main one. The draw calls are captured instead of going to the screen, and the engine's
// texture renderer, the one portraits use, draws them with a copy of that camera.
public static class MapRender
{
    private static readonly AccessTools.FieldRef<Game, sbyte> currentMapIndex = AccessTools.FieldRefAccess<Game, sbyte>("currentMapIndex");
    private static readonly AccessTools.FieldRef<CameraDriver, Vector3> rootPos = AccessTools.FieldRefAccess<CameraDriver, Vector3>("rootPos");
    private static readonly AccessTools.FieldRef<CameraDriver, float> rootSize = AccessTools.FieldRefAccess<CameraDriver, float>("rootSize");
    private static readonly Action<CameraDriver> applyPosition = AccessTools.MethodDelegate<Action<CameraDriver>>(AccessTools.Method(typeof(CameraDriver), "ApplyPositionToGameObject"));
    private static readonly List<MeshDrawCommand> commands = new();
    private static Camera camera;

    // what the main pass also draws: the meshes the engine keeps between frames (terrain edges, roofs, lighting...),
    // which go to a list of their own that capturing doesn't catch
    private static readonly AccessTools.FieldRef<List<PersistentDrawCallSource>[]> persistentSources = AccessTools.StaticFieldRefAccess<List<PersistentDrawCallSource>[]>(AccessTools.Field(typeof(MeshDrawSubmission), "submittedPersistentSources"));
    private static readonly AccessTools.FieldRef<int> currentArenaIndex = AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(MeshDrawSubmission), "currentArenaIndex"));
    private static readonly List<PersistentDrawCallSource> persistent = new();

    // the game's order for transparent things, by depth, internal so through reflection
    private static readonly Type depthOrderType = AccessTools.TypeByName("RimWorldWaylith.Rendering.TransparentDepthOrder");
    private static readonly object depthOrder = Activator.CreateInstance(depthOrderType, nonPublic: true);
    private static readonly System.Reflection.MethodInfo depthCompute = AccessTools.Method(depthOrderType, "Compute");
    private static readonly AccessTools.FieldRef<object, ushort[]> depthRanks = AccessTools.FieldRefAccess<ushort[]>(depthOrderType, "ranks");
    private static MeshDrawCommand[] commandArray = new MeshDrawCommand[256];
    private static readonly List<FinalDrawItem> items = new();
    private static readonly List<FinalDrawItem> rented = new();

    // plant materials are shared by every map and the game sets their sway only from the current map's wind
    private static readonly AccessTools.FieldRef<WindManager, float> plantSwayHead = AccessTools.FieldRefAccess<WindManager, float>("plantSwayHead");
    private static readonly AccessTools.FieldRef<List<Material>> plantMaterials = AccessTools.StaticFieldRefAccess<List<Material>>(AccessTools.Field(typeof(WindManager), "plantMaterials"));

    private static void SetPlantSway(Map map)
    {
        float head = plantSwayHead(map.windManager);
        foreach (Material material in plantMaterials())
        {
            material.SetFloat(ShaderPropertyIDs.SwayHead, head);
        }
    }

    public static bool Available => CameraTextureRenderer.Current != null;

    // true while the game's camera is on the other map, so the split offset stays off
    public static bool Rendering { get; private set; }

    // the camera size the last render used, what the root size turned into
    public static float LastOrthoSize { get; private set; } = 24f;

    // puts the game's camera where the driver says, with every mod's changes to it
    public static void ApplyCamera()
    {
        if (Find.CameraDriver != null)
        {
            applyPosition(Find.CameraDriver);
        }
    }

    private static Camera Camera
    {
        get
        {
            if (camera == null)
            {
                GameObject go = new GameObject("JustSplitMapCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
            }
            return camera;
        }
    }

    // pos and size are the camera driver's root position and size for that map, like its remembered camera
    // width is the part of the texture's width to draw into, from its left edge, so a texture made once at the
    // biggest size can show a smaller area without making a new one
    public static void Render(Map map, Vector3 pos, float size, RenderTexture target, float width = 1f)
    {
        CameraTextureBaker renderer = CameraTextureRenderer.Current as CameraTextureBaker;
        Game game = Current.Game;
        int index = game.Maps.IndexOf(map);
        CameraDriver driver = Find.CameraDriver;
        Camera main = Find.Camera;
        if (renderer == null || index < 0 || target == null || driver == null || main == null)
        {
            return;
        }
        width = Mathf.Clamp01(width);
        float aspect = target.width * width / (float)Math.Max(1, target.height);

        // CameraDriver.CurrentViewRect is cached once per frame, everything that culls by the view reads it
        ref int viewFrame = ref AccessTools.StaticFieldRefAccess<int>(typeof(CameraDriver), "lastViewRectGetFrame");
        ref CellRect viewRect = ref AccessTools.StaticFieldRefAccess<CellRect>(typeof(CameraDriver), "lastViewRect");
        int oldFrame = viewFrame;
        CellRect oldRect = viewRect;
        sbyte oldIndex = currentMapIndex(game);
        Vector3 oldPos = rootPos(driver);
        float oldSize = rootSize(driver);

        Rendering = true;
        commands.Clear();
        try
        {
            rootPos(driver) = new Vector3(pos.x, oldPos.y, pos.z);
            rootSize(driver) = size;
            applyPosition(driver);
            Camera cam = Camera;
            cam.transform.position = main.transform.position;
            cam.transform.rotation = main.transform.rotation;
            cam.orthographicSize = main.orthographicSize;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.aspect = aspect;
            LastOrthoSize = cam.orthographicSize;

            Vector3 center = cam.transform.position;
            float halfHeight = cam.orthographicSize;
            currentMapIndex(game) = (sbyte)index;
            viewFrame = Time.frameCount;
            viewRect = new CellRect
            {
                minX = Mathf.FloorToInt(center.x - halfHeight * aspect - 1f),
                maxX = Mathf.CeilToInt(center.x + halfHeight * aspect),
                minZ = Mathf.FloorToInt(center.z - halfHeight - 1f),
                maxZ = Mathf.CeilToInt(center.z + halfHeight)
            };
            List<PersistentDrawCallSource> mainSources = persistentSources()[currentArenaIndex()];
            int sourcesBefore = mainSources.Count;
            using (MeshDrawSubmission.BeginCapture(commands))
            {
                map.skyManager.SkyManagerUpdate();
                SetPlantSway(map);
                Draw(map);
            }
            persistent.Clear();
            persistent.AddRange(mainSources.GetRange(sourcesBefore, mainSources.Count - sourcesBefore));
            mainSources.RemoveRange(sourcesBefore, mainSources.Count - sourcesBefore);
            // while the other map's shader globals are still set
            RenderItems(renderer, target, cam, map, width);
        }
        catch (Exception e)
        {
            Log.ErrorOnce("[JustSplitMap] drawing the second map failed: " + e, 0x4A544D31);
        }
        finally
        {
            currentMapIndex(game) = oldIndex;
            viewFrame = oldFrame;
            viewRect = oldRect;
            rootPos(driver) = oldPos;
            rootSize(driver) = oldSize;
            Rendering = false;
            applyPosition(driver);
            commands.Clear();
            persistent.Clear();
            Map current = Find.CurrentMap;
            if (current != null)
            {
                current.skyManager.SkyManagerUpdate();
                SetPlantSway(current);
                SetShaderGlobals(current);
            }
        }
    }

    // like the game's main pass and its capture feature: the captured draws and the kept ones in one list, in the
    // game's order, drawn with the map's sun
    private static void RenderItems(CameraTextureBaker renderer, RenderTexture target, Camera cam, Map map, float width)
    {
        DrawItemFactory maker = MeshDrawSubmission.DrawItemMaker;
        if (maker == null)
        {
            return;
        }
        int count = commands.Count;
        if (commandArray.Length < count)
        {
            commandArray = new MeshDrawCommand[Math.Max(count, commandArray.Length * 2)];
        }
        commands.CopyTo(commandArray);
        System.Numerics.Matrix4x4 view = CameraViewFactory.GetView(cam).View;
        depthCompute.Invoke(depthOrder, new object[] { commandArray, count, view });
        ushort[] ranks = depthRanks(depthOrder);
        string keywords = Shader.GlobalKeywordKey;
        DrawPropertyArena arena = MeshDrawSubmission.ActiveArena;
        items.Clear();
        rented.Clear();
        try
        {
            for (int i = 0; i < count; i++)
            {
                FinalDrawItem item = maker.TryMake(in commandArray[i], arena, keywords, i, null, ranks[i]);
                if (item == null)
                {
                    continue;
                }
                rented.Add(item);
                if (item.PassId != RenderPassIds.WaterDepth)
                {
                    items.Add(item);
                }
            }
            foreach (PersistentDrawCallSource source in persistent)
            {
                IReadOnlyList<FinalDrawItem> kept = source.DrawItemsReadOnly;
                for (int i = 0; i < kept.Count; i++)
                {
                    if (kept[i].PassId != RenderPassIds.WaterDepth)
                    {
                        items.Add(kept[i]);
                    }
                }
            }
            items.Sort(DrawSortKeyComparer.Instance);
            System.Numerics.Vector3? sun = null;
            if (Shader.TryGetGlobalVector(ShaderPropertyIDs.MapSunLightDirection, out Vector4 v))
            {
                sun = new System.Numerics.Vector3(v.x, v.y, v.z);
            }
            Color background = map.skyManager.CurSky.colors.sky;
            background.a = 1f;
            renderer.RenderItems(target, items, cam, new Rect(0f, 0f, width, 1f), background, sun);
        }
        finally
        {
            foreach (FinalDrawItem item in rented)
            {
                FinalDrawItem.Return(item);
            }
            items.Clear();
            rented.Clear();
        }
    }

    private static readonly System.Collections.IList submittedCommands = (System.Collections.IList)AccessTools.Field(typeof(MeshDrawSubmission), "Commands").GetValue(null);

    // after switching maps in the middle of a frame: what the frame drew is the old map, which would show for a
    // frame through the new camera (and linger with effects that keep past frames), so it's thrown away and the
    // new current map drawn again
    public static void RedrawCurrentMap()
    {
        Map map = Find.CurrentMap;
        if (map == null || !WorldRendererUtility.DrawingMap)
        {
            return;
        }
        submittedCommands.Clear();
        MeshDrawSubmission.ActiveArena.Clear();
        persistentSources()[currentArenaIndex()].Clear();
        AccessTools.StaticFieldRefAccess<int>(typeof(CameraDriver), "lastViewRectGetFrame") = -1;
        map.skyManager.SkyManagerUpdate();
        SetPlantSway(map);
        Draw(map);
    }

    // the drawing part of Map.MapUpdate, without the debug drawers
    private static void Draw(Map map)
    {
        SetShaderGlobals(map);
        map.mapDrawer.MapMeshDrawerUpdate_First();
        map.mapDrawer.DrawMapMesh();
        map.dynamicDrawManager.DrawDynamicThings();
        map.gameConditionManager.GameConditionManagerDraw(map);
        MapEdgeClipDrawer.DrawClippers(map);
        map.designationManager.DrawDesignations();
        map.overlayDrawer.DrawAllOverlays();
        map.temporaryThingDrawer.Draw();
        map.flecks.FleckManagerDraw();
    }

    private static void SetShaderGlobals(Map map)
    {
        GlobalRendererUtility.UpdateGlobalShadersParams();
        PlantFallColors.SetFallShaderGlobals(map);
        map.waterInfo.SetTextures();
    }
}
