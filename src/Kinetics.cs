using System;
using System.Collections.Generic;
using System.Linq;

namespace SunHavenCreate
{
    public enum MachineKind { WaterWheel, Shaft, Cog, LargeCog, Belt, Mill, Chest }
    public sealed class Machine
    {
        public int X, Y, Direction;
        public MachineKind Kind;
        public double Rpm, Progress, Transport;
        public bool Overloaded, Conflict;
        public int Wheat, Flour;
        public int LogCost;
        public int Count => Wheat + Flour;
        public const int BufferLimit = 64;
        public Machine(int x, int y, MachineKind kind, int direction = 0)
        { X = x; Y = y; Kind = kind; Direction = direction; }
    }

    // Deliberately small 2D prototype rules, not a port of Create's 3D solver.
    public sealed class Factory
    {
        public readonly List<Machine> Machines = new List<Machine>();
        public static readonly int[] DX = { 1, 0, -1, 0 };
        public static readonly int[] DY = { 0, 1, 0, -1 };
        public Machine At(int x, int y) => Machines.FirstOrDefault(n => n.X == x && n.Y == y);
        public bool Place(Machine m)
        {
            if (Machines.Count >= 128 || At(m.X, m.Y) != null) return false;
            Machines.Add(m); Solve(); return true;
        }
        public void Remove(int x, int y)
        { var m = At(x, y); if (m != null) Machines.Remove(m); Solve(); }
        static bool Powered(Machine m) => m.Kind != MachineKind.Chest;
        static bool Gear(Machine m) => m.Kind == MachineKind.Cog || m.Kind == MachineKind.LargeCog;
        static double Ratio(Machine a, Machine b)
        {
            if (!Gear(a) || !Gear(b)) return 1;
            return -(a.Kind == MachineKind.LargeCog ? 2.0 : 1.0) /
                    (b.Kind == MachineKind.LargeCog ? 2.0 : 1.0);
        }
        IEnumerable<Machine> Neighbors(Machine n)
        {
            for (int d = 0; d < 4; d++)
            { var m = At(n.X + DX[d], n.Y + DY[d]); if (m != null && Powered(m)) yield return m; }
        }
        public void Solve()
        {
            foreach (var m in Machines) { m.Rpm = 0; m.Overloaded = false; m.Conflict = false; }
            var seen = new HashSet<Machine>();
            foreach (var start in Machines.Where(Powered))
            {
                if (!seen.Add(start)) continue;
                var component = new List<Machine>();
                var q = new Queue<Machine>(); q.Enqueue(start);
                while (q.Count > 0)
                {
                    var a = q.Dequeue(); component.Add(a);
                    foreach (var b in Neighbors(a)) if (seen.Add(b)) q.Enqueue(b);
                }
                var sources = component.Where(m => m.Kind == MachineKind.WaterWheel).ToList();
                if (sources.Count == 0) continue;
                var speeds = new Dictionary<Machine, double>();
                speeds[sources[0]] = 32; q.Enqueue(sources[0]);
                bool conflict = false;
                while (q.Count > 0)
                {
                    var a = q.Dequeue();
                    foreach (var b in Neighbors(a))
                    {
                        double expected = speeds[a] * Ratio(a, b);
                        if (speeds.TryGetValue(b, out double existing))
                        { if (Math.Abs(expected - existing) > .001) conflict = true; }
                        else { speeds[b] = expected; q.Enqueue(b); }
                    }
                }
                foreach (var source in sources)
                    if (Math.Abs(speeds[source] - 32) > .001) conflict = true;
                double stress = component.Sum(m => Math.Abs(speeds[m]) *
                    (m.Kind == MachineKind.Mill ? 8 : m.Kind == MachineKind.Belt ? 1 : 0));
                bool overloaded = stress > sources.Count * 1024;
                foreach (var m in component)
                { m.Conflict = conflict; m.Overloaded = overloaded; m.Rpm = conflict || overloaded ? 0 : speeds[m]; }
            }
        }
        public void Tick(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 1)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            Solve();
            foreach (var m in Machines.Where(n => n.Kind == MachineKind.Mill && n.Rpm != 0))
            {
                if (m.Wheat == 0) { m.Progress = 0; continue; }
                m.Progress += seconds * Math.Abs(m.Rpm) / 32;
                if (m.Progress >= 3)
                { m.Wheat--; m.Flour++; m.Progress -= 3; }
            }
            // Snapshot contents: an item can advance at most one tile per tick.
            var available = Machines.ToDictionary(m => m, m => new[] { m.Wheat, m.Flour });
            foreach (var a in Machines.Where(n => (n.Kind == MachineKind.Belt || n.Kind == MachineKind.Mill) && n.Rpm != 0))
            {
                a.Transport = Math.Min(1, a.Transport + seconds * Math.Abs(a.Rpm) / 32);
                if (a.Transport < 1) continue;
                var b = At(a.X + DX[a.Direction], a.Y + DY[a.Direction]);
                if (b == null || b.Count >= Machine.BufferLimit ||
                    (b.Kind != MachineKind.Belt && b.Kind != MachineKind.Mill && b.Kind != MachineKind.Chest)) continue;
                bool flour = available[a][1] > 0;
                if (!flour && (a.Kind == MachineKind.Mill || available[a][0] == 0)) continue;
                if (flour) { a.Flour--; b.Flour++; available[a][1]--; }
                else { a.Wheat--; b.Wheat++; available[a][0]--; }
                a.Transport = 0;
            }
        }
        public void Demo(int x, int y)
        {
            Machines.Clear();
            Place(new Machine(x, y, MachineKind.WaterWheel));
            Place(new Machine(x + 1, y, MachineKind.Shaft));
            Place(new Machine(x + 2, y, MachineKind.Mill) { Wheat = 30 });
            Place(new Machine(x + 3, y, MachineKind.Belt));
            Place(new Machine(x + 4, y, MachineKind.Chest));
        }
    }
}
