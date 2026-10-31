using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using Mirror;
using UnityEngine;
using Wish;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace SunHavenCreate
{
    [DefaultExecutionOrder(-1000)]
    public sealed partial class FactoryRuntime : MonoBehaviour
    {
        internal static FactoryRuntime Instance;
        BepInEx.Logging.ManualLogSource Logger => Plugin.Log;

        readonly Factory factory = new Factory();
        readonly Dictionary<Machine, Transform> visuals = new Dictionary<Machine, Transform>();
        readonly Dictionary<MachineKind, Sprite> sprites = new Dictionary<MachineKind, Sprite>();
        readonly string[] names = { "Водяное колесо", "Вал", "Малая шестерня", "Большая шестерня", "Конвейер", "Мельница", "Выходной сундук" };
        internal GameObject root;
        MachineKind selected;
        int direction;
        bool panel, faulted;
        string scene;
        object save;
        string message = "";
        Rect panelRect = new Rect(20, 55, 360, 620);
        double accumulator;
        bool previousBackground;
        DevBridge bridge;
        Texture2D panelBackground;
        const string InputBlock = "SunHavenCreate.palette";

        void Awake()
        {
            Instance = this;
            previousBackground = Application.runInBackground;
            if (Plugin.EnableBridge) Application.runInBackground = true;
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(.10f, .08f, .05f, .98f)); panelBackground.Apply();
            DontDestroyOnLoad(gameObject);
            root = new GameObject("SunHavenCreate_Sandbox");
            DontDestroyOnLoad(root);
            Logger.LogInfo("Create 0.2.0 runtime loaded. WorldIntegration=" + Plugin.Live);
            if (Plugin.EnableBridge)
                bridge = new DevBridge(this);
            Logger.LogInfo("Awake active=" + gameObject.activeInHierarchy + " enabled=" + enabled + " bridge=" + (bridge != null));
        }
        void Start() { Logger.LogInfo("Prototype Update host started"); }
        bool HasWorld()
        {
            var gs = SingletonBehaviour<GameSave>.Instance;
            // Local player, loaded world, no remote client and no other players.
            return !faulted && Player.Instance && gs && gs.LoadedGame && gs.CurrentSave != null &&
                !(NetworkClient.active && !NetworkServer.active) && NetworkServer.connections.Count <= 1 &&
                !string.IsNullOrEmpty(ScenePortalManager.ActiveSceneName) && !ScenePortalManager.LoadingScenes;
        }
        bool Ready() => HasWorld() && ReferenceEquals(save, SingletonBehaviour<GameSave>.Instance.CurrentSave) && scene == ScenePortalManager.ActiveSceneName;
        void Update()
        {
            try { UpdateSandbox(); }
            catch (Exception e)
            {
                faulted = true; panel = false; root.SetActive(false);
                PlayerInput.EnableInput(InputBlock);
                Logger.LogError("Sandbox disabled after exception: " + e);
            }
        }
        void UpdateSandbox()
        {
            if (bridge != null) { Application.runInBackground = true; bridge.Poll(); }
            if (!HasWorld()) { root.SetActive(false); panel = false; PlayerInput.EnableInput(InputBlock); accumulator = 0; return; }
            var player = Player.Instance;
            var currentSave = SingletonBehaviour<GameSave>.Instance.CurrentSave;
            string currentScene = ScenePortalManager.ActiveSceneName;
            if (scene != currentScene || !ReferenceEquals(save, currentSave))
            { ChangeWorld(currentSave, currentScene); }
            root.SetActive(true);
            if (Application.isFocused && Input.GetKeyDown(KeyCode.F8)) panel = !panel;
            if (panel) PlayerInput.DisableInput(InputBlock); else PlayerInput.EnableInput(InputBlock);
            if (player.pause || Cutscene.Active || Cutscene.WithinMultipartCutscene || UIHandler.InventoryOpen ||
                PlayerInput.disableInputObjects.Any(s => s != InputBlock)) { accumulator = 0; return; }
            if (panel && Application.isFocused)
            {
                if (Input.GetKeyDown(KeyCode.R)) direction = (direction + 1) % 4;
                if (Input.GetKeyDown(KeyCode.P) && CursorCell(out int x, out int y))
                {
                    if (Plugin.Live) PlaceLive(x, y, selected, direction);
                    else if (factory.Place(new Machine(x, y, selected, direction))) Sync();
                    else message = "Клетка занята или достигнут предел в 128 механизмов.";
                }
                if (Input.GetKeyDown(KeyCode.Delete) && CursorCell(out int rx, out int ry))
                { if (Plugin.Live) Dismantle(rx, ry); else { factory.Remove(rx, ry); Sync(); } }
                if (Plugin.Live && Input.GetKeyDown(KeyCode.T) && CursorCell(out int tx, out int ty)) DepositWheat(factory.At(tx, ty));
                if (Plugin.Live && Input.GetKeyDown(KeyCode.G) && CursorCell(out int gx, out int gy)) Collect(factory.At(gx, gy));
            }
            accumulator += Math.Min(Time.deltaTime, .2f);
            while (accumulator >= .1) { factory.Tick(.1); accumulator -= .1; if (Plugin.Live) StoreState(); }
            foreach (var pair in visuals)
            {
                var m = pair.Key;
                if (m.Kind == MachineKind.Cog || m.Kind == MachineKind.LargeCog)
                    pair.Value.Rotate(0, 0, (float)(m.Rpm * 6 * Time.deltaTime));
                pair.Value.GetComponent<SpriteRenderer>().color = m.Conflict ? Color.magenta : m.Overloaded ? Color.red : Color.white;
            }
        }
        bool CursorCell(out int x, out int y)
        {
            x = y = 0;
            if (panelRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))) return false;
            var point = Utilities.MousePositionFloat(12f);
            // Sandbox is limited to a nearby area; these are visual objects, no collision or native tiles.
            if (Vector2.Distance(point, Player.Instance.ExactPosition) > 12) return false;
            x = Mathf.FloorToInt(point.x); y = Mathf.FloorToInt(point.y); return true;
        }
        void Clear()
        { factory.Machines.Clear(); accumulator = 0; Sync(); }
        internal Factory DebugFactory => factory;
        internal void DebugShowPanel() { RequireLab(); panel = true; }
        internal void DebugDemo()
        {
            if (!Ready()) throw new InvalidOperationException("No local world loaded");
            if (Plugin.Live) throw new InvalidOperationException("Sandbox demo unavailable in live mode");
            var p = Player.Instance.ExactPosition;
            factory.Demo(Mathf.FloorToInt(p.x) - 2, Mathf.FloorToInt(p.y) + 3); Sync(); panel = true;
        }
        void Sync()
        {
            foreach (var t in visuals.Values) if (t) { t.parent.gameObject.SetActive(false); Destroy(t.parent.gameObject); }
            visuals.Clear();
            var renderSource = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>()
                .FirstOrDefault(r => r.isVisible && r.sprite && !r.GetComponentInParent<Canvas>() && r.transform.root != root.transform &&
                    r.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0);
            foreach (var m in factory.Machines)
            {
                var obj = new GameObject("CreateSandbox_" + m.Kind);
                obj.transform.SetParent(root.transform);
                obj.transform.position = MachinePosition(m);
                var graphics = new GameObject("Graphics"); graphics.transform.SetParent(obj.transform, false);
                var sr = graphics.AddComponent<SpriteRenderer>(); sr.sprite = Icon(m.Kind); sr.sortingOrder = 30000;
                var layers = SortingLayer.layers;
                if (layers.Length > 0) sr.sortingLayerID = layers[layers.Length - 1].id;
                if (renderSource)
                {
                    graphics.layer = renderSource.gameObject.layer;
                    sr.sharedMaterial = renderSource.sharedMaterial;
                    sr.sortingLayerID = renderSource.sortingLayerID;
                    graphics.transform.rotation = renderSource.transform.rotation;
                }
                if (Plugin.Live && (m.Kind == MachineKind.WaterWheel || m.Kind == MachineKind.Mill || m.Kind == MachineKind.Chest))
                    obj.AddComponent<BoxCollider2D>().size = new Vector2(.7f, .6f);
                visuals.Add(m, graphics.transform);
            }
        }
        Sprite Icon(MachineKind kind)
        {
            if (sprites.TryGetValue(kind, out var existing)) return existing;
            var imagePath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "assets", kind + ".png");
            if (File.Exists(imagePath))
            {
                var art = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (!ImageConversion.LoadImage(art, File.ReadAllBytes(imagePath)))
                    throw new InvalidDataException("Invalid machine sprite: " + kind);
                art.filterMode = FilterMode.Point;
                // Same one-tile width as the tested temporary art. Taller props extend upwards.
                var pivot = kind == MachineKind.Cog || kind == MachineKind.LargeCog ? new Vector2(.5f, .5f) : new Vector2(.5f, .18f);
                var loaded = Sprite.Create(art, new Rect(0, 0, art.width, art.height), pivot, 48);
                sprites.Add(kind, loaded); return loaded;
            }
            var tex = new Texture2D(24, 24, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color[24 * 24];
            Color brass = new Color(.8f, .57f, .24f), iron = new Color(.32f, .39f, .44f);
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
            {
                float dx = x - 11.5f, dy = y - 11.5f, radius = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = Color.clear;
                if (kind == MachineKind.WaterWheel || kind == MachineKind.Cog || kind == MachineKind.LargeCog)
                {
                    if (radius < 10 && (radius > 6 || Math.Abs(dx) < 2 || Math.Abs(dy) < 2)) c = brass;
                    if (radius < 2.5f) c = iron;
                }
                else if (kind == MachineKind.Shaft) { if (Math.Abs(dy) < 3 && Math.Abs(dx) < 10) c = iron; }
                else if (Math.Abs(dx) < 10 && Math.Abs(dy) < 8)
                {
                    c = kind == MachineKind.Chest ? brass : iron;
                    if (kind == MachineKind.Belt && x % 5 == 0) c = brass;
                    if (kind == MachineKind.Mill && radius < 5) c = brass;
                    if (kind == MachineKind.Chest && Math.Abs(dy) < 1) c = iron;
                }
                pixels[y * 24 + x] = c;
            }
            tex.SetPixels(pixels); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 24, 24), new Vector2(.5f, .5f), 24);
            sprites.Add(kind, sprite); return sprite;
        }
        void OnGUI()
        {
            if (!Ready()) return;
            GUI.Label(new Rect(20, 20, 600, 30), Plugin.Live ? "Sun Haven Create [F8]" : "Create — тестовый стенд [F8]");
            if (panel) panelRect = GUI.Window(82641, panelRect, DrawPanel, "Солнечная механика · 0.3");
            var cam = Player.Instance.Camera;
            if (!cam) return;
            foreach (var m in factory.Machines)
            {
                var p = cam.WorldToScreenPoint(visuals[m].position);
                if (p.z < 0) continue;
                string status = m.Conflict ? "Конфликт" : m.Overloaded ? "Перегрузка" : m.Kind == MachineKind.WaterWheel ? Math.Round(m.Rpm) + " rpm" : "";
                string count = m.Count > 0 ? "\nП:" + m.Wheat + " М:" + m.Flour : "";
                if (status.Length + count.Length > 0) GUI.Label(new Rect(p.x - 20, Screen.height - p.y + 15, 110, 48), status + count);
            }
        }
        void DrawPanel(int id)
        {
            GUI.DrawTexture(new Rect(2, 20, panelRect.width - 4, panelRect.height - 22), panelBackground);
            GUILayout.Label(Plugin.Live ? "Строительство за брёвна.\nМельница: пшеница → мука." : "Временный стенд без сохранения.");
            if (GUILayout.Button("Прочесть записку мастера")) ReadWorkshopNote();
            for (int i = 0; i < names.Length; i++)
                if (GUILayout.Toggle((int)selected == i, names[i] + (Plugin.Live ? " — брёвна: " + Costs[i] : ""), "Button")) selected = (MachineKind)i;
            GUILayout.Label("Выход: " + new[] { "вправо", "вверх", "влево", "вниз" }[direction] + " [R]");
            GUILayout.Label("Наведите курсор на клетку:\nP — построить, Delete — разобрать.\nF8 — закрыть. Радиус: 12 клеток.");
            if (!Plugin.Live && GUILayout.Button("Demo: wheel > shaft > mill > belt > chest"))
            {
                var p = Player.Instance.ExactPosition;
                factory.Demo(Mathf.FloorToInt(p.x) - 2, Mathf.FloorToInt(p.y) + 3); Sync();
                message = "30 test wheat loaded. Flour reaches the chest while powered.";
                Logger.LogInfo("Sandbox demo spawned in " + scene);
            }
            GUILayout.Label(Plugin.Live ? "T — вложить пшеницу, G — забрать предмет.\nНаведите курсор на механизм.\nПеред разборкой освободите буфер." : "T — добавить 10 тестовых единиц пшеницы.");
            if (!Plugin.Live && GUILayout.Button("Clear sandbox")) Clear();
            GUILayout.Label(message);
            if (CursorCell(out int hx, out int hy))
            {
                var m = factory.At(hx, hy);
                if (m != null) GUILayout.Label(names[(int)m.Kind] + ": " + Math.Round(m.Rpm) + " rpm\nПшеница: " + m.Wheat + ", мука: " + m.Flour);
            }
        }
        internal void ReadWorkshopNote()
        {
            if (!Ready()) return;
            panel = false; PlayerInput.EnableInput(InputBlock);
            Player.Instance.PausePlayerWithDialogue("SunHavenCreate.workshop-note",
                "Говорят, благословение Элиоса помогает воде и урожаю. Мы нашли собственный путь: вода вращает колесо, медные валы передают движение, а солнечные руны отмечают работающие механизмы. Соедини колесо, вал и мельницу; направь конвейер к сундуку. Вложи пшеницу — и пусть ферма трудится вместе с тобой. Не перегружай привод: даже чудесам нужна мера.",
                null, "Записка солнечной мастерской");
        }
        void LateUpdate()
        {
            if (Plugin.Live || !Application.isFocused || !Ready() || !panel || Player.Instance.pause || Cutscene.Active || Cutscene.WithinMultipartCutscene) return;
            if (Input.GetKeyDown(KeyCode.T) && CursorCell(out int x, out int y))
            {
                var m = factory.At(x, y);
                if (m != null && (m.Kind == MachineKind.Mill || m.Kind == MachineKind.Belt))
                    m.Wheat += Math.Min(10, Machine.BufferLimit - m.Count);
            }
        }
        void OnDestroy()
        {
            Logger.LogInfo("Prototype host destroyed");

            Application.runInBackground = previousBackground;
            PlayerInput.EnableInput(InputBlock);
            if (root) Destroy(root);
            if (panelBackground) Destroy(panelBackground);
            foreach (var sprite in sprites.Values) { Destroy(sprite.texture); Destroy(sprite); }
        }
    }
}

