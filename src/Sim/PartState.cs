using System;

namespace MyAutumnCar.Sim;

/// <summary>
/// Everything about a part that changes during play and therefore has to survive
/// a save. Deliberately free of engine types.
/// </summary>
public sealed class PartState
{
    public const float SecureThreshold = 0.95f;

    public string DefinitionId { get; init; } = "";

    /// <summary>Fitted to its mount point. A fitted part may still be unbolted.</summary>
    public bool Installed { get; set; }

    /// <summary>Tightness per bolt, 0..1, parallel to <see cref="PartDefinition.Bolts"/>.</summary>
    public float[] BoltTightness { get; set; } = Array.Empty<float>();

    /// <summary>0 = factory fresh, 1 = scrap.</summary>
    public float Wear { get; set; }

    public static PartState ForDefinition(PartDefinition def) => new()
    {
        DefinitionId = def.Id,
        Installed = false,
        BoltTightness = new float[def.BoltCount],
        Wear = 0f,
    };

    public bool AnyBoltLoose()
    {
        for (int i = 0; i < BoltTightness.Length; i++)
            if (BoltTightness[i] < SecureThreshold)
                return true;
        return false;
    }

    public bool AllBoltsLoose()
    {
        for (int i = 0; i < BoltTightness.Length; i++)
            if (BoltTightness[i] > 0.01f)
                return false;
        return true;
    }

    public int SecuredBoltCount()
    {
        int n = 0;
        for (int i = 0; i < BoltTightness.Length; i++)
            if (BoltTightness[i] >= SecureThreshold)
                n++;
        return n;
    }

    public float MeanTightness()
    {
        if (BoltTightness.Length == 0) return 1f;
        float s = 0f;
        for (int i = 0; i < BoltTightness.Length; i++) s += BoltTightness[i];
        return s / BoltTightness.Length;
    }

    /// <summary>Fitted and every bolt torqued down.</summary>
    public bool Secured => Installed && !AnyBoltLoose();

    /// <summary>Secured and not worn out — the condition the mechanical sim asks about.</summary>
    public bool Functional => Secured && Wear < 1f;
}
