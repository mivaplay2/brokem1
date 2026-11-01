using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// Placeholder geometry. Every mesh in the slice is a primitive built here, so the
/// project runs with no art pipeline at all; swapping in real models later means
/// replacing these calls, not rewriting the game logic that references the parts.
/// </summary>
public static class Build
{
    public static StandardMaterial3D Material(string hex, float roughness = 0.65f, float metallic = 0.25f)
    {
        var color = Color.FromHtml(hex);
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
        };
    }

    public static StandardMaterial3D Emissive(string hex, float energy = 0.6f)
    {
        var mat = Material(hex, 0.4f, 0.1f);
        mat.EmissionEnabled = true;
        mat.Emission = Color.FromHtml(hex);
        mat.EmissionEnergyMultiplier = energy;
        return mat;
    }

    public static MeshInstance3D Box(Vector3 size, StandardMaterial3D material, string name = "Mesh")
    {
        var mesh = new BoxMesh { Size = size };
        return new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
    }

    public static MeshInstance3D Cylinder(float radius, float height, StandardMaterial3D material,
                                          string name = "Mesh", int segments = 16)
    {
        var mesh = new CylinderMesh
        {
            TopRadius = radius,
            BottomRadius = radius,
            Height = height,
            RadialSegments = segments,
            Rings = 1,
        };
        return new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
    }

    public static CollisionShape3D BoxShape(Vector3 size, string name = "Shape") =>
        new() { Name = name, Shape = new BoxShape3D { Size = size } };

    public static CollisionShape3D CylinderShape(float radius, float height, string name = "Shape") =>
        new() { Name = name, Shape = new CylinderShape3D { Radius = radius, Height = height } };

    /// <summary>A static block of world: floor slab, wall, bench top.</summary>
    public static StaticBody3D Solid(Vector3 size, Vector3 position, string hex, string name)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = Layers.World,
            CollisionMask = 0,
        };
        body.AddChild(Box(size, Material(hex, 0.9f, 0.0f)));
        body.AddChild(BoxShape(size));
        return body;
    }

    /// <summary>A raycast-only interaction volume that follows whatever it is parented to.</summary>
    public static Area3D Touch(Vector3 size, string name = "Touch")
    {
        var area = new Area3D
        {
            Name = name,
            CollisionLayer = Layers.Interact,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
        };
        area.AddChild(BoxShape(size));
        return area;
    }
}
