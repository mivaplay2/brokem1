using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// Anything the crosshair can land on. <see cref="Prompt"/> returning null means
/// "nothing to do here right now", which is how the HUD stays quiet instead of
/// advertising actions that would be refused.
/// </summary>
public interface IInteractable
{
    string Prompt(PlayerContext ctx);
    void Use(PlayerContext ctx);
    void Hold(PlayerContext ctx, float dt);
    void Turn(PlayerContext ctx, int direction, float dt);
}

/// <summary>What an interaction is allowed to know about the player.</summary>
public sealed class PlayerContext
{
    public CarSim Sim { get; init; }
    public ToolBelt Tools { get; init; }
    public PlayerController Player { get; init; }
    public CarNode Car { get; init; }
    public Hud Hud { get; init; }

    public CarryableNode Held => Player?.Held;
    public bool HandsFree => Player?.Held == null;

    public void Say(string message) => Hud?.Flash(message);
}

/// <summary>
/// The tool in the player's other hand. One slot per spanner, chosen with the
/// number keys, and nothing at all until the toolbox has been picked up — the
/// right spanner being a thing you have to go and find is most of the loop.
/// </summary>
public sealed class ToolBelt
{
    public static readonly ToolSize[] Slots =
    {
        ToolSize.Wrench8,
        ToolSize.Wrench10,
        ToolSize.Wrench12,
        ToolSize.Wrench14,
        ToolSize.Wrench17,
        ToolSize.SparkPlugWrench,
        ToolSize.Screwdriver,
    };

    public bool HasToolbox { get; set; }
    public ToolSize Selected { get; private set; } = ToolSize.None;

    public string SelectedLabel => HasToolbox
        ? (Selected == ToolSize.None ? "no tool" : ToolSizeNames.Label(Selected))
        : "no toolbox";

    public bool Select(int slotIndex)
    {
        if (!HasToolbox) return false;
        if (slotIndex < 0 || slotIndex >= Slots.Length) return false;
        Selected = Slots[slotIndex] == Selected ? ToolSize.None : Slots[slotIndex];
        return true;
    }

    public void Clear() => Selected = ToolSize.None;
}
