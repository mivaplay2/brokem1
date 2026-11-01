using System;
using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Game;

/// <summary>
/// Builds the real world, bolts the real car together through the real model and
/// drives it with no one at the keyboard. The simulation tests cover the
/// mechanics; this covers the half the simulation cannot see — mounts,
/// suspension, tyre forces, mass and balance behaving in the scene — and the
/// three behaviours that are supposed to fall out of giving the wheels their own
/// rotation rather than being written by hand: wheelspin, engine braking and
/// lockup.
///
/// Run with:  godot --headless --path . -- --smoke
/// Exits 0 when every check passes, 1 otherwise.
/// </summary>
public partial class HeadlessSmokeTest : Node3D
{
    private const int Hz = 60;

    private const int FrameBareChassis = 1 * Hz;
    private const int FrameStanding = 3 * Hz;
    private const int FrameShift = 3 * Hz + 10;
    private const int FrameRevIt = 8 * Hz;
    private const int FrameDumpClutch = 9 * Hz + 30;
    private const int FrameCheckLaunch = 11 * Hz;
    private const int FrameCheckDrive = 20 * Hz;
    private const int FrameCheckEngineBraking = 26 * Hz;
    private const int FrameCheckLockup = 28 * Hz;
    private const int FrameCheckStopped = 31 * Hz;

    private GameWorld _world;
    private readonly List<string> _failures = new();
    private int _frame;

    private float _chassisRestY;
    private float _startZ;
    private float _peakSpeedKmh;
    private float _speedAtLiftKmh;

    private bool _sawWheelspin;
    private float _peakSlipRatio;
    private float _peakTyreOverspeedMs;

    private bool _sawLockup;
    private float _lockedAtKmh;

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

        if (_frame > FrameDumpClutch) _peakSpeedKmh = Mathf.Max(_peakSpeedKmh, car.SpeedKmh);
        ObserveLaunch(car);
        ObserveBraking(car);

        switch (_frame)
        {
            case FrameBareChassis:
                CheckBareChassis();
                BuildTheCar();
                break;

            case FrameStanding:
                CheckItStandsOnItsWheels();
                StartCranking();
                break;

            case FrameShift:
                True(sim.Drivetrain.TryShift(2), "first gear selected with the clutch down");
                break;

            case FrameRevIt:
                True(sim.Engine.Running, $"it started — status '{sim.Engine.Status}', {sim.Engine.Rpm:0} rpm");
                controls.Starter = false;
                controls.Throttle = 1f;          // revs first, clutch still down
                _startZ = car.GlobalPosition.Z;
                break;

            case FrameCheckLaunch:
                CheckLaunch();
                controls.Throttle = 0.6f;
                break;

            case FrameCheckDrive:
                CheckItDroveOut();
                _speedAtLiftKmh = car.SpeedKmh;
                controls.Throttle = 0f;          // off the throttle, still in gear
                break;

            case FrameCheckEngineBraking:
                CheckEngineBraking();
                controls.Brake = 1f;
                break;

            case FrameCheckLockup:
                CheckLockup();
                break;

            case FrameCheckStopped:
                Less(1.2f, car.SpeedKmh, $"the brakes stopped it ({car.SpeedKmh:0.0} km/h)");
                Finish();
                break;
        }

        // Drop the clutch in a fifth of a second, which is how you get wheelspin.
        if (_frame > FrameDumpClutch && _frame <= FrameDumpClutch + 12)
            controls.Clutch = Mathf.Clamp(1f - (_frame - FrameDumpClutch) / 12f, 0f, 1f);
    }

    /// <summary>
    /// Watches the pull-away rather than sampling one frame of it: the slip closes
    /// within a few ticks, so a single probe would be a timing coincidence.
    /// </summary>
    private void ObserveLaunch(CarNode car)
    {
        if (_frame <= FrameDumpClutch || _frame > FrameCheckLaunch) return;

        if (car.DrivenWheelsSpinning()) _sawWheelspin = true;

        foreach (var wheel in car.Wheels)
        {
            if (!wheel.Driven || !wheel.Grounded) continue;
            _peakSlipRatio = Mathf.Max(_peakSlipRatio, wheel.SlipRatio);
            _peakTyreOverspeedMs = Mathf.Max(_peakTyreOverspeedMs,
                                             wheel.TyreSurfaceSpeedMs - wheel.ForwardSpeedMs);
        }
    }

    private void ObserveBraking(CarNode car)
    {
        if (_frame <= FrameCheckEngineBraking || _frame > FrameCheckLockup) return;
        if (!car.AnyWheelLocked()) return;

        if (!_sawLockup) _lockedAtKmh = car.SpeedKmh;
        _sawLockup = true;
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
        Near(0f, car.DrivenWheelOmega(), 0.4f, "with the wheels barely turning as it settles");
    }

    private void StartCranking()
    {
        var controls = _world.Car.Controls;
        _world.Sim.Engine.IgnitionOn = true;
        controls.Starter = true;
        controls.Clutch = 1f;
        controls.Handbrake = false;
    }

    private void CheckLaunch()
    {
        var sim = _world.Sim;

        True(_sawWheelspin,
             $"dropping the clutch spun the driven wheels up (tyre ran {_peakTyreOverspeedMs:0.0} m/s " +
             $"faster than the road, peak slip ratio {_peakSlipRatio:0.00})");
        Greater(0.2f, _peakSlipRatio, "the driven tyre slipped rather than gripping perfectly");
        True(sim.Engine.Running, $"and it did not stall — '{sim.Engine.Status}', {sim.Engine.Rpm:0} rpm");
        Greater(0f, sim.Drivetrain.ClutchWear, $"the dump cost some lining ({sim.Drivetrain.ClutchWear * 100f:0.00}%)");
    }

    private void CheckItDroveOut()
    {
        var car = _world.Car;
        float travelled = _startZ - car.GlobalPosition.Z;   // forward is -Z

        True(_world.Sim.Engine.Running, $"still running — '{_world.Sim.Engine.Status}'");
        Greater(2.0f, travelled, $"it drove forward ({travelled:0.00} m)");
        Greater(6.0f, _peakSpeedKmh, $"it got up to speed ({_peakSpeedKmh:0.0} km/h peak)");
        // An open differential with a wheelspinning launch induces a little yaw.
        Less(travelled * 0.04f, Mathf.Abs(car.GlobalPosition.X),
             $"it tracked straight (drifted {car.GlobalPosition.X:0.00} m over {travelled:0.0} m)");
        Greater(0.3f, _world.Sim.Engine.OilPressureBar, "oil pressure while driving");
        True(_world.Sim.CanDrive, "running, bolted together and on its wheels");
        False(_world.Sim.Engine.Seized, "nothing seized");
        Equal(4, car.GroundedWheelCount(), "it stayed on all four wheels");
    }

    private void CheckEngineBraking()
    {
        var car = _world.Car;
        var sim = _world.Sim;

        float wheelOmega = Mathf.Abs(car.DrivenWheelOmega());
        float geared = wheelOmega * Mathf.Abs(sim.Drivetrain.Ratio) * 60f / Mathf.Tau;

        True(sim.Engine.Running, $"off the throttle in gear it keeps running — '{sim.Engine.Status}'");
        Near(geared, sim.Engine.Rpm, Mathf.Max(250f, geared * 0.15f),
             $"revs are tied to road speed through the gearing ({sim.Engine.Rpm:0} rpm against {geared:0} geared)");
        Less(0f, sim.Engine.LoadTorqueNm,
             $"the wheels are turning the engine, not the other way round ({sim.Engine.LoadTorqueNm:0.0} Nm)");
        Less(0f, sim.AxleTorqueNm, $"so the axle is being retarded ({sim.AxleTorqueNm:0} Nm)");
        Less(_speedAtLiftKmh - 1.5f, car.SpeedKmh,
             $"and it slowed on the engine alone ({_speedAtLiftKmh:0.0} to {car.SpeedKmh:0.0} km/h)");
    }

    private void CheckLockup()
    {
        True(_sawLockup, $"full brakes locked a wheel while it was still moving ({_lockedAtKmh:0.0} km/h)");
        Greater(1.5f, _lockedAtKmh, "and it was genuinely sliding, not stopped");
    }

    private void Finish()
    {
        Console.WriteLine();
        if (_failures.Count == 0)
        {
            Console.WriteLine("PASS  built, started, span the wheels up, drove out, braked on the engine and locked up");
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

    private void Record(bool ok, string what, string detail)
    {
        if (ok) Console.WriteLine($"  ok  {what}");
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
