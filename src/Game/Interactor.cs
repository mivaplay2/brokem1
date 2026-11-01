using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// Resolves what the crosshair is on. Walks up from the collider it hit, because
/// the thing that owns the behaviour is usually the parent of the shape that got
/// in the ray's way.
/// </summary>
public sealed class Interactor
{
    public const float ReachMetres = 2.6f;

    private readonly RayCast3D _ray;

    public IInteractable Target { get; private set; }
    public string Prompt { get; private set; }

    public Interactor(Node3D head)
    {
        _ray = new RayCast3D
        {
            Name = "Aim",
            TargetPosition = Vector3.Forward * ReachMetres,
            CollisionMask = Layers.InteractRayMask,
            CollideWithAreas = true,
            CollideWithBodies = true,
            Enabled = true,
        };
        head.AddChild(_ray);
    }

    public void AddException(CollisionObject3D body) => _ray.AddException(body);

    public void Poll(PlayerContext ctx)
    {
        Target = null;
        Prompt = null;

        _ray.ForceRaycastUpdate();
        if (!_ray.IsColliding()) return;

        var found = Resolve(_ray.GetCollider() as Node);
        if (found == null) return;

        string prompt = found.Prompt(ctx);
        if (prompt == null) return;        // it is there, but has nothing to offer

        Target = found;
        Prompt = prompt;
    }

    private static IInteractable Resolve(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
            if (n is IInteractable interactable)
                return interactable;
        return null;
    }
}
