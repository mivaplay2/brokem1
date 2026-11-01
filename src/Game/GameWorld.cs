using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;
using Ids = MyAutumnCar.Sim.PartCatalog.Ids;

namespace MyAutumnCar.Game;

/// <summary>
/// Builds the slice: a garage, a bare chassis on the floor, the parts to put on
/// it and the fluids to pour in. Every piece of geometry is made here in code, so
/// the project runs straight from a clone with no scene or asset authoring.
/// </summary>
public sealed class GameWorld
{
    public CarSim Sim { get; private set; }
    public CarNode Car { get; private set; }
    public PlayerController Player { get; private set; }
    public Hud Hud { get; private set; }
    public ToolBelt Tools { get; } = new();
    public PlayerContext Context { get; private set; }

    public Dictionary<string, PartNode> Parts { get; } = new();
    public List<JerryCanNode> Cans { get; } = new();

    /// <summary>Where each part starts out on the garage floor.</summary>
    private static readonly (string Id, Vector3 Where)[] PartLayout =
    {
        (Ids.EngineBlock,  new Vector3(-2.55f, 0.40f, -1.20f)),
        (Ids.CylinderHead, new Vector3(-2.55f, 0.20f, -2.00f)),
        (Ids.Carburetor,   new Vector3(-2.15f, 0.15f, -2.55f)),
        (Ids.SparkPlugs,   new Vector3(-1.85f, 0.10f, -2.95f)),
        (Ids.Clutch,       new Vector3( 2.30f, 0.14f,  1.20f)),
        (Ids.Gearbox,      new Vector3( 2.55f, 0.22f,  2.00f)),
        (Ids.Driveshaft,   new Vector3( 1.60f, 0.12f,  2.80f)),
        // Clear of the bench, which occupies x 2.4..4.2, z -2.35..-1.65.
        (Ids.Battery,      new Vector3( 2.40f, 0.18f, -0.60f)),
        (Ids.Radiator,     new Vector3( 1.90f, 0.12f, -3.10f)),
        (Ids.WheelFl,      new Vector3(-3.30f, 0.30f,  2.40f)),
        (Ids.WheelFr,      new Vector3(-3.30f, 0.30f,  3.10f)),
        (Ids.WheelRl,      new Vector3( 3.30f, 0.30f,  2.40f)),
        (Ids.WheelRr,      new Vector3( 3.30f, 0.30f,  3.10f)),
    };

    public static GameWorld Create(Node3D root)
    {
        var world = new GameWorld { Sim = new CarSim() };

        BuildEnvironment(root);
        BuildGarage(root);

        world.Car = CarNode.Create(world.Sim);
        world.Car.Position = new Vector3(0f, 0.40f, 0f);
        root.AddChild(world.Car);

        foreach (var (id, where) in PartLayout)
        {
            var part = PartNode.Create(world.Sim.Catalog.Get(id), world.Sim, world.Car);
            part.Position = where;
            root.AddChild(part);
            world.Parts[id] = part;
            world.Car.RegisterPart(part);
        }

        root.AddChild(Build.Solid(new Vector3(1.8f, 0.9f, 0.7f), new Vector3(3.3f, 0.45f, -2.0f),
                                  "5a4a3c", "Bench"));
        root.AddChild(ToolboxNode.Create(new Vector3(3.3f, 1.01f, -2.0f)));

        world.AddCan(root, FluidKind.Petrol, 20f, 20f, new Vector3(-3.30f, 0.22f, -0.20f));
        world.AddCan(root, FluidKind.Oil, 5f, 5f, new Vector3(-3.05f, 0.18f, -0.65f));
        world.AddCan(root, FluidKind.Coolant, 8f, 8f, new Vector3(-2.80f, 0.22f, -1.05f));

        world.Player = PlayerController.Create(new Vector3(0f, 0.2f, 4.2f));
        root.AddChild(world.Player);

        world.Hud = Hud.Create();
        root.AddChild(world.Hud);

        world.Context = new PlayerContext
        {
            Sim = world.Sim,
            Tools = world.Tools,
            Player = world.Player,
            Car = world.Car,
            Hud = world.Hud,
        };

        world.Hud.Flash("the chassis is on the floor, the parts are around it, the toolbox is on the bench. F1 for the controls.");
        return world;
    }

    private void AddCan(Node3D root, FluidKind kind, float litres, float capacity, Vector3 where)
    {
        var can = JerryCanNode.Create(kind, litres, capacity);
        can.Position = where;
        root.AddChild(can);
        Cans.Add(can);
    }

    private static void BuildEnvironment(Node3D root)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.36f, 0.44f, 0.54f),
            SkyHorizonColor = new Color(0.62f, 0.62f, 0.58f),
            GroundBottomColor = new Color(0.22f, 0.21f, 0.18f),
            GroundHorizonColor = new Color(0.46f, 0.44f, 0.38f),
            SunAngleMax = 24f,
        };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.72f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            FogEnabled = true,
            FogDensity = 0.004f,
            FogLightColor = new Color(0.58f, 0.60f, 0.60f),
        };
        root.AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-38f, 38f, 0f),
            LightEnergy = 1.05f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            ShadowEnabled = true,
        };
        root.AddChild(sun);

        var lamp = new OmniLight3D
        {
            Name = "GarageLamp",
            Position = new Vector3(0f, 3.0f, 0f),
            OmniRange = 16f,
            LightEnergy = 2.2f,
            LightColor = new Color(1f, 0.94f, 0.82f),
            ShadowEnabled = true,
        };
        root.AddChild(lamp);
    }

    private static void BuildGarage(Node3D root)
    {
        root.AddChild(Build.Solid(new Vector3(220f, 1f, 220f), new Vector3(0f, -0.5f, 0f),
                                  "4e5243", "Ground"));

        // The garage floor is a slab so the concrete reads differently from the field.
        root.AddChild(Build.Solid(new Vector3(10.4f, 0.12f, 16.4f), new Vector3(0f, 0.04f, 0f),
                                  "9a9a94", "Floor"));

        const float h = 3.4f;
        root.AddChild(Build.Solid(new Vector3(10.4f, h, 0.3f), new Vector3(0f, h / 2f, 8.2f), "8c8678", "WallBack"));
        root.AddChild(Build.Solid(new Vector3(0.3f, h, 16.4f), new Vector3(-5.2f, h / 2f, 0f), "8c8678", "WallLeft"));
        root.AddChild(Build.Solid(new Vector3(0.3f, h, 16.4f), new Vector3(5.2f, h / 2f, 0f), "8c8678", "WallRight"));

        // Front wall with a four metre opening to drive out of.
        root.AddChild(Build.Solid(new Vector3(3.2f, h, 0.3f), new Vector3(-3.6f, h / 2f, -8.2f), "8c8678", "WallFrontL"));
        root.AddChild(Build.Solid(new Vector3(3.2f, h, 0.3f), new Vector3(3.6f, h / 2f, -8.2f), "8c8678", "WallFrontR"));

        // A strip of tarmac out of the door, so there is somewhere to drive to.
        root.AddChild(Build.Solid(new Vector3(7f, 0.1f, 60f), new Vector3(0f, 0.03f, -38f), "3b3b3e", "Drive"));
    }
}
