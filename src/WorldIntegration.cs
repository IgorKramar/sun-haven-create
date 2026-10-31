using System;
using System.Linq;
using PSS;
using UnityEngine;
using Wish;

namespace SunHavenCreate
{
    public sealed partial class FactoryRuntime
    {
        const string SaveKey = "local.sunhavencreate.world.v1.20261014";
        internal static readonly int[] Costs = { 8, 1, 2, 4, 2, 6, 4 };
        WorldState document;
        bool inventoryBusy;
        void ChangeWorld(object nextSave, string nextScene)
        {
            bool changedSave = !ReferenceEquals(save, nextSave);
            if (changedSave || document == null)
                document = Plugin.Live ? WorldState.Decode(SingletonBehaviour<GameSave>.Instance.GetProgressStringWorld(SaveKey)) : new WorldState();
            save = nextSave; scene = nextScene;
            factory.Machines.Clear(); accumulator = 0;
            if (Plugin.Live) document.Load(scene, factory);
            Sync();
            message = Plugin.Live ? "Механизмы и содержимое сохраняются с миром." : "Временные тестовые ресурсы.";
        }
        internal void StoreState()
        {
            if (!Plugin.Live || document == null || string.IsNullOrEmpty(scene)) return;
            var gs = SingletonBehaviour<GameSave>.Instance;
            if (!gs || !ReferenceEquals(save, gs.CurrentSave) || gs.CurrentWorld == null) return;
            document.Capture(scene, factory);
            gs.SetProgressStringWorld(SaveKey, document.Encode(), fromLocal: false);
        }
        bool ValidMachine(Machine m, object expectedSave) => Ready() && ReferenceEquals(save, expectedSave) &&
            m != null && factory.Machines.Contains(m);
        bool CanBuild(int x, int y)
        {
            if (factory.At(x, y) != null || factory.Machines.Count >= 128) return false;
            var p = Player.Instance.ExactPosition;
            if (Vector2.Distance(new Vector2(x, y), p) > 12) return false;
            var gm = SingletonBehaviour<GameManager>.Instance;
            var position = new Vector3Int(x * 6, y * 6, 0);
            return gm && !gm.HasObjectSubTile(position, new Vector2Int(6, 6), new Vector2Int(6, 6), Vector2Int.zero, out _, setred: false);
        }
        internal bool Occupies(Vector3Int position, Vector2Int size)
        {
            if (!Ready() || position.z != 0) return false;
            return factory.Machines.Any(m => position.x < (m.X + 1) * 6 && position.x + size.x > m.X * 6 &&
                position.y < (m.Y + 1) * 6 && position.y + size.y > m.Y * 6);
        }
        Vector3 MachinePosition(Machine m)
        {
            var gm = SingletonBehaviour<GameManager>.Instance;
            var flat = new Vector2(m.X + .5f, (m.Y + .5f) * 1.4142135f);
            float depth = gm.LinearDepth(flat);
            return new Vector3(flat.x, flat.y + depth - .02f, depth - .02f);
        }
        internal bool PlaceLive(int x, int y, MachineKind kind, int direction)
        {
            if (!Ready() || !Plugin.Live || inventoryBusy) return false;
            int cost = Costs[(int)kind];
            var inv = Player.Instance.PlayerInventory;
            if (!CanBuild(x, y)) { message = "Выберите свободную землю рядом с персонажем."; return false; }
            if (inv.GetAmount(ItemID.Log) < cost) { message = "Нужно брёвен: " + cost + "."; return false; }
            int before = inv.GetAmount(ItemID.Log);
            inv.RemoveItem(ItemID.Log, cost);
            int paid = before - inv.GetAmount(ItemID.Log);
            if (paid != cost) throw new InvalidOperationException("Unexpected construction inventory change");
            factory.Place(new Machine(x, y, kind, direction) { LogCost = paid });
            StoreState(); Sync(); message = "Построено: " + names[(int)kind] + "."; return true;
        }
        internal void DepositWheat(Machine m)
        {
            if (!Plugin.Live || !Ready() || inventoryBusy || m == null ||
                (m.Kind != MachineKind.Mill && m.Kind != MachineKind.Belt && m.Kind != MachineKind.Chest) || m.Count >= 64) return;
            var inv = Player.Instance.PlayerInventory;
            int before = inv.GetAmount(ItemID.Wheat);
            if (before < 1) { message = "В инвентаре нет пшеницы."; return; }
            inv.RemoveItem(ItemID.Wheat, 1);
            int moved = before - inv.GetAmount(ItemID.Wheat);
            if (moved != 1) throw new InvalidOperationException("Unexpected wheat transfer");
            m.Wheat++; StoreState(); message = "Вложена одна единица пшеницы.";
        }
        internal void Collect(Machine m)
        {
            if (!Plugin.Live || !Ready() || inventoryBusy || m == null || m.Count == 0) return;
            bool flour = m.Flour > 0;
            GiveFromMachine(m, flour ? ItemID.Flour : ItemID.Wheat, 1, moved => {
                if (flour) m.Flour -= moved; else m.Wheat -= moved;
            });
        }
        void GiveFromMachine(Machine m, int itemId, int amount, Action<int> debit)
        {
            inventoryBusy = true;
            var expectedSave = save;
            Database.GetData<ItemData>(itemId, data => {
                try
                {
                    if (!ValidMachine(m, expectedSave) || !data) return;
                    int available = itemId == ItemID.Flour ? m.Flour : itemId == ItemID.Wheat ? m.Wheat : m.LogCost;
                    if (available < amount) { message = "Содержимое переместилось. Повторите действие."; return; }
                    var inv = Player.Instance.PlayerInventory;
                    var item = data.GenerateItem();
                    if (!inv.CanAcceptFullItemStack(item, amount)) { message = "Инвентарь полон. Предметы остались в механизме."; return; }
                    int before = inv.GetAmount(itemId);
                    try { inv.AddItem(item, amount, false); }
                    finally
                    {
                        int moved = inv.GetAmount(itemId) - before;
                        if (moved < 0 || moved > amount) throw new InvalidOperationException("Unexpected inventory receipt");
                        debit(moved); StoreState();
                        message = moved > 0 ? "Получено предметов: " + moved + "." : "Передача предмета не выполнена.";
                    }
                }
                finally { inventoryBusy = false; }
            }, () => { inventoryBusy = false; message = "Не удалось загрузить данные предмета."; });
        }
        void Dismantle(int x, int y)
        {
            var m = factory.At(x, y);
            if (!Ready() || inventoryBusy || m == null) return;
            if (m.Count > 0) { message = "Перед разборкой заберите все предметы."; return; }
            if (m.LogCost == 0) { factory.Remove(x, y); StoreState(); Sync(); return; }
            GiveFromMachine(m, ItemID.Log, m.LogCost, moved => {
                m.LogCost -= moved;
                if (m.LogCost == 0) { factory.Remove(x, y); Sync(); }
            });
        }
        internal void RequireLab()
        {
            if (!Ready() || GameSave.CurrentCharacter.characterName != "CreateLab")
                throw new InvalidOperationException("Test action requires CreateLab");
        }
        internal bool CollisionActive(Machine m)
        {
            if (!visuals.TryGetValue(m, out var graphics)) return false;
            var collider = graphics.parent.GetComponent<Collider2D>();
            return collider && collider.enabled && !collider.isTrigger &&
                !Physics2D.GetIgnoreLayerCollision(collider.gameObject.layer, Player.Instance.gameObject.layer);
        }
        readonly System.Collections.Generic.Dictionary<int, int> filledSlots = new System.Collections.Generic.Dictionary<int, int>();
        internal void DebugFillInventory()
        {
            RequireLab();
            if (filledSlots.Count > 0 || inventoryBusy) throw new InvalidOperationException("Inventory test already active");
            Database.GetData<ItemData>(ItemID.Flour, data => {
                RequireLab(); var inv = Player.Instance.PlayerInventory;
                for (int i = 0; i < Math.Min(inv.maxSlots, inv.Items.Count); i++)
                {
                    var slot = inv.Items[i];
                    if (!slot.slot.ValidateItem(ItemID.Flour) || (slot.item.ID() != 0 && slot.item.ID() != ItemID.Flour)) continue;
                    filledSlots[i] = slot.item.ID() == 0 ? 0 : slot.amount;
                    if (slot.item.ID() == 0) inv.AddItemToSpecificSlot(data.GenerateItem(), data.stackSize, i);
                    else inv.AddItem(data.GenerateItem(), data.stackSize - slot.amount, i, false);
                }
                inv.NeedToRecalculateAmounts(ItemID.Flour);
            });
        }
        internal void DebugClearFilledSlots()
        {
            RequireLab(); var inv = Player.Instance.PlayerInventory;
            foreach (var entry in filledSlots)
            {
                var slot = inv.Items[entry.Key];
                if (slot.item.ID() != ItemID.Flour || slot.amount < entry.Value) throw new InvalidOperationException("Inventory changed during test");
                if (entry.Value == 0) inv.RemoveItemAt(entry.Key); else inv.RemoveItemAt(entry.Key, slot.amount - entry.Value);
            }
            filledSlots.Clear(); inv.NeedToRecalculateAmounts(ItemID.Flour);
        }
        internal void DebugDismantleWheel()
        {
            RequireLab(); var m = factory.Machines.First(n => n.Kind == MachineKind.WaterWheel);
            Dismantle(m.X, m.Y);
        }
        internal void DebugRebuildWheel()
        {
            RequireLab(); var shaft = factory.Machines.First(n => n.Kind == MachineKind.Shaft);
            if (!PlaceLive(shaft.X - 1, shaft.Y, MachineKind.WaterWheel, 0)) throw new InvalidOperationException("Wheel reconstruction failed");
        }
        internal void DebugFixture()
        {
            RequireLab();
            Player.Instance.PlayerInventory.AddItem(ItemID.Log, 64, false);
            Player.Instance.PlayerInventory.AddItem(ItemID.Wheat, 30, false);
        }
        internal void DebugLiveDemo()
        {
            RequireLab();
            if (Player.Instance.PlayerInventory.GetAmount(ItemID.Log) < 21) throw new InvalidOperationException("Need 21 logs for demo");
            var p = Player.Instance.ExactPosition;
            for (int dy = -8; dy <= 8; dy++) for (int dx = -8; dx <= 4; dx++)
            {
                int x = Mathf.FloorToInt(p.x) + dx, y = Mathf.FloorToInt(p.y) + dy;
                if (!Enumerable.Range(0, 5).All(i => CanBuild(x + i, y))) continue;
                var kinds = new[] { MachineKind.WaterWheel, MachineKind.Shaft, MachineKind.Mill, MachineKind.Belt, MachineKind.Chest };
                for (int i = 0; i < 5; i++) if (!PlaceLive(x + i, y, kinds[i], 0)) throw new InvalidOperationException("Demo construction failed");
                panel = true; return;
            }
            throw new InvalidOperationException("No clear demo row near player");
        }
        internal void DebugSave()
        {
            RequireLab(); StoreState();
            var gs = SingletonBehaviour<GameSave>.Instance;
            gs.SaveGame(); gs.WriteCharacterToFile();
        }
    }
}
