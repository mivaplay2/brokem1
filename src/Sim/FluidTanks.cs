using System;

namespace MyAutumnCar.Sim;

public enum FluidKind
{
    Petrol,
    Oil,
    Coolant,
}

/// <summary>
/// Consumables. Oil is the one that punishes you: run the engine dry and the
/// pistons wear until it seizes for good.
/// </summary>
public sealed class FluidTanks
{
    public const float FuelCapacityL = 39f;
    public const float OilCapacityL = 4.2f;
    public const float CoolantCapacityL = 6.0f;

    /// <summary>Below this the oil pump cannot build pressure.</summary>
    public const float OilMinimumL = 1.4f;

    public float FuelL { get; set; }
    public float OilL { get; set; }
    public float CoolantL { get; set; }

    /// <summary>0..1 state of charge.</summary>
    public float BatteryCharge { get; set; } = 1f;

    public static FluidTanks Empty() => new() { FuelL = 0f, OilL = 0f, CoolantL = 0f, BatteryCharge = 0.92f };

    public float Capacity(FluidKind kind) => kind switch
    {
        FluidKind.Petrol => FuelCapacityL,
        FluidKind.Oil => OilCapacityL,
        FluidKind.Coolant => CoolantCapacityL,
        _ => 0f,
    };

    public float Level(FluidKind kind) => kind switch
    {
        FluidKind.Petrol => FuelL,
        FluidKind.Oil => OilL,
        FluidKind.Coolant => CoolantL,
        _ => 0f,
    };

    /// <summary>Pours up to <paramref name="litres"/> in and returns what actually fitted.</summary>
    public float Pour(FluidKind kind, float litres)
    {
        float room = MathF.Max(0f, Capacity(kind) - Level(kind));
        float added = MathF.Min(room, MathF.Max(0f, litres));
        switch (kind)
        {
            case FluidKind.Petrol: FuelL += added; break;
            case FluidKind.Oil: OilL += added; break;
            case FluidKind.Coolant: CoolantL += added; break;
        }
        return added;
    }

    public void Drain(FluidKind kind, float litres)
    {
        switch (kind)
        {
            case FluidKind.Petrol: FuelL = MathF.Max(0f, FuelL - litres); break;
            case FluidKind.Oil: OilL = MathF.Max(0f, OilL - litres); break;
            case FluidKind.Coolant: CoolantL = MathF.Max(0f, CoolantL - litres); break;
        }
    }

    public bool HasOilPressureSupply => OilL >= OilMinimumL;
}
