using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MyAutumnCar.Sim;

/// <summary>
/// The parts list for the vertical slice: a four-cylinder engine you build up
/// from a bare block, plus the four corners it rolls on. Everything here is data,
/// so growing the car to the full 150-part machine is a catalog edit.
/// </summary>
public sealed class PartCatalog
{
    private readonly Dictionary<string, PartDefinition> _parts;
    private readonly Dictionary<string, MountPoint> _mounts;

    public PartCatalog(IEnumerable<PartDefinition> parts, IEnumerable<MountPoint> mounts)
    {
        _parts = parts.ToDictionary(p => p.Id, p => p);
        _mounts = mounts.ToDictionary(m => m.Id, m => m);
    }

    public IReadOnlyDictionary<string, PartDefinition> Parts => _parts;
    public IReadOnlyDictionary<string, MountPoint> Mounts => _mounts;
    public IEnumerable<PartDefinition> All => _parts.Values;

    public PartDefinition Get(string id) =>
        _parts.TryGetValue(id, out var d)
            ? d
            : throw new KeyNotFoundException($"unknown part '{id}'");

    public bool TryGet(string id, out PartDefinition def) => _parts.TryGetValue(id, out def);

    public MountPoint GetMount(string id) =>
        _mounts.TryGetValue(id, out var m)
            ? m
            : throw new KeyNotFoundException($"unknown mount '{id}'");

    /// <summary>Parts without which the engine cannot run at all.</summary>
    public static readonly string[] EngineCritical =
    {
        Ids.EngineBlock, Ids.CylinderHead, Ids.Carburetor, Ids.SparkPlugs,
    };

    /// <summary>
    /// Everything between the flywheel and the rear axle. The engine runs
    /// perfectly well without any of it; the car simply does not move.
    /// </summary>
    public static readonly string[] Transmission =
    {
        Ids.Clutch, Ids.Gearbox, Ids.Driveshaft,
    };

    /// <summary>
    /// A workable order to build the car in. Dependencies are enforced by
    /// <see cref="AssemblyModel"/>; this is only what the next-job hint walks.
    /// </summary>
    public static readonly string[] BuildOrder =
    {
        Ids.EngineBlock, Ids.CylinderHead, Ids.Carburetor, Ids.SparkPlugs,
        Ids.Clutch, Ids.Gearbox, Ids.Driveshaft,
        Ids.Battery, Ids.Radiator,
        Ids.WheelFl, Ids.WheelFr, Ids.WheelRl, Ids.WheelRr,
    };

    public static readonly string[] Wheels =
    {
        Ids.WheelFl, Ids.WheelFr, Ids.WheelRl, Ids.WheelRr,
    };

    public static class Ids
    {
        public const string EngineBlock = "engine_block";
        public const string CylinderHead = "cylinder_head";
        public const string Carburetor = "carburetor";
        public const string SparkPlugs = "spark_plugs";
        public const string Clutch = "clutch";
        public const string Gearbox = "gearbox";
        public const string Driveshaft = "driveshaft";
        public const string Battery = "battery";
        public const string Radiator = "radiator";
        public const string WheelFl = "wheel_fl";
        public const string WheelFr = "wheel_fr";
        public const string WheelRl = "wheel_rl";
        public const string WheelRr = "wheel_rr";
    }

    private static BoltSpec[] Ring(int count, float radius, float height, ToolSize tool, float rate)
    {
        var bolts = new BoltSpec[count];
        for (int i = 0; i < count; i++)
        {
            float a = MathF.Tau * i / count;
            bolts[i] = new BoltSpec
            {
                Id = $"b{i + 1}",
                LocalPosition = new Vector3(MathF.Cos(a) * radius, height, MathF.Sin(a) * radius),
                Axis = Vector3.UnitY,
                Tool = tool,
                TightenRate = rate,
            };
        }
        return bolts;
    }

    private static BoltSpec[] WheelStuds() => Ring(4, 0.11f, 0.0f, ToolSize.Wrench17, 0.5f);

    private static PartDefinition Wheel(string id, string name, string mount) => new()
    {
        Id = id,
        DisplayName = name,
        Kind = PartKind.Running,
        MassKg = 11.5f,
        MountId = mount,
        Bolts = WheelStuds(),
        Size = new Vector3(0.19f, 0.58f, 0.58f),
        ColorHex = "2a2a2e",
    };

    public static PartCatalog CreateDefault()
    {
        var mounts = new[]
        {
            new MountPoint { Id = "mount_engine",   LocalPosition = new Vector3(0f, 0.62f, -1.10f) },
            new MountPoint { Id = "mount_head",     LocalPosition = new Vector3(0f, 0.88f, -1.10f), SnapRadius = 0.4f },
            new MountPoint { Id = "mount_carb",     LocalPosition = new Vector3(0f, 1.02f, -1.02f), SnapRadius = 0.35f },
            new MountPoint { Id = "mount_plugs",    LocalPosition = new Vector3(0.18f, 0.94f, -1.10f), SnapRadius = 0.35f },
            new MountPoint { Id = "mount_clutch",     LocalPosition = new Vector3(0f, 0.58f, -0.70f), SnapRadius = 0.42f },
            new MountPoint { Id = "mount_gearbox",    LocalPosition = new Vector3(0f, 0.54f, -0.22f) },
            new MountPoint { Id = "mount_driveshaft", LocalPosition = new Vector3(0f, 0.40f,  0.55f) },
            new MountPoint { Id = "mount_battery",  LocalPosition = new Vector3(0.46f, 0.74f, -1.46f) },
            new MountPoint { Id = "mount_radiator", LocalPosition = new Vector3(0f, 0.62f, -1.78f) },
            new MountPoint { Id = "hub_fl", LocalPosition = new Vector3(-0.78f, 0.30f, -1.25f) },
            new MountPoint { Id = "hub_fr", LocalPosition = new Vector3( 0.78f, 0.30f, -1.25f) },
            new MountPoint { Id = "hub_rl", LocalPosition = new Vector3(-0.78f, 0.30f,  1.25f) },
            new MountPoint { Id = "hub_rr", LocalPosition = new Vector3( 0.78f, 0.30f,  1.25f) },
        };

        var parts = new[]
        {
            new PartDefinition
            {
                Id = Ids.EngineBlock,
                DisplayName = "engine block",
                Kind = PartKind.Engine,
                MassKg = 94f,
                MountId = "mount_engine",
                Bolts = Ring(6, 0.26f, -0.22f, ToolSize.Wrench17, 0.4f),
                Size = new Vector3(0.44f, 0.46f, 0.52f),
                ColorHex = "6d7378",
            },
            new PartDefinition
            {
                Id = Ids.CylinderHead,
                DisplayName = "cylinder head",
                Kind = PartKind.Engine,
                MassKg = 23f,
                MountId = "mount_head",
                RequiresSecured = new[] { Ids.EngineBlock },
                Bolts = Ring(8, 0.17f, -0.06f, ToolSize.Wrench14, 0.45f),
                Size = new Vector3(0.40f, 0.14f, 0.48f),
                ColorHex = "8a8f94",
            },
            new PartDefinition
            {
                Id = Ids.Carburetor,
                DisplayName = "carburettor",
                Kind = PartKind.Fuel,
                MassKg = 3.4f,
                MountId = "mount_carb",
                RequiresSecured = new[] { Ids.CylinderHead },
                Bolts = Ring(2, 0.08f, -0.05f, ToolSize.Wrench10, 0.7f),
                Size = new Vector3(0.14f, 0.16f, 0.14f),
                ColorHex = "b08d4a",
            },
            new PartDefinition
            {
                Id = Ids.SparkPlugs,
                DisplayName = "spark plugs",
                Kind = PartKind.Electrical,
                MassKg = 0.4f,
                MountId = "mount_plugs",
                RequiresSecured = new[] { Ids.CylinderHead },
                Bolts = new[]
                {
                    new BoltSpec { Id = "p1", LocalPosition = new Vector3(0f, 0f, -0.15f), Tool = ToolSize.SparkPlugWrench, TightenRate = 0.8f },
                    new BoltSpec { Id = "p2", LocalPosition = new Vector3(0f, 0f, -0.05f), Tool = ToolSize.SparkPlugWrench, TightenRate = 0.8f },
                    new BoltSpec { Id = "p3", LocalPosition = new Vector3(0f, 0f,  0.05f), Tool = ToolSize.SparkPlugWrench, TightenRate = 0.8f },
                    new BoltSpec { Id = "p4", LocalPosition = new Vector3(0f, 0f,  0.15f), Tool = ToolSize.SparkPlugWrench, TightenRate = 0.8f },
                },
                Size = new Vector3(0.05f, 0.09f, 0.40f),
                ColorHex = "d9d4c8",
            },
            new PartDefinition
            {
                Id = Ids.Clutch,
                DisplayName = "clutch",
                Kind = PartKind.Transmission,
                MassKg = 8.5f,
                MountId = "mount_clutch",
                RequiresSecured = new[] { Ids.EngineBlock },
                Bolts = Ring(6, 0.15f, 0f, ToolSize.Wrench12, 0.6f),
                Size = new Vector3(0.30f, 0.30f, 0.09f),
                ColorHex = "9b9ea3",
            },
            new PartDefinition
            {
                Id = Ids.Gearbox,
                DisplayName = "gearbox",
                Kind = PartKind.Transmission,
                MassKg = 32f,
                MountId = "mount_gearbox",
                RequiresSecured = new[] { Ids.Clutch },
                Bolts = Ring(8, 0.16f, -0.02f, ToolSize.Wrench14, 0.45f),
                Size = new Vector3(0.30f, 0.30f, 0.62f),
                ColorHex = "5f666c",
            },
            new PartDefinition
            {
                Id = Ids.Driveshaft,
                DisplayName = "driveshaft",
                Kind = PartKind.Transmission,
                MassKg = 9f,
                MountId = "mount_driveshaft",
                RequiresSecured = new[] { Ids.Gearbox },
                Bolts = new[]
                {
                    new BoltSpec { Id = "f1", LocalPosition = new Vector3(-0.06f, 0f, -0.62f), Tool = ToolSize.Wrench12, TightenRate = 0.65f },
                    new BoltSpec { Id = "f2", LocalPosition = new Vector3( 0.06f, 0f, -0.62f), Tool = ToolSize.Wrench12, TightenRate = 0.65f },
                    new BoltSpec { Id = "r1", LocalPosition = new Vector3(-0.06f, 0f,  0.62f), Tool = ToolSize.Wrench12, TightenRate = 0.65f },
                    new BoltSpec { Id = "r2", LocalPosition = new Vector3( 0.06f, 0f,  0.62f), Tool = ToolSize.Wrench12, TightenRate = 0.65f },
                },
                Size = new Vector3(0.10f, 0.10f, 1.35f),
                ColorHex = "7a7f85",
            },
            new PartDefinition
            {
                Id = Ids.Battery,
                DisplayName = "battery",
                Kind = PartKind.Electrical,
                MassKg = 14f,
                MountId = "mount_battery",
                Bolts = Ring(2, 0.10f, 0.06f, ToolSize.Wrench10, 0.75f),
                Size = new Vector3(0.24f, 0.19f, 0.17f),
                ColorHex = "1f2933",
            },
            new PartDefinition
            {
                Id = Ids.Radiator,
                DisplayName = "radiator",
                Kind = PartKind.Cooling,
                MassKg = 6.2f,
                MountId = "mount_radiator",
                Bolts = Ring(4, 0.22f, 0f, ToolSize.Wrench10, 0.7f),
                Size = new Vector3(0.56f, 0.40f, 0.07f),
                ColorHex = "4b5358",
            },
            Wheel(Ids.WheelFl, "front left wheel", "hub_fl"),
            Wheel(Ids.WheelFr, "front right wheel", "hub_fr"),
            Wheel(Ids.WheelRl, "rear left wheel", "hub_rl"),
            Wheel(Ids.WheelRr, "rear right wheel", "hub_rr"),
        };

        return new PartCatalog(parts, mounts);
    }
}
