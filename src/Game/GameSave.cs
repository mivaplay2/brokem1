using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// Saving is a serialisation of the model plus the handful of transforms the view
/// layer owns. Nothing about the scene tree is written out, so a save made before
/// a scene change still loads.
/// </summary>
public static class GameSave
{
    public const string Path = "user://autumn_car_save.json";

    public static string AbsolutePath => ProjectSettings.GlobalizePath(Path);

    public static bool Exists() => FileAccess.FileExists(Path);

    public static string Save(GameWorld world)
    {
        var data = SaveSystem.Capture(world.Sim);

        data.Transforms["car"] = Pack(world.Car.GlobalTransform);
        data.Transforms["player"] = Pack(world.Player.GlobalTransform);
        data.Flags["toolbox"] = world.Tools.HasToolbox ? "1" : "0";
        data.Flags["seated"] = world.Player.SeatedIn != null ? "1" : "0";

        foreach (var part in world.Parts.Values)
            if (!part.Installed)
                data.Transforms[$"part:{part.Def.Id}"] = Pack(part.GlobalTransform);

        foreach (var can in world.Cans)
        {
            data.Transforms[$"can:{can.Kind}"] = Pack(can.GlobalTransform);
            data.Flags[$"can:{can.Kind}:litres"] = can.Litres.ToString("R");
        }

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
            return $"could not write the save: {FileAccess.GetOpenError()}";

        file.StoreString(SaveSystem.ToJson(data));
        return $"saved to {AbsolutePath}";
    }

    public static string Load(GameWorld world)
    {
        if (!Exists()) return "no save file yet";

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null)
            return $"could not read the save: {FileAccess.GetOpenError()}";

        SaveData data;
        try
        {
            data = SaveSystem.FromJson(file.GetAsText());
            SaveSystem.Apply(data, world.Sim);
        }
        catch (System.Exception ex)
        {
            return $"save refused: {ex.Message}";
        }

        world.Player.ExitCar();
        world.Player.DropHeld();

        // Parts first: whether each one is fitted has already been restored into the
        // model, so here the nodes are only being told to agree with it.
        foreach (var part in world.Parts.Values)
        {
            part.DetachFromMount();
            if (part.State.Installed)
            {
                var mount = world.Car.MountNodeFor(part.Def.MountId);
                if (mount != null) part.AttachToMount(mount);
            }
            else if (data.Transforms.TryGetValue($"part:{part.Def.Id}", out float[] t))
            {
                part.GlobalTransform = Unpack(t);
                part.LinearVelocity = Vector3.Zero;
                part.AngularVelocity = Vector3.Zero;
            }
        }

        foreach (var can in world.Cans)
        {
            if (data.Transforms.TryGetValue($"can:{can.Kind}", out float[] t))
                can.GlobalTransform = Unpack(t);
            if (data.Flags.TryGetValue($"can:{can.Kind}:litres", out string litres)
                && float.TryParse(litres, out float value))
                can.Litres = value;
        }

        if (data.Transforms.TryGetValue("car", out float[] carTransform))
        {
            world.Car.GlobalTransform = Unpack(carTransform);
            world.Car.LinearVelocity = Vector3.Zero;
            world.Car.AngularVelocity = Vector3.Zero;
        }

        if (data.Transforms.TryGetValue("player", out float[] playerTransform))
            world.Player.GlobalTransform = Unpack(playerTransform);

        world.Tools.HasToolbox = data.Flags.TryGetValue("toolbox", out string box) && box == "1";
        world.Tools.Clear();
        world.Car.OnAssemblyChanged();

        return "loaded";
    }

    private static float[] Pack(Transform3D t) => new[]
    {
        t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z,
        t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z,
        t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z,
        t.Origin.X, t.Origin.Y, t.Origin.Z,
    };

    private static Transform3D Unpack(IReadOnlyList<float> f)
    {
        if (f == null || f.Count < 12) return Transform3D.Identity;
        var basis = new Basis(
            new Vector3(f[0], f[1], f[2]),
            new Vector3(f[3], f[4], f[5]),
            new Vector3(f[6], f[7], f[8]));
        return new Transform3D(basis, new Vector3(f[9], f[10], f[11]));
    }
}
