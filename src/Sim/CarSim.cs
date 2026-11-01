using System.Collections.Generic;
using System.Linq;

namespace MyAutumnCar.Sim;

/// <summary>
/// The whole car as one simulation object: what is bolted on, what is poured in,
/// the engine and the gearbox. The view layer owns a single instance of this and
/// ticks it on the physics step.
/// </summary>
public sealed class CarSim
{
    public PartCatalog Catalog { get; }
    public AssemblyModel Assembly { get; }
    public EngineSim Engine { get; } = new();
    public Drivetrain Drivetrain { get; }
    public FluidTanks Fluids { get; } = FluidTanks.Empty();

    public float AxleTorqueNm { get; private set; }
    public double PlayTimeSeconds { get; set; }

    public CarSim() : this(PartCatalog.CreateDefault()) { }

    public CarSim(PartCatalog catalog)
    {
        Catalog = catalog;
        Assembly = new AssemblyModel(catalog);
        Drivetrain = new Drivetrain(Assembly);
        Engine.ResetToCold();
    }

    /// <summary>All four corners fitted and torqued.</summary>
    public bool RollsOnFourWheels => PartCatalog.Wheels.All(Assembly.IsFunctional);

    public IEnumerable<string> MissingForEngine() => Assembly.MissingForEngine();

    /// <summary>Clutch, gearbox and driveshaft all fitted and bolted down.</summary>
    public bool DrivetrainComplete => Drivetrain.Complete;

    /// <summary>Transmission parts that are not yet fitted and bolted, in build order.</summary>
    public IEnumerable<string> MissingForDrive() =>
        PartCatalog.Transmission.Concat(PartCatalog.Wheels).Where(id => !Assembly.IsFunctional(id));

    /// <summary>Everything that has to be true before the car can move under its own power.</summary>
    public bool CanDrive => Engine.Running && DrivetrainComplete && RollsOnFourWheels;

    /// <summary>
    /// One simulation step. <paramref name="drivenWheelOmega"/> is the mean angular
    /// speed of the driven wheels in rad/s, <paramref name="airspeedMs"/> the car's
    /// forward speed, used for radiator airflow.
    /// </summary>
    public void Tick(float dt, float drivenWheelOmega, float airspeedMs)
    {
        AxleTorqueNm = Drivetrain.Transmit(Engine, drivenWheelOmega, dt);
        Engine.Update(dt, Assembly, Fluids, airspeedMs);
        PlayTimeSeconds += dt;
    }
}
