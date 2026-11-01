using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MyAutumnCar.Sim;

/// <summary>
/// Turns a <see cref="CarSim"/> into a <see cref="SaveData"/> and back. Pure, so
/// it is covered by the sim tests rather than only by loading a real game.
/// </summary>
public static class SaveSystem
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    public static SaveData Capture(CarSim sim)
    {
        var data = new SaveData
        {
            FuelL = sim.Fluids.FuelL,
            OilL = sim.Fluids.OilL,
            CoolantL = sim.Fluids.CoolantL,
            BatteryCharge = sim.Fluids.BatteryCharge,
            PistonWear = sim.Engine.PistonWear,
            HeadGasketWear = sim.Engine.HeadGasketWear,
            CoolantTempC = sim.Engine.CoolantTempC,
            Seized = sim.Engine.Seized,
            IgnitionOn = sim.Engine.IgnitionOn,
            Gear = sim.Drivetrain.Gear,
            PlayTimeSeconds = sim.PlayTimeSeconds,
        };

        foreach (var kv in sim.Assembly.Parts)
        {
            data.Parts.Add(new PartSaveEntry
            {
                Id = kv.Key,
                Installed = kv.Value.Installed,
                Bolts = (float[])kv.Value.BoltTightness.Clone(),
                Wear = kv.Value.Wear,
            });
        }
        return data;
    }

    public static void Apply(SaveData data, CarSim sim)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Version > SaveData.CurrentVersion)
            throw new InvalidOperationException(
                $"save version {data.Version} is newer than this build ({SaveData.CurrentVersion})");

        sim.Fluids.FuelL = data.FuelL;
        sim.Fluids.OilL = data.OilL;
        sim.Fluids.CoolantL = data.CoolantL;
        sim.Fluids.BatteryCharge = data.BatteryCharge;

        sim.Engine.ResetToCold();
        sim.Engine.PistonWear = data.PistonWear;
        sim.Engine.HeadGasketWear = data.HeadGasketWear;
        sim.Engine.CoolantTempC = data.CoolantTempC;
        sim.Engine.Seized = data.Seized;
        sim.Engine.IgnitionOn = data.IgnitionOn;

        sim.Drivetrain.Gear = Math.Clamp(data.Gear, Drivetrain.GearReverse, Drivetrain.GearTop);
        sim.Drivetrain.ClutchPedal = 0f;
        sim.PlayTimeSeconds = data.PlayTimeSeconds;

        foreach (var entry in data.Parts)
        {
            if (!sim.Assembly.TryGetState(entry.Id, out var state)) continue;
            var def = sim.Catalog.Get(entry.Id);

            state.Installed = entry.Installed;
            state.Wear = entry.Wear;
            state.BoltTightness = NormaliseBolts(entry.Bolts, def.BoltCount);
        }
    }

    private static float[] NormaliseBolts(IReadOnlyList<float> saved, int expected)
    {
        var bolts = new float[expected];
        if (saved == null) return bolts;
        for (int i = 0; i < expected && i < saved.Count; i++)
            bolts[i] = Math.Clamp(saved[i], 0f, 1f);
        return bolts;
    }

    public static string ToJson(SaveData data) => JsonSerializer.Serialize(data, Options);

    public static SaveData FromJson(string json) =>
        JsonSerializer.Deserialize<SaveData>(json, Options)
        ?? throw new InvalidOperationException("save file is empty or malformed");
}
