using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Game;

/// <summary>
/// The car body. It owns the mount points, the four corners and the driving
/// controls, and it re-reads its own mass and balance from the simulation every
/// time a part goes on or comes off — so a car with no engine really is light at
/// the nose, and one with no wheels really does sit on its chassis.
/// </summary>
public partial class CarNode : RigidBody3D
{
    public const float WheelRadius = 0.29f;

    private static readonly Vector3 ChassisSize = new(1.30f, 0.52f, 3.70f);
    private static readonly Vector3 ChassisCentre = new(0f, 0.62f, 0.05f);

    public CarSim Sim { get; private set; }
    public bool DriverSeated { get; set; }
    public Node3D DriverView { get; private set; }
    public Node3D ExitPoint { get; private set; }
    public SeatNode Seat { get; private set; }

    private readonly Dictionary<string, Node3D> _mountNodes = new();
    private readonly Dictionary<string, PartNode> _parts = new();
    private readonly List<RaycastWheel> _wheels = new();

    private float _steerAngle;
    private float _airDrag = 0.42f;

    /// <summary>The pedals and the key, as data.</summary>
    public DriverControls Controls { get; } = new();

    /// <summary>
    /// When set, <see cref="Controls"/> is left alone instead of being refilled
    /// from the keyboard each tick.
    /// </summary>
    public bool ExternalControls { get; set; }

    public float ForwardSpeedMs => LinearVelocity.Dot(-GlobalTransform.Basis.Z);
    public float SpeedKmh => LinearVelocity.Length() * 3.6f;

    public static CarNode Create(CarSim sim)
    {
        var car = new CarNode
        {
            Name = "Car",
            Sim = sim,
            CollisionLayer = Layers.Car,
            CollisionMask = Layers.CarMask,
            CenterOfMassMode = CenterOfMassModeEnum.Custom,
            ContinuousCd = true,
            CanSleep = false,
            AngularDamp = 0.6f,
        };

        car.AddChild(Build.Box(ChassisSize, Build.Material("7d4a3a", 0.55f, 0.35f), "Chassis"));
        var shape = Build.BoxShape(ChassisSize, "ChassisShape");
        shape.Position = Vector3.Zero;
        car.AddChild(shape);
        foreach (Node child in car.GetChildren())
            if (child is Node3D n3 && (n3.Name == "Chassis" || n3.Name == "ChassisShape"))
                n3.Position = ChassisCentre;

        // A cabin block, purely so the thing reads as a car from the outside.
        var cabin = Build.Box(new Vector3(1.22f, 0.5f, 1.5f), Build.Material("6b3f32", 0.55f, 0.3f), "Cabin");
        cabin.Position = new Vector3(0f, 1.12f, 0.45f);
        car.AddChild(cabin);

        car.BuildWheels(sim);
        car.BuildMounts(sim);
        car.BuildFillers(sim);
        car.BuildCockpit();
        car.OnAssemblyChanged();
        return car;
    }

    private void BuildWheels(CarSim sim)
    {
        (string part, string mount, bool driven, bool steers)[] corners =
        {
            (Ids.WheelFl, "hub_fl", false, true),
            (Ids.WheelFr, "hub_fr", false, true),
            (Ids.WheelRl, "hub_rl", true, false),
            (Ids.WheelRr, "hub_rr", true, false),
        };

        foreach (var (partId, mountId, driven, steers) in corners)
        {
            var mount = sim.Catalog.GetMount(mountId);
            var hub = new Vector3(mount.LocalPosition.X, mount.LocalPosition.Y, mount.LocalPosition.Z);

            var wheel = RaycastWheel.Create(partId, hub, driven, steers);
            wheel.Radius = WheelRadius;
            AddChild(wheel);
            wheel.Attach(this);
            _wheels.Add(wheel);

            // The mount marker rides on the suspension, so the wheel you bolt on
            // moves with the strut instead of floating at its design height.
            var marker = MountNode.Create(mount, sim.Catalog.Get(partId), sim, this);
            marker.Position = Vector3.Zero;
            wheel.Anchor.AddChild(marker);
            _mountNodes[mountId] = marker;
        }
    }

    private void BuildMounts(CarSim sim)
    {
        foreach (var def in sim.Catalog.All)
        {
            if (def.Kind == PartKind.Running) continue;   // wheels are handled with the struts
            var mount = sim.Catalog.GetMount(def.MountId);
            var marker = MountNode.Create(mount, def, sim, this);
            AddChild(marker);
            _mountNodes[def.MountId] = marker;
        }
    }

    private void BuildFillers(CarSim sim)
    {
        AddChild(FillerNode.Create(FluidKind.Oil, "oil filler", Ids.EngineBlock,
            new Vector3(-0.22f, 0.96f, -1.10f), sim));
        AddChild(FillerNode.Create(FluidKind.Coolant, "radiator cap", Ids.Radiator,
            new Vector3(0f, 0.86f, -1.78f), sim));
        AddChild(FillerNode.Create(FluidKind.Petrol, "fuel filler", null,
            new Vector3(0.68f, 0.92f, 1.55f), sim));
    }

    private void BuildCockpit()
    {
        Seat = SeatNode.Create(new Vector3(-0.34f, 1.06f, 0.30f));
        AddChild(Seat);

        DriverView = new Node3D { Name = "DriverView", Position = new Vector3(-0.34f, 1.42f, 0.20f) };
        AddChild(DriverView);

        ExitPoint = new Node3D { Name = "ExitPoint", Position = new Vector3(-1.35f, 1.0f, 0.30f) };
        AddChild(ExitPoint);
    }

    public Node3D MountNodeFor(string mountId) =>
        _mountNodes.TryGetValue(mountId, out var node) ? node : null;

    public void RegisterPart(PartNode part) => _parts[part.Def.Id] = part;

    public PartNode PartFor(string id) => _parts.TryGetValue(id, out var p) ? p : null;

    /// <summary>
    /// Re-reads mass and balance from the model. Called whenever a part is fitted
    /// or removed — there is nowhere else the body's properties come from.
    /// </summary>
    public void OnAssemblyChanged()
    {
        Mass = Sim.Assembly.TotalMassKg;
        var com = Sim.Assembly.CenterOfMassLocal;
        CenterOfMass = new Vector3(com.X, com.Y, com.Z);
    }

    public override void _Process(double delta)
    {
        if (!DriverSeated) return;

        if (Input.IsActionJustPressed(GameInput.Ignition))
            Sim.Engine.IgnitionOn = !Sim.Engine.IgnitionOn;

        if (Input.IsActionJustPressed(GameInput.ShiftUp)) Sim.Drivetrain.ShiftUp();
        if (Input.IsActionJustPressed(GameInput.ShiftDown)) Sim.Drivetrain.ShiftDown();
        if (Input.IsActionJustPressed(GameInput.Neutral)) Sim.Drivetrain.TryShift(Drivetrain.GearNeutral);

        float chokeStep = (float)delta * 1.2f;
        if (Input.IsActionPressed(GameInput.ChokeOut))
            Sim.Engine.Choke = Mathf.Min(1f, Sim.Engine.Choke + chokeStep);
        if (Input.IsActionPressed(GameInput.ChokeIn))
            Sim.Engine.Choke = Mathf.Max(0f, Sim.Engine.Choke - chokeStep);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (!ExternalControls)
        {
            if (DriverSeated)
            {
                Controls.Throttle = Input.GetActionStrength(GameInput.Throttle);
                Controls.Brake = Input.GetActionStrength(GameInput.Brake);
                Controls.Steer = Input.GetActionStrength(GameInput.SteerRight)
                                 - Input.GetActionStrength(GameInput.SteerLeft);
                Controls.Clutch = Input.GetActionStrength(GameInput.Clutch);
                Controls.Handbrake = Input.IsActionPressed(GameInput.Handbrake);
                Controls.Starter = Input.IsActionPressed(GameInput.Starter);
            }
            else
            {
                Controls.Reset();
            }
        }

        float throttle = Controls.Throttle;
        float brake = Controls.Brake;
        float steer = Controls.Steer;
        float clutch = Controls.Clutch;
        bool handbrake = Controls.Handbrake;

        Sim.Engine.Throttle = throttle;
        Sim.Engine.StarterEngaged = Controls.Starter;
        Sim.Drivetrain.ClutchPedal = clutch;

        // ----- steering: less lock the faster it goes, so it is not twitchy
        float speed = Mathf.Abs(ForwardSpeedMs);
        float maxSteer = Mathf.DegToRad(33f) * (1f - Mathf.Clamp(speed / 36f, 0f, 0.62f));
        _steerAngle = Mathf.Lerp(_steerAngle, steer * maxSteer, Mathf.Min(1f, dt * 7f));

        // ----- the driven axle's current speed, which is what the clutch couples to
        float omegaSum = 0f;
        int drivenCount = 0;
        foreach (var wheel in _wheels)
        {
            if (!wheel.Driven || !Sim.Assembly.IsFunctional(wheel.PartId)) continue;
            omegaSum += wheel.SpinRateRadPerSec;
            drivenCount++;
        }
        float drivenOmega = drivenCount > 0 ? omegaSum / drivenCount : 0f;

        Sim.Tick(dt, drivenOmega, Mathf.Abs(ForwardSpeedMs));

        // ----- hand the axle torque to the driven wheels that are actually there
        float perWheel = drivenCount > 0 ? Sim.AxleTorqueNm / drivenCount : 0f;

        foreach (var wheel in _wheels)
        {
            bool fitted = Sim.Assembly.IsFunctional(wheel.PartId);

            wheel.SteerAngle = wheel.Steers ? _steerAngle : 0f;
            wheel.DriveTorqueNm = wheel.Driven ? perWheel : 0f;
            wheel.BrakeTorqueNm = brake * (wheel.Driven ? 900f : 1500f)
                                  + (handbrake && wheel.Driven ? 2200f : 0f);

            wheel.Simulate(this, dt, fitted);

            var part = PartFor(wheel.PartId);
            if (part != null) part.SpinRate = fitted ? wheel.SpinRateRadPerSec : 0f;
        }

        // ----- aerodynamic drag, enough to give it a terminal speed
        Vector3 v = LinearVelocity;
        if (v.LengthSquared() > 0.01f)
            ApplyCentralForce(-v.Normalized() * v.LengthSquared() * _airDrag);
    }

    /// <summary>Corners with a bolted wheel that is currently touching the ground.</summary>
    public int GroundedWheelCount()
    {
        int n = 0;
        foreach (var wheel in _wheels)
            if (wheel.Grounded && Sim.Assembly.IsFunctional(wheel.PartId)) n++;
        return n;
    }
}
