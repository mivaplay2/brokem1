using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// One fastener, aimable in its own right. Tightness is read straight out of the
/// simulation every frame, so the colour on screen can never disagree with what
/// the engine thinks is holding the part on.
/// </summary>
public partial class BoltNode : Area3D, IInteractable
{
    private const float HeadRadius = 0.019f;
    private const float HeadHeight = 0.026f;
    private const float AimRadius = 0.055f;

    private PartNode _part;
    private int _index;
    private BoltSpec _spec;
    private MeshInstance3D _head;
    private StandardMaterial3D _material;

    public static BoltNode Create(PartNode part, int index, BoltSpec spec)
    {
        var node = new BoltNode
        {
            Name = $"Bolt_{spec.Id}",
            Position = ToGodot(spec.LocalPosition),
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
        };
        node._part = part;
        node._index = index;
        node._spec = spec;

        node._material = Build.Material("8d8f93", 0.4f, 0.85f);
        node._head = Build.Cylinder(HeadRadius, HeadHeight, node._material, "Head", 6);
        node.AddChild(node._head);

        // The aim volume is deliberately much larger than the head: hunting for a
        // 19 mm target with a mouse is tedious, missing the bolt is not interesting.
        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = AimRadius } };
        node.AddChild(shape);
        return node;
    }

    private float Tightness => _part.State.BoltTightness[_index];

    public override void _Process(double delta)
    {
        float t = Tightness;
        _material.AlbedoColor = Color.FromHtml("8d8f93").Lerp(Color.FromHtml("5fbf6a"), t);
        // The head sinks as it goes home — readable at a glance, without a label.
        _head.Position = new Vector3(0f, (1f - t) * 0.014f, 0f);
        _head.Rotation = new Vector3(0f, t * Mathf.Tau * 3f, 0f);
    }

    public string Prompt(PlayerContext ctx)
    {
        float t = Tightness;
        string state = t >= PartState.SecureThreshold ? "tight" : $"{t * 100f:0}%";

        if (ctx.Tools.Selected != _spec.Tool)
            return $"{_part.Def.DisplayName} bolt — {state} — needs a {ToolSizeNames.Label(_spec.Tool)}";

        if (t >= 1f) return $"{_part.Def.DisplayName} bolt — tight — [RMB] loosen";
        if (t <= 0f) return $"{_part.Def.DisplayName} bolt — loose — [LMB] tighten";
        return $"{_part.Def.DisplayName} bolt — {state} — [LMB] tighten / [RMB] loosen";
    }

    public void Use(PlayerContext ctx)
    {
        if (ctx.Tools.Selected != _spec.Tool)
            ctx.Say($"needs a {ToolSizeNames.Label(_spec.Tool)}");
    }

    public void Hold(PlayerContext ctx, float dt) { }

    public void Turn(PlayerContext ctx, int direction, float dt)
    {
        float before = Tightness;
        ctx.Sim.Assembly.TurnBolt(_part.Def.Id, _index, ctx.Tools.Selected, dt, direction, out string message);

        if (Mathf.IsZeroApprox(before) && message != null)
        {
            ctx.Say(message);
            return;
        }

        float after = Tightness;
        if (before < PartState.SecureThreshold && after >= PartState.SecureThreshold)
            ctx.Say($"{_part.Def.DisplayName}: {_part.State.SecuredBoltCount()}/{_part.Def.BoltCount} bolts tight");
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
