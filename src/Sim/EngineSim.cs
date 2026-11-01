using System;

namespace MyAutumnCar.Sim;

/// <summary>
/// A 1.6 litre carburetted inline four, simulated as a single rotating inertia.
/// It knows nothing about meshes or physics bodies: it asks the
/// <see cref="AssemblyModel"/> what is bolted on and the <see cref="FluidTanks"/>
/// what has been poured in, and from that decides whether it runs, how much it
/// hurts itself and what torque comes out of the flywheel.
/// </summary>
public sealed class EngineSim
{
    // ------------------------------------------------------------- tuning
    public float IdleRpm = 880f;
    public float StallRpm = 330f;
    public float RedlineRpm = 6400f;
    public float PeakTorqueNm = 118f;
    public float PeakTorqueRpm = 3200f;
    public float TorqueSpreadRpm = 3400f;
    public float InertiaKgM2 = 0.24f;

    public float StarterTorqueNm = 95f;
    public float StarterCeilingRpm = 420f;
    public float StarterDrainPerSec = 0.045f;
    public float StarterMinCharge = 0.15f;

    /// <summary>Idle screw. Too low and it dies at the lights; too high and it never settles.</summary>
    public float IdleBypass = 0.085f;

    public float FuelBurnCoef = 1.8e-6f;
    public float IgniteRpm = 360f;
    public float IgniteMixtureMin = 0.45f;

    public float AmbientTempC = 9f;
    public float OverheatTempC = 117f;
    public float HeatScale = 0.31f;

    public float OilPressureWarnBar = 0.5f;
    public float OilDamageBar = 0.45f;

    // --------------------------------------------------------------- state
    public float Rpm;
    public float Throttle;
    public float Choke;
    public bool IgnitionOn;
    public bool StarterEngaged;
    public bool Running;
    public bool Seized;

    public float OilPressureBar;
    public float CoolantTempC = 9f;
    public float PistonWear;
    public float HeadGasketWear;

    /// <summary>Written by the drivetrain every tick before <see cref="Update"/>.</summary>
    public float LoadTorqueNm;

    public float CombustionTorqueNm { get; private set; }
    public string Status { get; private set; } = "off";

    /// <summary>Keeps the reason it died on the gauge until the starter is turned again.</summary>
    private bool _stopLatched;

    public bool OilWarning => Running && OilPressureBar < OilPressureWarnBar;
    public bool TempWarning => CoolantTempC > 105f;
    public float RpmNormalized => Math.Clamp(Rpm / RedlineRpm, 0f, 1.1f);

    public void ResetToCold()
    {
        Rpm = 0f;
        Throttle = 0f;
        Choke = 0f;
        IgnitionOn = false;
        StarterEngaged = false;
        Running = false;
        Seized = false;
        OilPressureBar = 0f;
        CoolantTempC = AmbientTempC;
        PistonWear = 0f;
        HeadGasketWear = 0f;
        LoadTorqueNm = 0f;
        Status = "off";
        _stopLatched = false;
    }

    /// <summary>Flywheel torque at a given speed, before throttle and condition.</summary>
    public float TorqueCurveNm(float rpm)
    {
        if (rpm < 200f) return 0f;
        float x = (rpm - PeakTorqueRpm) / TorqueSpreadRpm;
        float f = 1f - x * x * 0.95f;
        if (rpm > RedlineRpm) f *= Math.Max(0f, 1f - (rpm - RedlineRpm) / 400f);
        return PeakTorqueNm * Math.Clamp(f, 0f, 1f);
    }

    /// <summary>
    /// How well the mixture burns. A cold engine wants the choke out and a warm
    /// one drowns on it, which is the whole reason the choke is a control.
    /// </summary>
    public float MixtureEfficiency()
    {
        float wanted = Math.Clamp((35f - CoolantTempC) / 35f, 0f, 1f);
        float error = Math.Abs(Choke - wanted);
        return Math.Clamp(1f - error * 0.85f, 0.12f, 1f);
    }

    /// <summary>Extra throttle the idle screw lets past when the engine sags.</summary>
    private float IdleAssist()
    {
        float sag = Math.Clamp((IdleRpm + 150f - Rpm) / 400f, 0f, 1f);
        return IdleBypass * sag;
    }

    private float FrictionTorqueNm()
    {
        float f = 2.2f + 0.0032f * Rpm;
        if (!Running) f += 0.0008f * Rpm;   // pumping losses while cranking
        return f;
    }

    private void Stop(string reason)
    {
        Running = false;
        Status = reason;
        _stopLatched = true;
    }

    public void Update(float dt, AssemblyModel asm, FluidTanks fluids, float airspeedMs)
    {
        if (StarterEngaged) _stopLatched = false;

        bool mechanicalOk =
            asm.IsFunctional(PartCatalog.Ids.EngineBlock) &&
            asm.IsFunctional(PartCatalog.Ids.CylinderHead) &&
            asm.IsFunctional(PartCatalog.Ids.Carburetor);

        bool sparkOk =
            IgnitionOn &&
            asm.IsFunctional(PartCatalog.Ids.SparkPlugs) &&
            asm.IsFunctional(PartCatalog.Ids.Battery) &&
            fluids.BatteryCharge > 0.05f;

        bool fuelOk = fluids.FuelL > 0.005f;

        if (Seized)
        {
            Running = false;
            CombustionTorqueNm = 0f;
            Rpm = Math.Max(0f, Rpm - 5000f * dt);
            OilPressureBar = 0f;
            Status = "seized";
            CoolDown(dt, asm, fluids, airspeedMs);
            return;
        }

        float omega = Rpm * MathF.Tau / 60f;
        float torque = 0f;

        // ----- starter motor
        bool cranking = false;
        if (StarterEngaged && !Running && asm.IsFunctional(PartCatalog.Ids.Battery)
            && fluids.BatteryCharge > StarterMinCharge && mechanicalOk)
        {
            float fall = Math.Clamp(1f - Rpm / StarterCeilingRpm, 0f, 1f);
            torque += StarterTorqueNm * fall;
            fluids.BatteryCharge = Math.Max(0f, fluids.BatteryCharge - StarterDrainPerSec * dt);
            cranking = true;
        }

        // ----- combustion
        float mixture = MixtureEfficiency();
        float effThrottle = Math.Max(Throttle, IdleAssist());

        if (!Running && sparkOk && fuelOk && mechanicalOk
            && Rpm > IgniteRpm && mixture > IgniteMixtureMin)
        {
            Running = true;
            Status = "running";
            _stopLatched = false;
        }

        CombustionTorqueNm = 0f;
        if (Running && Rpm < RedlineRpm + 120f)
        {
            float condition = (1f - 0.65f * PistonWear) * (1f - 0.55f * HeadGasketWear);
            CombustionTorqueNm = TorqueCurveNm(Rpm)
                                 * (0.06f + 0.94f * effThrottle)
                                 * mixture
                                 * Math.Max(0.05f, condition);
            torque += CombustionTorqueNm;
        }

        // ----- losses and load
        torque -= FrictionTorqueNm();
        torque -= LoadTorqueNm;

        omega += torque / Math.Max(0.01f, InertiaKgM2) * dt;
        Rpm = Math.Clamp(omega * 60f / MathF.Tau, 0f, RedlineRpm + 300f);

        // ----- shutdown conditions
        if (Running)
        {
            if (!fuelOk) Stop("out of petrol");
            else if (!sparkOk) Stop(IgnitionOn ? "no spark" : "ignition off");
            else if (!mechanicalOk) Stop("engine not complete");
            else if (Rpm < StallRpm && !cranking) Stop("stalled");
        }
        else if (cranking)
        {
            Status = mixture <= IgniteMixtureMin
                ? (Choke < 0.3f ? "cranking — try the choke" : "cranking — too much choke")
                : "cranking";
        }
        else if (!_stopLatched)
        {
            if (!mechanicalOk) Status = DescribeIncomplete(asm);
            else if (!IgnitionOn) Status = "ignition off";
            else if (!fuelOk) Status = "no petrol";
            else if (!sparkOk) Status = "no spark";
            else if (Rpm < 5f) Status = "off";
        }

        // ----- consumables
        if (Running)
            fluids.Drain(FluidKind.Petrol, FuelBurnCoef * Rpm * (0.2f + 0.8f * effThrottle) * dt);

        if (Running) fluids.BatteryCharge = Math.Min(1f, fluids.BatteryCharge + 0.02f * dt);
        else if (IgnitionOn) fluids.BatteryCharge = Math.Max(0f, fluids.BatteryCharge - 0.002f * dt);

        // ----- oil
        if (Running || cranking)
        {
            float pickup = fluids.OilL <= 0.5f ? 0f : Math.Clamp((fluids.OilL - 0.5f) / 1.2f, 0f, 1f);
            OilPressureBar = pickup * (0.7f + Rpm / 3600f) * (1f - 0.5f * PistonWear);
        }
        else
        {
            OilPressureBar = 0f;
        }

        if (Running && OilPressureBar < OilDamageBar)
        {
            PistonWear = Math.Min(1f, PistonWear + dt * 0.05f * (0.4f + Rpm / 2500f));
            if (PistonWear >= 1f)
            {
                Seized = true;
                Running = false;
                Status = "seized — no oil pressure";
            }
        }

        CoolDown(dt, asm, fluids, airspeedMs);
    }

    private void CoolDown(float dt, AssemblyModel asm, FluidTanks fluids, float airspeedMs)
    {
        float heat = Running
            ? (0.0055f * Rpm * (0.25f + 0.75f * Math.Max(Throttle, IdleAssist())) + 3.5f) * HeatScale
            : 0f;

        bool coolingOk = asm.IsFunctional(PartCatalog.Ids.Radiator) && fluids.CoolantL > 1.2f;
        float k = coolingOk ? 0.02f + 0.0035f * Math.Max(0f, airspeedMs) : 0.004f;

        CoolantTempC += (heat - k * (CoolantTempC - AmbientTempC)) * dt;
        CoolantTempC = Math.Clamp(CoolantTempC, AmbientTempC - 2f, 190f);

        if (CoolantTempC > OverheatTempC)
        {
            HeadGasketWear = Math.Min(1f, HeadGasketWear + 0.015f * dt);
            fluids.Drain(FluidKind.Coolant, 0.02f * dt);
            if (Running) Status = "overheating";
        }
    }

    private static string DescribeIncomplete(AssemblyModel asm)
    {
        foreach (string id in PartCatalog.EngineCritical)
            if (!asm.IsFunctional(id))
                return $"missing: {asm.Catalog.Get(id).DisplayName}";
        return "engine not complete";
    }
}
