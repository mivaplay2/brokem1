using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// The view of one <see cref="PartState"/>. A part is never two objects: the same
/// node is the thing lying on the garage floor, the thing in your hands and the
/// thing bolted to the car. Which of those it is, is decided by the simulation,
/// and the node follows.
/// </summary>
public partial class PartNode : CarryableNode
{
    public PartDefinition Def { get; private set; }

    private CarSim _sim;
    private CarNode _car;
    private Node3D _mount;
    private MeshInstance3D _mesh;
    private readonly List<BoltNode> _bolts = new();
    private float _spinRadians;

    public PartState State => _sim.Assembly.Get(Def.Id);
    public bool Installed => State.Installed;
    public bool IsWheel => Def.Kind == PartKind.Running;

    /// <summary>Wheel rotation in rad/s, written by the car so the tyre looks like it rolls.</summary>
    public float SpinRate { get; set; }

    public static PartNode Create(PartDefinition def, CarSim sim, CarNode car)
    {
        var node = new PartNode
        {
            Name = $"Part_{def.Id}",
            DisplayName = def.DisplayName,
            Def = def,
            _sim = sim,
            _car = car,
        };

        var material = Build.Material(def.ColorHex);

        if (def.Kind == PartKind.Running)
        {
            float radius = def.Size.Y * 0.5f;
            float width = def.Size.X;
            node._mesh = Build.Cylinder(radius, width, material, "Mesh", 20);
            node._mesh.RotationDegrees = new Vector3(0f, 0f, 90f);
            var shape = Build.CylinderShape(radius, width);
            shape.RotationDegrees = new Vector3(0f, 0f, 90f);
            node.AddChild(node._mesh);
            node.AddChild(shape);
        }
        else
        {
            var size = new Vector3(def.Size.X, def.Size.Y, def.Size.Z);
            node._mesh = Build.Box(size, material);
            node.AddChild(node._mesh);
            node.AddChild(Build.BoxShape(size));
        }

        node.ConfigureLoosePhysics(def.MassKg);
        return node;
    }

    /// <summary>Called once the simulation has accepted the part onto its mount.</summary>
    public void AttachToMount(Node3D mount)
    {
        _mount = mount;
        Freeze = true;
        FreezeMode = FreezeModeEnum.Kinematic;
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        GlobalTransform = mount.GlobalTransform;
        SpawnBolts();
    }

    /// <summary>Called once the simulation has accepted the part coming back off.</summary>
    public void DetachFromMount()
    {
        _mount = null;
        ClearBolts();
        Freeze = false;
        CollisionLayer = Layers.LooseItem;
        CollisionMask = Layers.LooseItemMask;
    }

    private void SpawnBolts()
    {
        ClearBolts();
        for (int i = 0; i < Def.BoltCount; i++)
        {
            var bolt = BoltNode.Create(this, i, Def.Bolts[i]);
            AddChild(bolt);
            _bolts.Add(bolt);
        }
    }

    private void ClearBolts()
    {
        foreach (var bolt in _bolts)
            if (IsInstanceValid(bolt)) bolt.QueueFree();
        _bolts.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (_mount != null && IsInstanceValid(_mount))
            GlobalTransform = _mount.GlobalTransform;

        if (IsWheel && _mesh != null)
        {
            _spinRadians = Mathf.Wrap(_spinRadians + SpinRate * (float)delta, 0f, Mathf.Tau);
            _mesh.Rotation = new Vector3(_spinRadians, 0f, Mathf.Pi * 0.5f);
        }
    }

    public override string Prompt(PlayerContext ctx)
    {
        if (!Installed) return base.Prompt(ctx);

        var state = State;
        int tight = state.SecuredBoltCount();

        if (state.AnyBoltLoose())
        {
            string tool = Def.NeedsBolts ? ToolSizeNames.Label(Def.Bolts[0].Tool) : "no tool";
            return $"{Def.DisplayName} — {tight}/{Def.BoltCount} bolts tight — {tool}";
        }

        if (!ctx.HandsFree)
            return $"{Def.DisplayName} — bolted down";

        string blocker = ctx.Sim.Assembly.RemoveBlocker(Def.Id);
        return blocker == null
            ? $"[E] take off the {Def.DisplayName}"
            : $"{Def.DisplayName} — {blocker}";
    }

    public override void Use(PlayerContext ctx)
    {
        if (!Installed)
        {
            base.Use(ctx);
            return;
        }

        var result = ctx.Sim.Assembly.TryRemove(Def.Id, out string message);
        ctx.Say(message);
        if (result != AssemblyResult.Ok) return;

        DetachFromMount();
        _car.OnAssemblyChanged();

        // It comes off into your hands, which is where you wanted it.
        if (ctx.HandsFree) ctx.Player.PickUp(this);
    }
}
