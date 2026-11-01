using System.Numerics;

namespace MyAutumnCar.Sim;

/// <summary>
/// A slot on the chassis. Position is local to the car body origin, which sits at
/// ground level in the middle of the wheelbase.
/// </summary>
public sealed class MountPoint
{
    public string Id { get; init; } = "";
    public Vector3 LocalPosition { get; init; }

    /// <summary>Euler angles in radians, applied to the fitted part.</summary>
    public Vector3 LocalEuler { get; init; }

    /// <summary>How close the held part must be before the mount offers itself, metres.</summary>
    public float SnapRadius { get; init; } = 0.55f;
}
