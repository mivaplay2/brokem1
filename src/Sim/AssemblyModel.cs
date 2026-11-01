using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MyAutumnCar.Sim;

public enum AssemblyResult
{
    Ok,
    UnknownPart,
    AlreadyInstalled,
    NotInstalled,
    MissingDependency,
    StillBolted,
    BlockedByDependent,
    WrongTool,
    PartDestroyed,
}

/// <summary>
/// The car as data. This is the single source of truth about what is bolted to
/// what; the physics bodies and meshes only mirror it. Everything the mechanical
/// simulation needs to know it asks here, which is what makes the sim testable
/// without launching the game and what makes a save a plain serialisation.
/// </summary>
public sealed class AssemblyModel
{
    /// <summary>Bare chassis, with the gearbox and axles treated as fixed for the slice.</summary>
    public const float ChassisMassKg = 640f;

    private static readonly Vector3 ChassisComLocal = new(0f, 0.46f, 0.12f);

    private readonly Dictionary<string, PartState> _states;

    public PartCatalog Catalog { get; }

    public AssemblyModel(PartCatalog catalog)
    {
        Catalog = catalog;
        _states = catalog.All.ToDictionary(d => d.Id, PartState.ForDefinition);
    }

    public IReadOnlyDictionary<string, PartState> Parts => _states;

    public PartState Get(string id) =>
        _states.TryGetValue(id, out var s)
            ? s
            : throw new KeyNotFoundException($"unknown part '{id}'");

    public bool TryGetState(string id, out PartState state) => _states.TryGetValue(id, out state);

    public bool IsInstalled(string id) => _states.TryGetValue(id, out var s) && s.Installed;
    public bool IsSecured(string id) => _states.TryGetValue(id, out var s) && s.Secured;
    public bool IsFunctional(string id) => _states.TryGetValue(id, out var s) && s.Functional;

    public IEnumerable<PartState> Installed => _states.Values.Where(s => s.Installed);
    public IEnumerable<PartState> Loose => _states.Values.Where(s => !s.Installed);

    // ---------------------------------------------------------------- install

    /// <summary>Why a part cannot go on yet, or null when it can.</summary>
    public string InstallBlocker(string id)
    {
        if (!Catalog.TryGet(id, out var def)) return $"unknown part '{id}'";
        var state = Get(id);
        if (state.Installed) return $"{def.DisplayName} is already fitted";
        if (state.Wear >= 1f) return $"{def.DisplayName} is scrap";

        foreach (string dep in def.RequiresSecured)
        {
            if (!IsInstalled(dep))
                return $"{Catalog.Get(dep).DisplayName} has to go on first";
            if (!IsSecured(dep))
                return $"{Catalog.Get(dep).DisplayName} is not bolted down yet";
        }
        return null;
    }

    public bool CanInstall(string id) => InstallBlocker(id) == null;

    public AssemblyResult TryInstall(string id, out string message)
    {
        message = InstallBlocker(id);
        if (!Catalog.TryGet(id, out var def))
            return AssemblyResult.UnknownPart;

        var state = Get(id);
        if (state.Installed) return AssemblyResult.AlreadyInstalled;
        if (state.Wear >= 1f) return AssemblyResult.PartDestroyed;
        if (message != null) return AssemblyResult.MissingDependency;

        state.Installed = true;
        Array.Clear(state.BoltTightness, 0, state.BoltTightness.Length);
        message = def.NeedsBolts
            ? $"{def.DisplayName} fitted — {def.BoltCount} bolts loose, {ToolSizeNames.Label(def.Bolts[0].Tool)}"
            : $"{def.DisplayName} fitted";
        return AssemblyResult.Ok;
    }

    // ----------------------------------------------------------------- remove

    public IEnumerable<string> InstalledDependentsOf(string id) =>
        Catalog.All
            .Where(d => d.RequiresSecured.Contains(id) && IsInstalled(d.Id))
            .Select(d => d.Id);

    public string RemoveBlocker(string id)
    {
        if (!Catalog.TryGet(id, out var def)) return $"unknown part '{id}'";
        var state = Get(id);
        if (!state.Installed) return $"{def.DisplayName} is not fitted";
        if (!state.AllBoltsLoose()) return $"{def.DisplayName} is still bolted down";

        string dependent = InstalledDependentsOf(id).FirstOrDefault();
        if (dependent != null)
            return $"take the {Catalog.Get(dependent).DisplayName} off first";
        return null;
    }

    public bool CanRemove(string id) => RemoveBlocker(id) == null;

    public AssemblyResult TryRemove(string id, out string message)
    {
        message = RemoveBlocker(id);
        if (!Catalog.TryGet(id, out var def))
            return AssemblyResult.UnknownPart;

        var state = Get(id);
        if (!state.Installed) return AssemblyResult.NotInstalled;
        if (!state.AllBoltsLoose()) return AssemblyResult.StillBolted;
        if (message != null) return AssemblyResult.BlockedByDependent;

        state.Installed = false;
        message = $"{def.DisplayName} removed";
        return AssemblyResult.Ok;
    }

    // ------------------------------------------------------------------ bolts

    /// <summary>
    /// Turns one bolt. <paramref name="direction"/> is +1 to tighten, -1 to loosen.
    /// Returns the tightness actually applied this call.
    /// </summary>
    public float TurnBolt(string partId, int boltIndex, ToolSize heldTool, float dt, int direction, out string message)
    {
        message = null;
        if (!Catalog.TryGet(partId, out var def)) { message = $"unknown part '{partId}'"; return 0f; }
        var state = Get(partId);

        if (!state.Installed) { message = $"{def.DisplayName} is not fitted"; return 0f; }
        if (boltIndex < 0 || boltIndex >= def.BoltCount) { message = "no such bolt"; return 0f; }

        var bolt = def.Bolts[boltIndex];
        if (heldTool != bolt.Tool)
        {
            message = heldTool == ToolSize.None
                ? $"needs a {ToolSizeNames.Label(bolt.Tool)}"
                : $"wrong tool — needs a {ToolSizeNames.Label(bolt.Tool)}";
            return 0f;
        }

        float before = state.BoltTightness[boltIndex];
        float after = Math.Clamp(before + bolt.TightenRate * dt * Math.Sign(direction), 0f, 1f);
        state.BoltTightness[boltIndex] = after;
        return after - before;
    }

    /// <summary>Test and setup helper: bolt a fitted part all the way down.</summary>
    public void SecureAll(string partId)
    {
        var state = Get(partId);
        for (int i = 0; i < state.BoltTightness.Length; i++)
            state.BoltTightness[i] = 1f;
    }

    /// <summary>Test and setup helper: fit a part and torque every bolt.</summary>
    public AssemblyResult InstallAndSecure(string partId, out string message)
    {
        var r = TryInstall(partId, out message);
        if (r == AssemblyResult.Ok) SecureAll(partId);
        return r;
    }

    // ------------------------------------------------------------------- mass

    public float TotalMassKg
    {
        get
        {
            float m = ChassisMassKg;
            foreach (var s in _states.Values)
                if (s.Installed)
                    m += Catalog.Get(s.DefinitionId).MassKg;
            return m;
        }
    }

    /// <summary>
    /// Mass-weighted centre of mass in body-local space. This is why a car with no
    /// engine in it feels light at the nose and squats on its back wheels.
    /// </summary>
    public Vector3 CenterOfMassLocal
    {
        get
        {
            Vector3 weighted = ChassisComLocal * ChassisMassKg;
            float mass = ChassisMassKg;

            foreach (var s in _states.Values)
            {
                if (!s.Installed) continue;
                var def = Catalog.Get(s.DefinitionId);
                var mount = Catalog.GetMount(def.MountId);
                weighted += mount.LocalPosition * def.MassKg;
                mass += def.MassKg;
            }
            return weighted / mass;
        }
    }

    // ------------------------------------------------------------- diagnosis

    /// <summary>Engine-critical parts that are not yet fitted and bolted, in build order.</summary>
    public IEnumerable<string> MissingForEngine()
    {
        foreach (string id in PartCatalog.EngineCritical)
            if (!IsFunctional(id))
                yield return id;
        if (!IsFunctional(PartCatalog.Ids.Battery))
            yield return PartCatalog.Ids.Battery;
    }

    public IEnumerable<string> MissingWheels() =>
        PartCatalog.Wheels.Where(id => !IsFunctional(id));

    /// <summary>Human-readable next job, for the HUD.</summary>
    public string NextJobHint()
    {
        foreach (string id in PartCatalog.BuildOrder)
        {
            var def = Catalog.Get(id);
            var st = Get(id);
            if (!st.Installed) return $"fit the {def.DisplayName}";
            if (st.AnyBoltLoose())
                return $"bolt the {def.DisplayName} ({st.SecuredBoltCount()}/{def.BoltCount}, {ToolSizeNames.Label(def.Bolts[0].Tool)})";
        }
        return "nothing left to bolt";
    }
}
