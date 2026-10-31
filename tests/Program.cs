using System;
using System.Linq;
using SunHavenCreate;

static class Program
{
    static int checks;
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Run(Factory f, int ticks)
    { for (int i = 0; i < ticks; i++) f.Tick(.1); }
    static void Main()
    {
        var f = new Factory(); f.Demo(0, 0); Run(f, 400);
        Check(f.At(4, 0).Flour >= 12, "demo delivers flour to output chest");
        Check(f.Machines.Sum(m => m.Count) == 30, "production and transport conserve item count");
        f.Remove(0, 0); int before = f.At(4, 0).Flour; Run(f, 100);
        Check(f.At(4, 0).Flour == before && f.Machines.All(m => m.Rpm == 0), "removing power stops production and transport");
        var gears = new Factory();
        gears.Place(new Machine(0, 0, MachineKind.WaterWheel));
        gears.Place(new Machine(1, 0, MachineKind.LargeCog));
        gears.Place(new Machine(2, 0, MachineKind.Cog));
        Check(gears.At(2, 0).Rpm == -64, "large-to-small gear doubles rpm and reverses direction");
        gears.Place(new Machine(3, 0, MachineKind.WaterWheel));
        Check(gears.Machines.All(m => m.Rpm == 0 && m.Conflict), "incompatible sources stop entire network");
        var heavy = new Factory(); heavy.Place(new Machine(0, 0, MachineKind.WaterWheel));
        for (int i = 1; i <= 5; i++) heavy.Place(new Machine(i, 0, MachineKind.Mill) { Wheat = 2 });
        Run(heavy, 100);
        Check(heavy.Machines.All(m => m.Overloaded && m.Rpm == 0), "stress overload stops network");
        Check(heavy.Machines.Sum(m => m.Wheat) == 10, "overloaded mills do not consume ingredients");
        heavy.Remove(5, 0); Run(heavy, 40);
        Check(heavy.Machines.All(m => !m.Overloaded) && heavy.Machines.Sum(m => m.Flour) > 0, "network recovers after reducing load");
        var blocked = new Factory(); blocked.Demo(0, 0);
        blocked.At(4, 0).Wheat = 64; Run(blocked, 500);
        Check(blocked.Machines.Sum(m => m.Count) == 94 && blocked.At(4, 0).Count == 64, "full output does not destroy or overflow items");
        var belt = new Factory();
        belt.Place(new Machine(0, 1, MachineKind.WaterWheel));
        belt.Place(new Machine(0, 0, MachineKind.Belt) { Wheat = 1 });
        belt.Place(new Machine(1, 0, MachineKind.Belt));
        belt.Place(new Machine(2, 0, MachineKind.Chest));
        belt.Tick(1);
        Check(belt.At(1, 0).Wheat == 1 && belt.At(2, 0).Wheat == 0, "newly received item cannot cross two belts in one tick");
        Check(!belt.Place(new Machine(1, 0, MachineKind.Mill)), "occupied cell rejects placement");
        bool invalid = false; try { belt.Tick(double.NaN); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "invalid simulation time rejected");
        var persisted = new WorldState();
        persisted.Capture("farm", blocked);
        var restored = new Factory(); WorldState.Decode(persisted.Encode()).Load("farm", restored);
        Check(restored.Machines.Count == 5 && restored.Machines.Sum(m => m.Count) == 94, "factory save round trip preserves machines and all resources");
        Check(restored.At(2, 0).Progress == blocked.At(2, 0).Progress, "save preserves partial processing progress");
        WorldState.Decode(persisted.Encode()).Load("other-farm", restored);
        Check(restored.Machines.Count == 0, "scene data cannot leak to another farm");
        persisted.Scenes[0].Machines.Add(persisted.Scenes[0].Machines[0]);
        bool duplicate = false; try { persisted.Encode(); } catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate, "duplicate saved positions rejected");
        persisted = new WorldState { Version = 2 };
        bool version = false; try { persisted.Encode(); } catch (InvalidOperationException) { version = true; }
        Check(version, "unknown save version rejected without overwriting");
        Console.WriteLine(checks + " checks passed.");
    }
}
