using System;
using System.Collections.Generic;

namespace MyAutumnCar.Tests;

/// <summary>Dependency-free assertion harness so the sim tests run with a bare SDK.</summary>
public static class Harness
{
    private static readonly List<string> Failures = new();
    private static int _checks;
    private static int _cases;
    private static string _current = "";

    public static void Case(string name, Action body)
    {
        _cases++;
        _current = name;
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Record(bool ok, string detail)
    {
        _checks++;
        if (!ok) Failures.Add($"{_current}: {detail}");
    }

    public static void True(bool value, string what) =>
        Record(value, $"expected true — {what}");

    public static void False(bool value, string what) =>
        Record(!value, $"expected false — {what}");

    public static void Equal<T>(T expected, T actual, string what) =>
        Record(EqualityComparer<T>.Default.Equals(expected, actual),
               $"{what}: expected {expected}, got {actual}");

    public static void Near(float expected, float actual, float tolerance, string what) =>
        Record(MathF.Abs(expected - actual) <= tolerance,
               $"{what}: expected {expected} +/- {tolerance}, got {actual}");

    public static void InRange(float low, float high, float actual, string what) =>
        Record(actual >= low && actual <= high,
               $"{what}: expected {low}..{high}, got {actual}");

    public static void Greater(float threshold, float actual, string what) =>
        Record(actual > threshold, $"{what}: expected > {threshold}, got {actual}");

    public static void Less(float threshold, float actual, string what) =>
        Record(actual < threshold, $"{what}: expected < {threshold}, got {actual}");

    public static int Report()
    {
        Console.WriteLine();
        if (Failures.Count == 0)
        {
            Console.WriteLine($"PASS  {_cases} cases, {_checks} checks");
            return 0;
        }
        Console.WriteLine($"FAIL  {Failures.Count} of {_checks} checks in {_cases} cases");
        foreach (string f in Failures) Console.WriteLine($"  - {f}");
        return 1;
    }
}
