using System.Numerics;

namespace MyAutumnCar.Sim;

/// <summary>
/// A single fastener on a part. Purely declarative: position is local to the part
/// origin so the view layer can spawn a selectable bolt node per entry.
/// </summary>
public sealed class BoltSpec
{
    public string Id { get; init; } = "bolt";

    /// <summary>Position relative to the part origin, metres.</summary>
    public Vector3 LocalPosition { get; init; }

    /// <summary>Axis the bolt is driven along; used by the view layer for orientation.</summary>
    public Vector3 Axis { get; init; } = Vector3.UnitY;

    public ToolSize Tool { get; init; } = ToolSize.Wrench10;

    /// <summary>
    /// Tightness gained per second of continuous turning. Lower values mean a
    /// longer, more deliberate bolt.
    /// </summary>
    public float TightenRate { get; init; } = 0.55f;
}
