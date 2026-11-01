using System;

namespace MyAutumnCar.Sim;

/// <summary>
/// Single dry clutch, five-speed box and a live rear axle. The clutch is modelled
/// as a slipping torsional coupling, which is what makes dumping it at idle stall
/// the engine instead of teleporting the car forward.
/// </summary>
public sealed class Drivetrain
{
    public const int GearReverse = 0;
    public const int GearNeutral = 1;
    public const int GearTop = 6;

    /// <summary>Index 0 is reverse, 1 is neutral, 2..6 are first through fifth.</summary>
    public static readonly float[] GearRatios = { -3.55f, 0f, 3.45f, 1.94f, 1.29f, 0.97f, 0.76f };

    public static readonly string[] GearNames = { "R", "N", "1", "2", "3", "4", "5" };

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

    public float Ratio => GearRatios[Gear] * FinalDrive;
    public bool InGear => Gear != GearNeutral && MathF.Abs(GearRatios[Gear]) > 0.001f;
    public float Engagement => Math.Clamp(1f - ClutchPedal, 0f, 1f);
    public string GearLabel => GearNames[Gear];

    public bool TryShift(int gear)
    {
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
    /// Couples engine and driven axle for one tick. Writes the reaction torque
    /// onto <paramref name="engine"/> and returns the torque to hand the wheels.
    /// </summary>
    public float Transmit(EngineSim engine, float drivenWheelOmega)
    {
        if (!InGear || Engagement <= 0.001f || engine.Seized)
        {
            engine.LoadTorqueNm = 0f;
            AxleTorqueNm = 0f;
            ClutchSlipRadPerSec = 0f;
            return 0f;
        }

        float ratio = Ratio;
        float engineOmega = engine.Rpm * MathF.Tau / 60f;
        float targetEngineOmega = drivenWheelOmega * ratio;
        float slip = engineOmega - targetEngineOmega;
        ClutchSlipRadPerSec = slip;

        float coupling = Math.Clamp(slip * ClutchStiffness, -ClutchMaxTorqueNm, ClutchMaxTorqueNm)
                         * Engagement;

        engine.LoadTorqueNm = coupling;
        AxleTorqueNm = coupling * ratio;
        return AxleTorqueNm;
    }
}
