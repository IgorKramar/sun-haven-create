using System;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Newtonsoft.Json;

namespace SunHavenCreate
{
    // Disabled by default. No network listener or arbitrary code execution.
    internal sealed class DevBridge
    {
        [Serializable] public class Request { public string nonce = "", action = "observe"; public int id = 0; }
        [Serializable] public class Ui { public int id; public string name, text; }
        [Serializable] public class State
        {
            public string nonce, result, scene;
            public bool loaded, paused, cutscene;
            public float timeScale;
            public int inventoryWheat, inventoryFlour, inventoryLogs;
            public string[] spriteSources;
            public string[] spriteMetrics;
            public Ui[] ui;
            public MachineView[] machines;
        }
        [Serializable] public class MachineView { public string kind; public int x, y, wheat, flour; public double rpm; public bool collisionActive; }
        readonly FactoryRuntime plugin;
        readonly string requestPath = Path.Combine(Paths.ConfigPath, "CreateBridge.request.json");
        readonly string statePath = Path.Combine(Paths.ConfigPath, "CreateBridge.state.json");
        string lastNonce, result;
        float next;
        public DevBridge(FactoryRuntime plugin) { this.plugin = plugin; }
        public void Poll()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + .5f;
            try
            {
                if (File.Exists(requestPath))
                {
                    var r = JsonConvert.DeserializeObject<Request>(File.ReadAllText(requestPath));
                    if (r != null && r.nonce != lastNonce)
                    {
                        lastNonce = r.nonce; result = "ok";
                        if (r.action == "demo") plugin.DebugDemo();
                        else if (r.action == "fixture") plugin.DebugFixture();
                        else if (r.action == "show_panel") plugin.DebugShowPanel();
                        else if (r.action == "lore") { plugin.RequireLab(); plugin.ReadWorkshopNote(); }
                        else if (r.action == "visit_factory")
                        {
                            plugin.RequireLab();
                            Wish.SingletonBehaviour<Wish.ScenePortalManager>.Instance.ChangeScene(new Vector2(361.5f, 92.5f * 1.4142135f), "2playerfarm");
                        }
                        else if (r.action == "fill_inventory") plugin.DebugFillInventory();
                        else if (r.action == "clear_inventory_test") plugin.DebugClearFilledSlots();
                        else if (r.action == "dismantle_wheel") plugin.DebugDismantleWheel();
                        else if (r.action == "rebuild_wheel") plugin.DebugRebuildWheel();
                        else if (r.action == "live_demo") plugin.DebugLiveDemo();
                        else if (r.action == "deposit") { plugin.RequireLab(); plugin.DepositWheat(plugin.DebugFactory.Machines.FirstOrDefault(m => m.Kind == MachineKind.Mill)); }
                        else if (r.action == "collect") { plugin.RequireLab(); plugin.Collect(plugin.DebugFactory.Machines.FirstOrDefault(m => m.Kind == MachineKind.Chest)); }
                        else if (r.action == "save_lab") plugin.DebugSave();
                        else if (r.action == "return_menu") { plugin.DebugSave(); Wish.UIHandler.UnloadGame(); }
                        else if (r.action == "load_lab")
                        {
                            var menu = Wish.MainMenuController.Instance;
                            var games = Wish.SingletonBehaviour<Wish.GameSave>.Instance;
                            int index = games.Saves.FindIndex(s => s != null && s.characterData != null && s.characterData.characterName == "CreateLab");
                            if (!menu || index < 0 || games.LoadedGame) throw new InvalidOperationException("CreateLab menu save unavailable");
                            menu.SetSinglePlayer(); menu.PlayGame(index);
                        }
                        else if (r.action == "click")
                        {
                            var target = FindUi().FirstOrDefault(c => c.GetInstanceID() == r.id);
                            if (!target) throw new InvalidOperationException("UI target missing");
                            if (target.name.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("Deletion unavailable");
                            ExecuteEvents.Execute(target.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                        }
                        else if (r.action != "observe") throw new InvalidOperationException("Unknown test action");
                    }
                }
            }
            catch (Exception e) { result = e.GetType().Name + ": " + e.Message; }
            var gs = Wish.SingletonBehaviour<Wish.GameSave>.Instance;
            var p = Wish.Player.Instance;
            var state = new State
            {
                nonce = lastNonce, result = result, scene = p ? Wish.ScenePortalManager.ActiveSceneName : "menu", loaded = gs && gs.LoadedGame,
                paused = p && p.pause,
                cutscene = Wish.Cutscene.Active || Wish.Cutscene.WithinMultipartCutscene, timeScale = Time.timeScale,
                inventoryWheat = gs && gs.LoadedGame && p && p.PlayerInventory && p.PlayerInventory.Items != null ? p.PlayerInventory.GetAmount(ItemID.Wheat) : 0,
                inventoryFlour = gs && gs.LoadedGame && p && p.PlayerInventory && p.PlayerInventory.Items != null ? p.PlayerInventory.GetAmount(ItemID.Flour) : 0,
                inventoryLogs = gs && gs.LoadedGame && p && p.PlayerInventory && p.PlayerInventory.Items != null ? p.PlayerInventory.GetAmount(ItemID.Log) : 0,
                spriteSources = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Where(r => r.isVisible)
                    .Take(8).Select(r => r.name + ":" + r.gameObject.layer + ":" + r.sortingLayerName + ":" + (r.sharedMaterial ? r.sharedMaterial.shader.name : "none")).ToArray(),
                spriteMetrics = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Where(r => r.isVisible && r.sprite && !r.GetComponentInParent<Canvas>())
                    .Take(20).Select(r => r.name + ":" + r.sprite.rect + ":ppu=" + r.sprite.pixelsPerUnit + ":pivot=" + r.sprite.pivot + ":rotation=" + r.transform.eulerAngles).ToArray(),
                ui = FindUi().Select(c => new Ui { id = c.GetInstanceID(), name = c.name,
                    text = string.Join(" | ", c.GetComponentsInChildren<TMP_Text>().Select(t => t.text)) }).ToArray(),
                machines = plugin.DebugFactory.Machines.Select(m => new MachineView {
                    kind = m.Kind.ToString(), x = m.X, y = m.Y, wheat = m.Wheat, flour = m.Flour, rpm = m.Rpm,
                    collisionActive = p && plugin.CollisionActive(m) }).ToArray()
            };
            File.WriteAllText(statePath, JsonConvert.SerializeObject(state, Formatting.Indented));
        }
        static Component[] FindUi() => UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
            .Where(c => c.gameObject.activeInHierarchy && c is IPointerClickHandler).Cast<Component>().ToArray();
    }
}

