using System.Numerics;

namespace MyAutumnCar.Sim;

public enum PartKind
{
    Engine,
    Fuel,
    Electrical,
    Cooling,
    Running,
    Body,
}

/// <summary>
/// Static description of a part. Never mutated at runtime — all per-game state
/// lives in <see cref="PartState"/>. Adding a part to the game means adding one
/// of these to the catalog, not writing a new script.
/// </summary>
public sealed class PartDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public PartKind Kind { get; init; } = PartKind.Body;
    public float MassKg { get; init; } = 1f;

    /// <summary>Mount point id on the chassis this part belongs to.</summary>
    public string MountId { get; init; } = "";

    /// <summary>
    /// Parts that must be installed AND fully bolted before this one can go on.
    /// Enforced in both directions: a dependency cannot be removed while a
    /// dependent part is still fitted.
    /// </summary>
    public string[] RequiresSecured { get; init; } = System.Array.Empty<string>();

    public BoltSpec[] Bolts { get; init; } = System.Array.Empty<BoltSpec>();

    /// <summary>Box extents for the placeholder mesh, metres. Replace with real meshes later.</summary>
    public Vector3 Size { get; init; } = new(0.2f, 0.2f, 0.2f);

    /// <summary>Placeholder colour, "RRGGBB".</summary>
    public string ColorHex { get; init; } = "9aa0a6";

    public int BoltCount => Bolts.Length;
    public bool NeedsBolts => Bolts.Length > 0;
}
