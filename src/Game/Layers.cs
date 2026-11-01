namespace MyAutumnCar.Game;

/// <summary>
/// Physics layer assignments. Kept in one place because the difference between a
/// part you can bump into and a part welded to a moving car is entirely a matter
/// of which layer it sits on.
/// </summary>
public static class Layers
{
    public const uint World = 1 << 0;      // garage, ground
    public const uint LooseItem = 1 << 1;  // parts, tools and cans lying about
    public const uint Car = 1 << 2;
    public const uint Player = 1 << 3;

    /// <summary>
    /// Raycast-only: fitted parts, bolts, fillers, the seat. Nothing's collision
    /// mask includes this, so a fitted part cannot fight the body it is bolted to.
    /// </summary>
    public const uint Interact = 1 << 4;

    public const uint LooseItemMask = World | Car;
    public const uint CarMask = World | LooseItem;
    public const uint PlayerMask = World | LooseItem | Car;
    public const uint InteractRayMask = LooseItem | Interact;
    public const uint GroundRayMask = World;
}
