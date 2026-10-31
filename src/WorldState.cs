using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SunHavenCreate
{
    public sealed class StoredMachine
    {
        public int X, Y, Direction, Wheat, Flour, LogCost;
        public MachineKind Kind;
        public double Progress, Transport;
        public static StoredMachine From(Machine m) => new StoredMachine {
            X = m.X, Y = m.Y, Direction = m.Direction, Kind = m.Kind, Wheat = m.Wheat, Flour = m.Flour,
            Progress = m.Progress, Transport = m.Transport, LogCost = m.LogCost };
        public Machine Restore() => new Machine(X, Y, Kind, Direction) {
            Wheat = Wheat, Flour = Flour, Progress = Progress, Transport = Transport, LogCost = LogCost };
    }
    public sealed class StoredScene
    { public string Name; public List<StoredMachine> Machines = new List<StoredMachine>(); }
    public sealed class WorldState
    {
        public int Version = 1;
        public List<StoredScene> Scenes = new List<StoredScene>();
        public void Validate()
        {
            if (Version != 1 || Scenes == null || Scenes.Count > 128) throw new InvalidOperationException("Unsupported factory save version or size");
            var names = new HashSet<string>();
            foreach (var scene in Scenes)
            {
                if (scene == null || string.IsNullOrEmpty(scene.Name) || scene.Name.Length > 128 || !names.Add(scene.Name) ||
                    scene.Machines == null || scene.Machines.Count > 128) throw new InvalidOperationException("Invalid factory scene");
                var positions = new HashSet<string>();
                foreach (var m in scene.Machines)
                {
                    if (m == null || m.X < -100000 || m.X > 100000 || m.Y < -100000 || m.Y > 100000 ||
                        !positions.Add(m.X + "," + m.Y) || !Enum.IsDefined(typeof(MachineKind), m.Kind) || m.Direction < 0 || m.Direction > 3 ||
                        m.Wheat < 0 || m.Flour < 0 || m.Wheat + m.Flour > 64 || m.LogCost < 0 || m.LogCost > 8 ||
                        double.IsNaN(m.Progress) || double.IsInfinity(m.Progress) || m.Progress < 0 || m.Progress > 10000 ||
                        double.IsNaN(m.Transport) || double.IsInfinity(m.Transport) || m.Transport < 0 || m.Transport > 1)
                        throw new InvalidOperationException("Invalid factory machine");
                }
            }
        }
        public string Encode()
        { Validate(); return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this))); }
        public static WorldState Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new WorldState();
            if (text.Length > 4 * 1024 * 1024) throw new InvalidOperationException("Factory save too large");
            var state = JsonConvert.DeserializeObject<WorldState>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text)),
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 16 });
            if (state == null) throw new InvalidOperationException("Empty factory save");
            state.Validate(); return state;
        }
        public void Capture(string scene, Factory factory)
        {
            var stored = Scenes.FirstOrDefault(s => s.Name == scene);
            if (stored == null) { stored = new StoredScene { Name = scene }; Scenes.Add(stored); }
            stored.Machines = factory.Machines.Select(StoredMachine.From).ToList();
        }
        public void Load(string scene, Factory factory)
        {
            Validate(); factory.Machines.Clear();
            var stored = Scenes.FirstOrDefault(s => s.Name == scene);
            if (stored != null) foreach (var m in stored.Machines) factory.Place(m.Restore());
        }
    }
}
