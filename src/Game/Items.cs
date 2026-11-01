using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>A can of something. Pours into a matching filler, and runs out.</summary>
public partial class JerryCanNode : CarryableNode
{
    public FluidKind Kind { get; private set; }
    public float Litres { get; set; }
    public float CapacityL { get; private set; }

    /// <summary>Litres per second while the pour button is held.</summary>
    public float PourRate { get; private set; } = 1.4f;

    public static JerryCanNode Create(FluidKind kind, float litres, float capacityL)
    {
        string label = kind switch
        {
            FluidKind.Petrol => "petrol can",
            FluidKind.Oil => "oil can",
            _ => "coolant can",
        };
        string hex = kind switch
        {
            FluidKind.Petrol => "b33a2b",
            FluidKind.Oil => "27313a",
            _ => "2f7fa8",
        };

        var size = kind == FluidKind.Oil
            ? new Vector3(0.14f, 0.26f, 0.14f)
            : new Vector3(0.22f, 0.34f, 0.16f);

        var node = new JerryCanNode
        {
            Name = $"Can_{kind}",
            DisplayName = label,
            Kind = kind,
            Litres = litres,
            CapacityL = capacityL,
            PourRate = kind == FluidKind.Oil ? 0.8f : 1.6f,
        };
        node.AddChild(Build.Box(size, Build.Material(hex, 0.7f, 0.1f)));
        node.AddChild(Build.BoxShape(size));
        node.ConfigureLoosePhysics(2f + litres);
        return node;
    }

    public override string Prompt(PlayerContext ctx) =>
        ctx.HandsFree ? $"[E] pick up {DisplayName} ({Litres:0.0} l)" : null;
}

/// <summary>
/// Where a fluid goes in. Deliberately tied to a part: no engine block, no oil
/// filler, which is one less way to pour oil into a car that has no sump.
/// </summary>
public partial class FillerNode : Area3D, IInteractable
{
    public FluidKind Kind { get; private set; }
    private string _requiresPartId;
    private string _label;
    private CarSim _sim;

    public static FillerNode Create(FluidKind kind, string label, string requiresPartId,
                                    Vector3 localPosition, CarSim sim)
    {
        var node = new FillerNode
        {
            Name = $"Filler_{kind}",
            Position = localPosition,
            Kind = kind,
            _label = label,
            _requiresPartId = requiresPartId,
            _sim = sim,
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
        };

        var cap = Build.Cylinder(0.045f, 0.03f, Build.Material("c9a227", 0.5f, 0.6f), "Cap", 12);
        node.AddChild(cap);
        node.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.09f } });
        return node;
    }

    private bool Available => _requiresPartId == null || _sim.Assembly.IsInstalled(_requiresPartId);

    public string Prompt(PlayerContext ctx)
    {
        if (!Available)
            return $"{_label} — needs the {_sim.Catalog.Get(_requiresPartId).DisplayName} fitted";

        float level = _sim.Fluids.Level(Kind);
        float capacity = _sim.Fluids.Capacity(Kind);
        string gauge = $"{level:0.0}/{capacity:0.0} l";

        if (ctx.Held is JerryCanNode can)
        {
            if (can.Kind != Kind) return $"{_label} — {gauge} — that is {can.DisplayName.Replace(" can", "")}";
            if (can.Litres <= 0.001f) return $"{_label} — {gauge} — the can is empty";
            if (level >= capacity - 0.001f) return $"{_label} — full";
            return $"{_label} — {gauge} — [hold E] pour";
        }

        return $"{_label} — {gauge}";
    }

    public void Use(PlayerContext ctx) { }

    public void Hold(PlayerContext ctx, float dt)
    {
        if (!Available) return;
        if (ctx.Held is not JerryCanNode can || can.Kind != Kind) return;
        if (can.Litres <= 0.001f) return;

        float wanted = Mathf.Min(can.PourRate * dt, can.Litres);
        float poured = _sim.Fluids.Pour(Kind, wanted);
        can.Litres -= poured;

        if (poured <= 0.0001f) ctx.Say($"{_label} is full");
    }

    public void Turn(PlayerContext ctx, int direction, float dt) { }
}

/// <summary>The toolbox. Picking it up once is what gives you the spanner slots.</summary>
public partial class ToolboxNode : StaticBody3D, IInteractable
{
    private bool _taken;

    public static ToolboxNode Create(Vector3 position)
    {
        var size = new Vector3(0.46f, 0.22f, 0.26f);
        var node = new ToolboxNode
        {
            Name = "Toolbox",
            Position = position,
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
        };
        node.AddChild(Build.Box(size, Build.Material("a4422f", 0.6f, 0.3f)));
        node.AddChild(Build.BoxShape(size));
        return node;
    }

    public string Prompt(PlayerContext ctx) =>
        _taken ? "toolbox — spanners on 1 to 7" : "[E] take the spanners";

    public void Use(PlayerContext ctx)
    {
        if (_taken) return;
        _taken = true;
        ctx.Tools.HasToolbox = true;
        ctx.Say("spanners: 1 is 8 mm, 2 is 10, 3 is 12, 4 is 14, 5 is 17, 6 is the plug spanner, 7 a screwdriver");
    }

    public void Hold(PlayerContext ctx, float dt) { }
    public void Turn(PlayerContext ctx, int direction, float dt) { }
}

/// <summary>The driver's seat.</summary>
public partial class SeatNode : Area3D, IInteractable
{
    public static SeatNode Create(Vector3 localPosition)
    {
        var node = new SeatNode
        {
            Name = "Seat",
            Position = localPosition,
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
        };
        node.AddChild(Build.Box(new Vector3(0.44f, 0.5f, 0.44f), Build.Material("4a3b31", 0.85f, 0.0f)));
        node.AddChild(Build.BoxShape(new Vector3(0.5f, 0.6f, 0.5f)));
        return node;
    }

    public string Prompt(PlayerContext ctx) =>
        ctx.HandsFree ? "[E] get in" : "[E] get in — you are carrying something";

    public void Use(PlayerContext ctx) => ctx.Player.EnterCar(ctx.Car);

    public void Hold(PlayerContext ctx, float dt) { }
    public void Turn(PlayerContext ctx, int direction, float dt) { }
}
