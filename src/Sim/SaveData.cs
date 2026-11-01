using System.Collections.Generic;

namespace MyAutumnCar.Sim;

public sealed class PartSaveEntry
{
    public string Id { get; set; } = "";
    public bool Installed { get; set; }
    public float[] Bolts { get; set; } = System.Array.Empty<float>();
    public float Wear { get; set; }
}

/// <summary>
/// The complete persistent state of a game. Scene nodes are never serialised —
/// only this, which is why loading cannot drift from the simulation.
/// Spatial data the view layer owns goes into <see cref="Transforms"/> as flat
/// float arrays so this stays free of engine types.
/// </summary>
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<PartSaveEntry> Parts { get; set; } = new();

    public float FuelL { get; set; }
    public float OilL { get; set; }
    public float CoolantL { get; set; }
    public float BatteryCharge { get; set; }

    public float PistonWear { get; set; }
    public float HeadGasketWear { get; set; }
    public float CoolantTempC { get; set; }
    public bool Seized { get; set; }
    public bool IgnitionOn { get; set; }
    public int Gear { get; set; } = Drivetrain.GearNeutral;

    public double PlayTimeSeconds { get; set; }

    /// <summary>Named transforms as 12 floats: basis rows then origin.</summary>
    public Dictionary<string, float[]> Transforms { get; set; } = new();

    /// <summary>Free-form view-layer flags, e.g. whether the toolbox was picked up.</summary>
    public Dictionary<string, string> Flags { get; set; } = new();
}
