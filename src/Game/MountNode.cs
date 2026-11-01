using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// A slot on the car. Offers itself only to the part that belongs in it, and
/// states the reason when the simulation will not accept it yet, so the player is
/// never left guessing why a part refuses to go on.
/// </summary>
public partial class MountNode : Area3D, IInteractable
{
    public string MountId { get; private set; }
    public string PartId { get; private set; }

    private CarSim _sim;
    private CarNode _car;
    private MeshInstance3D _marker;
    private StandardMaterial3D _material;

    public static MountNode Create(MountPoint mount, PartDefinition part, CarSim sim, CarNode car)
    {
        var node = new MountNode
        {
            Name = $"Mount_{mount.Id}",
            Position = new Vector3(mount.LocalPosition.X, mount.LocalPosition.Y, mount.LocalPosition.Z),
            MountId = mount.Id,
            PartId = part.Id,
            _sim = sim,
            _car = car,
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
        };

        var size = new Vector3(part.Size.X, part.Size.Y, part.Size.Z);
        if (part.Kind == PartKind.Running) size = new Vector3(part.Size.X, part.Size.Y, part.Size.Y);

        node._material = Build.Material("3a3f45", 0.9f, 0.1f);
        node._material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        node._material.AlbedoColor = new Color(0.6f, 0.65f, 0.7f, 0.18f);
        node._marker = Build.Box(size * 1.04f, node._material, "Ghost");
        node.AddChild(node._marker);
        node.AddChild(Build.BoxShape(size * 1.15f));
        return node;
    }

    public override void _Process(double delta)
    {
        // The ghost is only there while the slot is empty — once the part is on,
        // a translucent box over it would just be visual noise.
        bool empty = !_sim.Assembly.IsInstalled(PartId);
        if (_marker.Visible != empty) _marker.Visible = empty;
    }

    public string Prompt(PlayerContext ctx)
    {
        var def = _sim.Catalog.Get(PartId);

        if (_sim.Assembly.IsInstalled(PartId)) return null;

        if (ctx.Held is PartNode held && held.Def.Id == PartId)
        {
            string blocker = _sim.Assembly.InstallBlocker(PartId);
            return blocker == null
                ? $"[E] fit the {def.DisplayName}"
                : $"cannot fit it yet — {blocker}";
        }

        if (ctx.Held is PartNode wrong)
            return $"the {wrong.Def.DisplayName} does not go here — this is for the {def.DisplayName}";

        return $"empty — the {def.DisplayName} goes here";
    }

    public void Use(PlayerContext ctx)
    {
        if (ctx.Held is not PartNode held || held.Def.Id != PartId) return;

        var result = _sim.Assembly.TryInstall(PartId, out string message);
        ctx.Say(message);
        if (result != AssemblyResult.Ok) return;

        ctx.Player.ReleaseHeld();
        held.AttachToMount(this);
        _car.OnAssemblyChanged();
    }

    public void Hold(PlayerContext ctx, float dt) { }
    public void Turn(PlayerContext ctx, int direction, float dt) { }
}
