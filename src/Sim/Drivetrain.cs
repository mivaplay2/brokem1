using System;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Sim;

/// <summary>
/// Clutch, five-speed box and driveshaft — all three of them parts you have to
/// fit and bolt down. The clutch is a slipping torsional coupling, which is what
/// makes dumping it at idle stall the engine instead of teleporting the car
/// forward, and its lining wears from the energy it dissipates while slipping,
/// so riding it has a cost.
/// </summary>
public sealed class Drivetrain
{
    public const int GearReverse = 0;
    public const int GearNeutral = 1;
    public const int GearTop = 6;

    /// <summary>Index 0 is reverse, 1 is neutral, 2..6 are first through fifth.</summary>
    public static readonly float[] GearRatios = { -3.55f, 0f, 3.45f, 1.94f, 1.29f, 0.97f, 0.76f };

    public static readonly string[] GearNames = { "R", "N", "1", "2", "3", "4", "5" };

    /// <summary>
    /// Wear per joule the clutch burns off while slipping. A pull-away costs a few
    /// kilojoules; sitting on a slipping clutch costs it by the minute.
    /// </summary>
    public const float ClutchWearPerJoule = 1.2e-7f;

    /// <summary>
    /// Wear stops here rather than at 1. A part at full wear becomes unfittable,
    /// and with no parts shop in the slice that would be a dead end — so a ruined
    /// clutch slips uselessly but can still be taken off and put back on.
    /// </summary>
    public const float MaxClutchWear = 0.95f;

    private readonly AssemblyModel _asm;

    public int Gear = GearNeutral;
    public float FinalDrive = 3.90f;

    /// <summary>0 = released, 1 = fully pressed to the floor.</summary>
    public float ClutchPedal;

    public float ClutchStiffness = 0.35f;
    public float ClutchMaxTorqueNm = 150f;

    /// <summary>Pedal travel above which the box will accept a new gear.</summary>
    public float ShiftPedalThreshold = 0.6f;

    public float AxleTorqueNm { get; private set; }
    public float ClutchSlipRadPerSec { get; private set; }
    public string LastShiftMessage { get; private set; } = "";

    /// <summary>Why no drive is reaching the wheels, or null when it is.</summary>
    public string Blocker { get; private set; }

    public Drivetrain(AssemblyModel assembly)
    {
        _asm = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public float Ratio => GearRatios[Gear] * FinalDrive;
    public bool InGear => Gear != GearNeutral && MathF.Abs(GearRatios[Gear]) > 0.001f;
    public float Engagement => Math.Clamp(1f - ClutchPedal, 0f, 1f);

    public bool ClutchFitted => _asm.IsFunctional(Ids.Clutch);
    public bool GearboxFitted => _asm.IsFunctional(Ids.Gearbox);
    public bool DriveshaftFitted => _asm.IsFunctional(Ids.Driveshaft);
    public bool Complete => ClutchFitted && GearboxFitted && DriveshaftFitted;

    public float ClutchWear => _asm.TryGetState(Ids.Clutch, out var state) ? state.Wear : 0f;

    /// <summary>How much torque the lining can still hold before it just slips.</summary>
    public float ClutchCapacityNm => ClutchMaxTorqueNm * (1f - 0.85f * ClutchWear);

    /// <summary>A dash with no gearbox, because there is no lever to read.</summary>
    public string GearLabel => GearboxFitted ? GearNames[Gear] : "-";

    public bool TryShift(int gear)
    {
        if (!GearboxFitted)
        {
            LastShiftMessage = _asm.IsInstalled(Ids.Gearbox)
                ? "the gearbox is not bolted down"
                : "there is no gearbox in it";
            return false;
        }
        if (gear < GearReverse || gear > GearTop)
        {
            LastShiftMessage = "no such gear";
            return false;
        }
        if (gear != GearNeutral && ClutchPedal < ShiftPedalThreshold)
        {
            LastShiftMessage = "clutch!";
            return false;
        }
        Gear = gear;
        LastShiftMessage = $"gear {GearNames[gear]}";
        return true;
    }

    public bool ShiftUp() => TryShift(Math.Min(GearTop, Gear + 1));
    public bool ShiftDown() => TryShift(Math.Max(GearReverse, Gear - 1));

    /// <summary>
    /// Couples engine and driven axle for one tick. Writes the reaction torque onto
    /// <paramref name="engine"/> and returns the torque to hand the wheels. Every
    /// link in the chain has to be fitted and bolted, or nothing gets through.
    /// </summary>
    public float Transmit(EngineSim engine, float drivenWheelOmega, float dt)
    {
        Blocker = null;

        if (!GearboxFitted)
        {
            Blocker = _asm.IsInstalled(Ids.Gearbox) ? "gearbox not bolted down" : "no gearbox";
            Gear = GearNeutral;                 // nothing in there to hold a gear
            return Disengage(engine);
        }
        if (!ClutchFitted)
        {
            Blocker = _asm.IsInstalled(Ids.Clutch) ? "clutch not bolted down" : "no clutch";
            return Disengage(engine);
        }
        if (!DriveshaftFitted)
        {
            Blocker = _asm.IsInstalled(Ids.Driveshaft) ? "driveshaft not bolted down" : "no driveshaft";
            return Disengage(engine);
        }

        if (engine.Seized)
        {
            Blocker = "engine seized";
            return Disengage(engine);
        }
        if (!InGear)
        {
            Blocker = "in neutral";
            return Disengage(engine);
        }
        if (Engagement <= 0.001f)
        {
            Blocker = "clutch down";
            return Disengage(engine);
        }

        float ratio = Ratio;
        float engineOmega = engine.Rpm * MathF.Tau / 60f;
        float slip = engineOmega - drivenWheelOmega * ratio;
        ClutchSlipRadPerSec = slip;

        float capacity = ClutchCapacityNm;
        float coupling = Math.Clamp(slip * ClutchStiffness, -capacity, capacity) * Engagement;

        engine.LoadTorqueNm = coupling;
        AxleTorqueNm = coupling * ratio;

        WearTheLining(coupling, slip, dt);

        if (ClutchWear >= MaxClutchWear) Blocker = "clutch worn out";
        return AxleTorqueNm;
    }

    /// <summary>The lining loses material in proportion to the power it is burning.</summary>
    private void WearTheLining(float coupling, float slip, float dt)
    {
        float slipPowerW = MathF.Abs(coupling * slip);
        if (slipPowerW < 1f) return;
        if (!_asm.TryGetState(Ids.Clutch, out var clutch)) return;

        clutch.Wear = Math.Min(MaxClutchWear, clutch.Wear + slipPowerW * ClutchWearPerJoule * dt);
    }

    private float Disengage(EngineSim engine)
    {
        engine.LoadTorqueNm = 0f;
        AxleTorqueNm = 0f;
        ClutchSlipRadPerSec = 0f;
        return 0f;
    }
}
