using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// A thing you can pick up. While carried it becomes a kinematic body with its
/// collision switched off and is driven toward the hand anchor, which is the
/// cheapest cure for the classic held-object jitter: a carried rigid body fighting
/// the player capsule it is clipping into.
/// </summary>
public partial class CarryableNode : RigidBody3D, IInteractable
{
    [Export] public string DisplayName { get; set; } = "item";

    public bool Carried { get; private set; }
    private Node3D _anchor;

    protected void ConfigureLoosePhysics(float massKg)
    {
        Mass = Mathf.Max(0.05f, massKg);
        CollisionLayer = Layers.LooseItem;
        CollisionMask = Layers.LooseItemMask;
        ContinuousCd = true;
        CanSleep = true;
        Freeze = false;
        FreezeMode = FreezeModeEnum.Kinematic;
    }

    public virtual void OnPickedUp(Node3D anchor)
    {
        _anchor = anchor;
        Carried = true;
        Freeze = true;
        FreezeMode = FreezeModeEnum.Kinematic;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        CollisionLayer = Layers.Interact;   // still aimable, no longer solid
        CollisionMask = 0;
        Sleeping = false;
    }

    public virtual void OnDropped(Vector3 velocity)
    {
        _anchor = null;
        Carried = false;
        Freeze = false;
        CollisionLayer = Layers.LooseItem;
        CollisionMask = Layers.LooseItemMask;
        LinearVelocity = velocity;
        AngularVelocity = Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Carried && _anchor != null && IsInstanceValid(_anchor))
            GlobalTransform = _anchor.GlobalTransform;
    }

    public virtual string Prompt(PlayerContext ctx) =>
        ctx.HandsFree ? $"[E] pick up {DisplayName}" : null;

    public virtual void Use(PlayerContext ctx)
    {
        if (ctx.HandsFree) ctx.Player.PickUp(this);
    }

    public virtual void Hold(PlayerContext ctx, float dt) { }
    public virtual void Turn(PlayerContext ctx, int direction, float dt) { }
}
