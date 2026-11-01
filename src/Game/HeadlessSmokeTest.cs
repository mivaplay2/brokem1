using System;
using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Game;

/// <summary>
/// Builds the real world, bolts the real car together through the real model and
/// drives it out of the garage with no one at the keyboard. The simulation tests
/// cover the mechanics; this covers the half the simulation cannot see — mounts,
/// suspension, tyre forces, mass and balance actually behaving in the scene.
///
/// Run with:  godot --headless --path . -- --smoke
/// Exits 0 when every check passes, 1 otherwise.
/// </summary>
public partial class HeadlessSmokeTest : Node3D
{
    private const int Hz = 60;

    private GameWorld _world;
    private readonly List<string> _failures = new();
    private int _frame;

    private float _chassisRestY;
    private float _startZ;
    private float _peakSpeed;

    public override void _Ready()
    {
        _world = GameWorld.Create(this);
        _world.Car.ExternalControls = true;
        _world.Car.Controls.Reset();
        Console.WriteLine("my autumn car — headless scene test");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        var car = _world.Car;
        var sim = _world.Sim;
        var controls = car.Controls;

        // Keep the choke where a driver would hold it for the current temperature.
        sim.Engine.Choke = Mathf.Clamp((35f - sim.Engine.CoolantTempC) / 35f, 0f, 1f);
        if (_frame > 8 * Hz) _peakSpeed = Mathf.Max(_peakSpeed, car.SpeedKmh);

        switch (_frame)
        {
            case 1 * Hz:
                CheckBareChassis();
                BuildTheCar();
                break;

            case 3 * Hz:
                CheckItStandsOnItsWheels();
                StartCranking();
                break;

            case 3 * Hz + 10:
                True(_world.Sim.Drivetrain.TryShift(2), "first gear selected with the clutch down");
                break;

            case 8 * Hz:
                True(sim.Engine.Running, $"it started — status '{sim.Engine.Status}', {sim.Engine.Rpm:0} rpm");
                controls.Starter = false;
                controls.Throttle = 0.7f;
                _startZ = car.GlobalPosition.Z;
                break;

            case 15 * Hz:
                CheckItDroveOut();
                controls.Throttle = 0f;
                controls.Brake = 1f;
                break;

            case 20 * Hz:
                Less(1.2f, car.SpeedKmh, $"the brakes stopped it ({car.SpeedKmh:0.0} km/h)");
                Finish();
                break;
        }

        // Feed the clutch in over a second, as you would to pull away.
        if (_frame > 8 * Hz + 30 && _frame <= 15 * Hz)
        {
            float t = (_frame - (8f * Hz + 30f)) / Hz;
            controls.Clutch = Mathf.Clamp(1f - t, 0f, 1f);
        }
    }

    private void CheckBareChassis()
    {
        var car = _world.Car;
        _chassisRestY = car.GlobalPosition.Y;

        Equal(0, car.GroundedWheelCount(), "a car with no wheels has no wheels on the ground");
        False(_world.Sim.DrivetrainComplete, "a bare chassis has no transmission in it");
        False(_world.Sim.Drivetrain.TryShift(2), "and no gear can be selected");
        Less(0.3f, Mathf.Abs(car.LinearVelocity.Y), "the bare chassis has settled on the floor");
        Near(AssemblyModel.ChassisMassKg, car.Mass, 0.5f, "bare chassis mass");

        var com = _world.Sim.Assembly.CenterOfMassLocal;
        Greater(0f, com.Z, "with nothing fitted the balance sits behind the axle line");
    }

    private void BuildTheCar()
    {
        var sim = _world.Sim;

        foreach (string id in PartCatalog.BuildOrder)
        {
            var result = sim.Assembly.InstallAndSecure(id, out string message);
            if (result != AssemblyResult.Ok)
            {
                _failures.Add($"could not fit {id}: {message}");
                continue;
            }

            var part = _world.Parts[id];
            var mount = _world.Car.MountNodeFor(part.Def.MountId);
            if (mount == null)
            {
                _failures.Add($"no mount node for {part.Def.MountId}");
                continue;
            }
            part.AttachToMount(mount);
        }

        sim.Fluids.Pour(FluidKind.Petrol, FluidTanks.FuelCapacityL);
        sim.Fluids.Pour(FluidKind.Oil, FluidTanks.OilCapacityL);
        sim.Fluids.Pour(FluidKind.Coolant, FluidTanks.CoolantCapacityL);
        sim.Fluids.BatteryCharge = 1f;

        _world.Car.OnAssemblyChanged();

        float expected = AssemblyModel.ChassisMassKg;
        foreach (string id in PartCatalog.BuildOrder) expected += sim.Catalog.Get(id).MassKg;
        Near(expected, _world.Car.Mass, 0.5f, "mass after building it");

        var com = sim.Assembly.CenterOfMassLocal;
        Less(0f, com.Z, "a fitted engine pulls the balance forward");
        True(sim.RollsOnFourWheels, "four wheels bolted on");
        Equal(0, CountList(sim.MissingForEngine()), "nothing missing for the engine");
        True(sim.DrivetrainComplete, "clutch, gearbox and driveshaft all bolted on");
        Equal(0, CountList(sim.MissingForDrive()), "nothing missing to drive");
    }

    private void CheckItStandsOnItsWheels()
    {
        var car = _world.Car;
        Equal(4, car.GroundedWheelCount(), "all four wheels are carrying it");
        Greater(_chassisRestY + 0.12f, car.GlobalPosition.Y,
                $"the suspension lifted it off the floor ({_chassisRestY:0.00} m to {car.GlobalPosition.Y:0.00} m)");
        Less(0.25f, Mathf.Abs(car.LinearVelocity.Length()), "and it is sitting still");
    }

    private void StartCranking()
    {
        var controls = _world.Car.Controls;
        _world.Sim.Engine.IgnitionOn = true;
        controls.Starter = true;
        controls.Clutch = 1f;
        controls.Handbrake = false;
    }

    private void CheckItDroveOut()
    {
        var car = _world.Car;
        float travelled = _startZ - car.GlobalPosition.Z;   // forward is -Z

        True(_world.Sim.Engine.Running, $"still running — '{_world.Sim.Engine.Status}'");
        Greater(2.0f, travelled, $"it drove forward ({travelled:0.00} m)");
        Greater(6.0f, _peakSpeed, $"it got up to speed ({_peakSpeed:0.0} km/h peak)");
        Less(0.35f, Mathf.Abs(car.GlobalPosition.X), $"it tracked straight (drifted {car.GlobalPosition.X:0.00} m)");
        Greater(0.3f, _world.Sim.Engine.OilPressureBar, "oil pressure while driving");
        True(_world.Sim.CanDrive, "running, bolted together and on its wheels");
        Greater(0f, _world.Sim.Drivetrain.ClutchWear,
                $"pulling away burned some lining ({_world.Sim.Drivetrain.ClutchWear * 100f:0.00}%)");
        Less(0.05f, _world.Sim.Drivetrain.ClutchWear, "but nowhere near ruining it");
        False(_world.Sim.Engine.Seized, "nothing seized");
        Equal(4, car.GroundedWheelCount(), "it stayed on all four wheels");
    }

    private void Finish()
    {
        Console.WriteLine();
        if (_failures.Count == 0)
        {
            Console.WriteLine("PASS  the car was built, started and driven out of the garage");
            GetTree().Quit(0);
            return;
        }

        Console.WriteLine($"FAIL  {_failures.Count} checks");
        foreach (string f in _failures) Console.WriteLine($"  - {f}");
        GetTree().Quit(1);
    }

    // ------------------------------------------------------------- assertions

    private static int CountList(IEnumerable<string> items)
    {
        int n = 0;
        foreach (string _ in items) n++;
        return n;
    }

    private void Pass(string what) => Console.WriteLine($"  ok  {what}");

    private void Record(bool ok, string what, string detail)
    {
        if (ok) Pass(what);
        else _failures.Add(detail);
    }

    private void True(bool value, string what) => Record(value, what, $"expected true — {what}");
    private void False(bool value, string what) => Record(!value, what, $"expected false — {what}");

    private void Equal<T>(T expected, T actual, string what) =>
        Record(EqualityComparer<T>.Default.Equals(expected, actual), what,
               $"{what}: expected {expected}, got {actual}");

    private void Near(float expected, float actual, float tolerance, string what) =>
        Record(Mathf.Abs(expected - actual) <= tolerance, what,
               $"{what}: expected {expected} +/- {tolerance}, got {actual}");

    private void Greater(float threshold, float actual, string what) =>
        Record(actual > threshold, what, $"{what}: expected > {threshold}, got {actual}");

    private void Less(float threshold, float actual, string what) =>
        Record(actual < threshold, what, $"{what}: expected < {threshold}, got {actual}");
}
