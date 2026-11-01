namespace MyAutumnCar.Sim;

/// <summary>
/// Fastener drivers. A bolt only turns when the player holds the matching tool,
/// which is the core friction of the assembly loop: finding the right spanner.
/// </summary>
public enum ToolSize
{
    None = 0,
    Wrench8,
    Wrench10,
    Wrench12,
    Wrench14,
    Wrench17,
    SparkPlugWrench,
    Screwdriver,
}

public static class ToolSizeNames
{
    public static string Label(ToolSize t) => t switch
    {
        ToolSize.None => "bare hands",
        ToolSize.Wrench8 => "8 mm spanner",
        ToolSize.Wrench10 => "10 mm spanner",
        ToolSize.Wrench12 => "12 mm spanner",
        ToolSize.Wrench14 => "14 mm spanner",
        ToolSize.Wrench17 => "17 mm spanner",
        ToolSize.SparkPlugWrench => "spark plug wrench",
        ToolSize.Screwdriver => "screwdriver",
        _ => t.ToString(),
    };
}
