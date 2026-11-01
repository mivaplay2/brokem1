using System;
using MyAutumnCar.Sim;
using static MyAutumnCar.Tests.Harness;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Tests;

public static class Program
{
    private const float Dt = 1f / 60f;

    private static CarSim FreshCar() => new();

    /// <summary>Fits and torques every part the engine needs, plus the four wheels.</summary>
    private static CarSim BuiltCar(bool wheels = true)
    {
        var sim = FreshCar();
        var a = sim.Assembly;
        foreach (string id in new[] { Ids.EngineBlock, Ids.CylinderHead, Ids.Carburetor, Ids.SparkPlugs, Ids.Battery, Ids.Radiator })
            a.InstallAndSecure(id, out _);
        if (wheels)
            foreach (string id in PartCatalog.Wheels)
                a.InstallAndSecure(id, out _);

        sim.Fluids.Pour(FluidKind.Petrol, 20f);
        sim.Fluids.Pour(FluidKind.Oil, FluidTanks.OilCapacityL);
        sim.Fluids.Pour(FluidKind.Coolant, FluidTanks.CoolantCapacityL);
        sim.Fluids.BatteryCharge = 1f;
        return sim;
    }

    /// <summary>Cranks with the right choke for the current temperature until it fires.</summary>
    private static bool StartEngine(CarSim sim, float maxSeconds = 6f)
    {
        var e = sim.Engine;
        e.IgnitionOn = true;
        e.Choke = WantedChoke(e.CoolantTempC);
        e.StarterEngaged = true;
        for (float t = 0; t < maxSeconds && !e.Running; t += Dt)
            sim.Tick(Dt, 0f, 0f);
        e.StarterEngaged = false;
        return e.Running;
    }

    /// <summary>Choke setting a competent driver would be holding at this temperature.</summary>
    private static float WantedChoke(float coolantTempC) =>
        Math.Clamp((35f - coolantTempC) / 35f, 0f, 1f);

    /// <summary>
    /// Ticks the sim. <paramref name="trimChoke"/> models a driver pushing the choke
    /// back in as the engine warms; without it a carburetted engine goes rich and
    /// dies, which is tested separately.
    /// </summary>
    private static void Run(CarSim sim, float seconds, float wheelOmega = 0f, float airspeed = 0f,
                            bool trimChoke = true)
    {
        for (float t = 0; t < seconds; t += Dt)
        {
            if (trimChoke) sim.Engine.Choke = WantedChoke(sim.Engine.CoolantTempC);
            sim.Tick(Dt, wheelOmega, airspeed);
        }
    }

    public static int Main()
    {
        Console.WriteLine("my autumn car — simulation tests");

        // ----------------------------------------------------------- assembly
        Case("a part with unmet dependencies cannot be fitted", () =>
        {
            var a = FreshCar().Assembly;
            False(a.CanInstall(Ids.CylinderHead), "head before block");
            True(a.CanInstall(Ids.EngineBlock), "block has no dependencies");
            Equal(AssemblyResult.MissingDependency, a.TryInstall(Ids.CylinderHead, out string msg), "result");
            True(msg.Contains("engine block"), $"message names the blocker, got '{msg}'");
        });

        Case("a dependency must be bolted down, not merely fitted", () =>
        {
            var a = FreshCar().Assembly;
            a.TryInstall(Ids.EngineBlock, out _);
            False(a.CanInstall(Ids.CylinderHead), "block fitted but loose");
            a.SecureAll(Ids.EngineBlock);
            True(a.CanInstall(Ids.CylinderHead), "block torqued down");
        });

        Case("a bolted part cannot be pulled off", () =>
        {
            var a = FreshCar().Assembly;
            a.InstallAndSecure(Ids.EngineBlock, out _);
            Equal(AssemblyResult.StillBolted, a.TryRemove(Ids.EngineBlock, out _), "still bolted");
            for (int i = 0; i < a.Catalog.Get(Ids.EngineBlock).BoltCount; i++)
                a.TurnBolt(Ids.EngineBlock, i, ToolSize.Wrench17, 10f, -1, out _);
            Equal(AssemblyResult.Ok, a.TryRemove(Ids.EngineBlock, out _), "all bolts backed off");
        });

        Case("a part underneath a dependent part is blocked", () =>
        {
            var a = FreshCar().Assembly;
            a.InstallAndSecure(Ids.EngineBlock, out _);
            a.InstallAndSecure(Ids.CylinderHead, out _);
            for (int i = 0; i < a.Catalog.Get(Ids.EngineBlock).BoltCount; i++)
                a.TurnBolt(Ids.EngineBlock, i, ToolSize.Wrench17, 10f, -1, out _);
            Equal(AssemblyResult.BlockedByDependent, a.TryRemove(Ids.EngineBlock, out string msg), "head still on");
            True(msg.Contains("cylinder head"), $"message names the dependent, got '{msg}'");
        });

        Case("only the matching spanner turns a bolt", () =>
        {
            var a = FreshCar().Assembly;
            a.TryInstall(Ids.EngineBlock, out _);
            Near(0f, a.TurnBolt(Ids.EngineBlock, 0, ToolSize.Wrench10, Dt, 1, out string wrong), 0.0001f, "wrong spanner");
            True(wrong.Contains("17 mm"), $"hint names the right size, got '{wrong}'");
            Greater(0f, a.TurnBolt(Ids.EngineBlock, 0, ToolSize.Wrench17, Dt, 1, out _), "right spanner bites");
        });

        Case("tightening takes real time and saturates at one", () =>
        {
            var a = FreshCar().Assembly;
            a.TryInstall(Ids.Battery, out _);
            var state = a.Get(Ids.Battery);
            float rate = a.Catalog.Get(Ids.Battery).Bolts[0].TightenRate;
            for (float t = 0; t < 0.5f; t += Dt)
                a.TurnBolt(Ids.Battery, 0, ToolSize.Wrench10, Dt, 1, out _);
            Near(rate * 0.5f, state.BoltTightness[0], 0.05f, "half a second of turning");
            False(state.Secured, "one of two bolts, and not yet home");
            for (float t = 0; t < 5f; t += Dt)
                for (int i = 0; i < 2; i++)
                    a.TurnBolt(Ids.Battery, i, ToolSize.Wrench10, Dt, 1, out _);
            True(state.Secured, "both bolts home");
            Near(1f, state.BoltTightness[0], 0.0001f, "clamped at one");
        });

        Case("fitting the engine moves the centre of mass forward and up", () =>
        {
            var sim = FreshCar();
            var bare = sim.Assembly.CenterOfMassLocal;
            sim.Assembly.InstallAndSecure(Ids.EngineBlock, out _);
            var loaded = sim.Assembly.CenterOfMassLocal;
            Greater(bare.Z, loaded.Z, "com shifts toward the nose");
            Greater(bare.Y, loaded.Y, "com rises with the block");
            Near(AssemblyModel.ChassisMassKg + 94f, sim.Assembly.TotalMassKg, 0.01f, "total mass");
        });

        Case("the next-job hint walks the build order", () =>
        {
            var sim = FreshCar();
            True(sim.Assembly.NextJobHint().Contains("engine block"), "starts at the block");
            sim.Assembly.TryInstall(Ids.EngineBlock, out _);
            True(sim.Assembly.NextJobHint().Contains("bolt the engine block"), "then bolt it");
            sim.Assembly.SecureAll(Ids.EngineBlock);
            True(sim.Assembly.NextJobHint().Contains("cylinder head"), "then the head");
        });

        // ------------------------------------------------------------- engine
        Case("an incomplete engine will not fire", () =>
        {
            var sim = BuiltCar();
            for (int i = 0; i < sim.Catalog.Get(Ids.Carburetor).BoltCount; i++)
                sim.Assembly.TurnBolt(Ids.Carburetor, i, ToolSize.Wrench10, 10f, -1, out _);
            False(StartEngine(sim), "carburettor loose");
            True(sim.Engine.Status.Contains("carburettor"), $"status names it, got '{sim.Engine.Status}'");
        });

        Case("no battery means no starter", () =>
        {
            var sim = BuiltCar();
            for (int i = 0; i < sim.Catalog.Get(Ids.Battery).BoltCount; i++)
                sim.Assembly.TurnBolt(Ids.Battery, i, ToolSize.Wrench10, 10f, -1, out _);
            False(StartEngine(sim), "battery not connected");
            Near(0f, sim.Engine.Rpm, 1f, "it does not even turn over");
        });

        Case("a dry tank means no running engine", () =>
        {
            var sim = BuiltCar();
            sim.Fluids.FuelL = 0f;
            False(StartEngine(sim), "no petrol");
            Greater(100f, sim.Engine.Rpm, "but the starter still spins it");
        });

        Case("a cold engine needs the choke", () =>
        {
            var sim = BuiltCar();
            sim.Engine.CoolantTempC = 9f;
            sim.Engine.IgnitionOn = true;
            sim.Engine.Choke = 0f;
            sim.Engine.StarterEngaged = true;
            Run(sim, 5f, trimChoke: false);
            False(sim.Engine.Running, "cranks but will not catch without choke");

            sim.Engine.Choke = 0.75f;
            Run(sim, 2f, trimChoke: false);
            True(sim.Engine.Running, "catches with the choke out");
        });

        Case("a warm engine floods on full choke", () =>
        {
            var sim = BuiltCar();
            sim.Engine.CoolantTempC = 80f;
            sim.Engine.IgnitionOn = true;
            sim.Engine.Choke = 1f;
            sim.Engine.StarterEngaged = true;
            Run(sim, 5f, trimChoke: false);
            False(sim.Engine.Running, "too rich to fire when hot");
        });

        Case("a complete car starts and holds an idle", () =>
        {
            var sim = BuiltCar();
            True(StartEngine(sim), "fires up");
            sim.Engine.Throttle = 0f;
            Run(sim, 8f);
            True(sim.Engine.Running, "still running with the throttle shut");
            InRange(550f, 1500f, sim.Engine.Rpm, "settles near idle");
            Greater(0.4f, sim.Engine.OilPressureBar, "oil pressure comes up");
            False(sim.Engine.OilWarning, "no oil light");
        });

        Case("full throttle pulls it to the top of its range and no further", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Engine.Throttle = 1f;
            Run(sim, 12f);
            InRange(5500f, sim.Engine.RedlineRpm, sim.Engine.Rpm, "pulls to the top of its range");
            Less(sim.Engine.RedlineRpm + 300f, sim.Engine.Rpm, "never runs past the limiter");
        });

        Case("switching off the ignition stops it", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            Run(sim, 2f);
            sim.Engine.IgnitionOn = false;
            Run(sim, 3f);
            False(sim.Engine.Running, "dead");
            Near(0f, sim.Engine.OilPressureBar, 0.01f, "pressure gone");
        });

        Case("it burns petrol and dies when the tank empties", () =>
        {
            var sim = BuiltCar();
            sim.Fluids.FuelL = 0.05f;
            StartEngine(sim);
            float before = sim.Fluids.FuelL;
            sim.Engine.Throttle = 1f;
            Run(sim, 60f);
            Less(before, sim.Fluids.FuelL, "fuel went down");
            Near(0f, sim.Fluids.FuelL, 0.005f, "tank is dry");
            False(sim.Engine.Running, "stopped");
            True(sim.Engine.Status.Contains("petrol"), $"status explains it, got '{sim.Engine.Status}'");
        });

        Case("running without oil wears the pistons out and seizes it for good", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Fluids.OilL = 0f;
            sim.Engine.Throttle = 0.4f;
            Run(sim, 2f);
            True(sim.Engine.OilWarning, "oil light on");
            Run(sim, 60f);
            True(sim.Engine.Seized, "seized");
            False(sim.Engine.Running, "not running");

            // and it stays dead however much oil you pour in afterwards
            sim.Fluids.Pour(FluidKind.Oil, FluidTanks.OilCapacityL);
            False(StartEngine(sim), "a seized engine does not come back");
            Near(1f, sim.Engine.PistonWear, 0.001f, "pistons are scrap");
        });

        Case("oil in the sump protects it over the same run", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Engine.Throttle = 0.4f;
            Run(sim, 62f);
            False(sim.Engine.Seized, "survives with oil");
            Near(0f, sim.Engine.PistonWear, 0.001f, "no piston wear");
        });

        Case("no radiator means it cooks the head gasket", () =>
        {
            var sim = BuiltCar();
            for (int i = 0; i < sim.Catalog.Get(Ids.Radiator).BoltCount; i++)
                sim.Assembly.TurnBolt(Ids.Radiator, i, ToolSize.Wrench10, 10f, -1, out _);
            sim.Assembly.TryRemove(Ids.Radiator, out _);
            StartEngine(sim);
            sim.Engine.Throttle = 0f;
            Run(sim, 240f);
            Greater(sim.Engine.OverheatTempC, sim.Engine.CoolantTempC, "boils over at a mere idle");
            Greater(0f, sim.Engine.HeadGasketWear, "gasket is going");
        });

        Case("a bolted radiator with coolant keeps it in range", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Engine.Throttle = 0f;
            Run(sim, 300f);
            InRange(60f, sim.Engine.OverheatTempC, sim.Engine.CoolantTempC, "warms up but does not boil");
            Near(0f, sim.Engine.HeadGasketWear, 0.0001f, "gasket intact");
        });

        Case("leaving the choke out as it warms up kills the idle", () =>
        {
            var sim = BuiltCar();
            True(StartEngine(sim), "cold start with the choke out");
            sim.Engine.Throttle = 0f;

            // choke left exactly where the cold start needed it
            float peakTempC = sim.Engine.CoolantTempC;
            for (float t = 0; t < 300f && sim.Engine.Running; t += Dt)
            {
                sim.Tick(Dt, 0f, 0f);
                peakTempC = Math.Max(peakTempC, sim.Engine.CoolantTempC);
            }

            False(sim.Engine.Running, "runs too rich once warm and dies");
            Greater(35f, peakTempC, "it warmed past the point the choke should have gone in");
            Less(0.45f, sim.Engine.MixtureEfficiency(), "mixture is well off by then");
        });

        // --------------------------------------------------------- drivetrain
        Case("the box refuses a gear without the clutch", () =>
        {
            var sim = BuiltCar();
            var d = sim.Drivetrain;
            d.ClutchPedal = 0f;
            False(d.TryShift(2), "no clutch, no gear");
            Equal(Drivetrain.GearNeutral, d.Gear, "still in neutral");
            d.ClutchPedal = 1f;
            True(d.TryShift(2), "clutch in, gear selected");
            Equal("1", d.GearLabel, "first");
        });

        Case("neutral passes no torque", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Engine.Throttle = 0.8f;
            Run(sim, 3f);
            sim.Drivetrain.ClutchPedal = 0f;
            Near(0f, sim.AxleTorqueNm, 0.0001f, "neutral");
            Near(0f, sim.Engine.LoadTorqueNm, 0.0001f, "engine feels no load");
        });

        Case("dumping the clutch at idle stalls it", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            Run(sim, 2f);
            sim.Drivetrain.ClutchPedal = 1f;
            sim.Drivetrain.TryShift(2);
            sim.Engine.Throttle = 0f;
            sim.Drivetrain.ClutchPedal = 0f;   // let it straight out, wheels held
            Run(sim, 2f);
            False(sim.Engine.Running, "stalled");
            True(sim.Engine.Status.Contains("stall"), $"status says so, got '{sim.Engine.Status}'");
        });

        Case("revs and a slipping clutch put torque on the axle", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Drivetrain.ClutchPedal = 1f;
            sim.Drivetrain.TryShift(2);
            sim.Engine.Throttle = 1f;
            Run(sim, 3f);                      // build revs with the clutch in
            sim.Drivetrain.ClutchPedal = 0.45f; // feed it in
            Run(sim, 0.5f);
            Greater(50f, sim.AxleTorqueNm, "axle torque");
            Greater(0f, sim.Engine.LoadTorqueNm, "engine is pulling against it");
            True(sim.Engine.Running, "did not stall");
        });

        Case("reverse turns the axle the other way", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Drivetrain.ClutchPedal = 1f;
            sim.Drivetrain.TryShift(Drivetrain.GearReverse);
            sim.Engine.Throttle = 0.9f;
            Run(sim, 3f);
            sim.Drivetrain.ClutchPedal = 0.5f;
            Run(sim, 0.4f);
            Less(0f, sim.AxleTorqueNm, "axle torque is negative in reverse");
        });

        Case("four bolted wheels make it driveable", () =>
        {
            var sim = BuiltCar(wheels: false);
            False(sim.RollsOnFourWheels, "no wheels on it");
            foreach (string id in PartCatalog.Wheels)
                sim.Assembly.InstallAndSecure(id, out _);
            True(sim.RollsOnFourWheels, "all four on");

            sim.Assembly.TurnBolt(Ids.WheelFl, 0, ToolSize.Wrench17, 10f, -1, out _);
            False(sim.RollsOnFourWheels, "one loose stud is enough to matter");
        });

        // ------------------------------------------------------------- fluids
        Case("tanks do not overfill and the pour reports what fitted", () =>
        {
            var f = FluidTanks.Empty();
            Near(FluidTanks.OilCapacityL, f.Pour(FluidKind.Oil, 99f), 0.0001f, "only what fits goes in");
            Near(FluidTanks.OilCapacityL, f.OilL, 0.0001f, "full");
            Near(0f, f.Pour(FluidKind.Oil, 5f), 0.0001f, "nothing more fits");
            f.Drain(FluidKind.Oil, 99f);
            Near(0f, f.OilL, 0.0001f, "drains to empty, not below");
        });

        // --------------------------------------------------------------- save
        Case("a save round trip preserves part by part state", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            Run(sim, 5f);
            sim.Assembly.TurnBolt(Ids.WheelRr, 2, ToolSize.Wrench17, 0.6f, -1, out _);
            sim.Assembly.Get(Ids.Carburetor).Wear = 0.42f;
            sim.Drivetrain.ClutchPedal = 1f;
            sim.Drivetrain.TryShift(4);

            string json = SaveSystem.ToJson(SaveSystem.Capture(sim));
            var restored = new CarSim();
            SaveSystem.Apply(SaveSystem.FromJson(json), restored);

            Near(sim.Assembly.Get(Ids.WheelRr).BoltTightness[2],
                 restored.Assembly.Get(Ids.WheelRr).BoltTightness[2], 0.0001f, "loosened stud");
            Near(0.42f, restored.Assembly.Get(Ids.Carburetor).Wear, 0.0001f, "part wear");
            True(restored.Assembly.IsSecured(Ids.CylinderHead), "head still bolted");
            False(restored.RollsOnFourWheels, "that loose stud survived the save");
            Near(sim.Fluids.FuelL, restored.Fluids.FuelL, 0.0001f, "fuel");
            Near(sim.Engine.CoolantTempC, restored.Engine.CoolantTempC, 0.0001f, "coolant temperature");
            Equal(4, restored.Drivetrain.Gear, "gear");
            False(restored.Engine.Running, "a loaded game starts with the engine off");
        });

        Case("a seized engine stays seized across a save", () =>
        {
            var sim = BuiltCar();
            StartEngine(sim);
            sim.Fluids.OilL = 0f;
            sim.Engine.Throttle = 0.5f;
            Run(sim, 70f);
            True(sim.Engine.Seized, "seized before saving");

            var restored = new CarSim();
            SaveSystem.Apply(SaveSystem.FromJson(SaveSystem.ToJson(SaveSystem.Capture(sim))), restored);
            True(restored.Engine.Seized, "still seized");
            Near(1f, restored.Engine.PistonWear, 0.001f, "wear carried over");
        });

        Case("a save from a newer build is refused rather than half applied", () =>
        {
            var data = SaveSystem.Capture(BuiltCar());
            data.Version = SaveData.CurrentVersion + 1;
            bool threw = false;
            try { SaveSystem.Apply(data, new CarSim()); }
            catch (InvalidOperationException) { threw = true; }
            True(threw, "refused");
        });

        return Report();
    }
}
