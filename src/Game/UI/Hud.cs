using System.Collections.Generic;
using Godot;
using MyAutumnCar.Sim;

namespace MyAutumnCar.Game;

/// <summary>
/// Reads the simulation and says what it finds. Every number on screen comes
/// straight out of the model each frame, so the gauges cannot drift away from
/// what the engine is actually doing.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float FlashSeconds = 4.2f;
    private const int MaxFlashLines = 4;

    private Label _crosshair;
    private Label _prompt;
    private Label _left;
    private Label _gauges;
    private Label _flash;
    private Label _help;
    private ColorRect _helpBackdrop;

    private readonly List<(string Text, float Remaining)> _messages = new();

    public static Hud Create()
    {
        var hud = new Hud { Name = "Hud" };

        hud._crosshair = MakeLabel(18, HorizontalAlignment.Center, VerticalAlignment.Center);
        hud._crosshair.Text = "+";
        hud.AddChild(hud._crosshair);

        hud._prompt = MakeLabel(16, HorizontalAlignment.Center, VerticalAlignment.Center);
        hud._prompt.OffsetTop = 58;
        hud.AddChild(hud._prompt);

        hud._left = MakeLabel(15, HorizontalAlignment.Left, VerticalAlignment.Top);
        hud.AddChild(hud._left);

        hud._gauges = MakeLabel(15, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        hud.AddChild(hud._gauges);

        hud._flash = MakeLabel(14, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        hud.AddChild(hud._flash);

        hud._helpBackdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f), Visible = false };
        hud._helpBackdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(hud._helpBackdrop);

        hud._help = MakeLabel(15, HorizontalAlignment.Left, VerticalAlignment.Center);
        hud._help.OffsetLeft = 64;
        hud._help.Visible = false;
        hud._help.Text = HelpText;
        hud.AddChild(hud._help);

        return hud;
    }

    private static Label MakeLabel(int size, HorizontalAlignment h, VerticalAlignment v)
    {
        var label = new Label
        {
            HorizontalAlignment = h,
            VerticalAlignment = v,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                FontSize = size,
                FontColor = new Color(0.94f, 0.95f, 0.96f),
                OutlineSize = 5,
                OutlineColor = new Color(0f, 0f, 0f, 0.85f),
            },
        };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 20;
        label.OffsetTop = 18;
        label.OffsetRight = -20;
        label.OffsetBottom = -18;
        return label;
    }

    public void Flash(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (_messages.Count > 0 && _messages[^1].Text == message)
        {
            _messages[^1] = (message, FlashSeconds);
            return;
        }
        _messages.Add((message, FlashSeconds));
        while (_messages.Count > MaxFlashLines) _messages.RemoveAt(0);
    }

    public void ToggleHelp()
    {
        _help.Visible = !_help.Visible;
        _helpBackdrop.Visible = _help.Visible;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            var (text, remaining) = _messages[i];
            remaining -= dt;
            if (remaining <= 0f) _messages.RemoveAt(i);
            else _messages[i] = (text, remaining);
        }

        var sb = new System.Text.StringBuilder();
        foreach (var (text, _) in _messages) sb.AppendLine(text);
        _flash.Text = sb.ToString().TrimEnd();
    }

    public void Refresh(PlayerContext ctx, string prompt)
    {
        _prompt.Text = prompt ?? "";
        _crosshair.Text = prompt == null ? "+" : "[+]";

        var sim = ctx.Sim;
        bool seated = ctx.Player.SeatedIn != null;

        _left.Text =
            $"tool    {ctx.Tools.SelectedLabel}\n" +
            $"hands   {(ctx.Held == null ? "empty" : ctx.Held.DisplayName)}\n" +
            $"next    {sim.Assembly.NextJobHint()}\n" +
            $"\nF1 controls   F5 save   F9 load";

        _gauges.Text = seated ? DrivingGauges(sim, ctx.Car) : StandingGauges(sim);
    }

    private static string DrivingGauges(CarSim sim, CarNode car)
    {
        var e = sim.Engine;
        var d = sim.Drivetrain;

        string warnings = "";
        if (e.Seized) warnings += "  SEIZED";
        else
        {
            if (e.OilWarning) warnings += "  OIL";
            if (e.TempWarning) warnings += "  TEMP";
            if (e.HeadGasketWear > 0.02f) warnings += $"  GASKET {e.HeadGasketWear * 100f:0}%";
            if (e.PistonWear > 0.02f) warnings += $"  PISTONS {e.PistonWear * 100f:0}%";
        }

        return
            $"{e.Status}{warnings}\n" +
            $"{e.Rpm,5:0} rpm    gear {d.GearLabel}    {car.SpeedKmh,5:0.0} km/h\n" +
            $"oil {e.OilPressureBar:0.00} bar    water {e.CoolantTempC:0} C    petrol {sim.Fluids.FuelL:0.0} l\n" +
            $"battery {sim.Fluids.BatteryCharge * 100f:0}%    choke {e.Choke * 100f:0}%    clutch {d.ClutchPedal * 100f:0}%\n" +
            $"wheels on the ground {car.GroundedWheelCount()}/4\n" +
            $"I ignition   hold R starter   [ ] choke   Z/X gear   shift clutch   F out";
    }

    private static string StandingGauges(CarSim sim)
    {
        var e = sim.Engine;
        string condition = e.Seized ? "engine: seized" : $"engine: {e.Status}";
        return
            $"{condition}\n" +
            $"petrol {sim.Fluids.FuelL:0.0}/{FluidTanks.FuelCapacityL:0} l    " +
            $"oil {sim.Fluids.OilL:0.0}/{FluidTanks.OilCapacityL:0.0} l    " +
            $"water {sim.Fluids.CoolantL:0.0}/{FluidTanks.CoolantCapacityL:0} l\n" +
            $"battery {sim.Fluids.BatteryCharge * 100f:0}%    water temp {e.CoolantTempC:0} C";
    }

    private const string HelpText =
        "my autumn car — vertical slice\n" +
        "\n" +
        "on foot\n" +
        "  WASD move, shift run, space jump\n" +
        "  E pick up / fit / take off / get in, hold E to pour\n" +
        "  G drop what you are carrying\n" +
        "  1-7 pick a spanner (after you take the toolbox)\n" +
        "      1 8mm   2 10mm   3 12mm   4 14mm   5 17mm   6 plug spanner   7 screwdriver\n" +
        "  left mouse tighten a bolt, right mouse loosen it\n" +
        "\n" +
        "driving\n" +
        "  I ignition on/off, hold R to crank the starter\n" +
        "  [ and ] push the choke in and pull it out\n" +
        "  W throttle, S brake, A/D steer, space handbrake\n" +
        "  shift clutch, Z down a gear, X up a gear, N neutral\n" +
        "  F get out\n" +
        "\n" +
        "building it\n" +
        "  the block goes in first, then the head on top of it, then the\n" +
        "  carburettor and the plugs. a part will not go on until the part\n" +
        "  underneath it is bolted down, and every bolt wants its own spanner.\n" +
        "  pour oil in before you start it, or you will cook the pistons.\n" +
        "  pull the choke out to start it cold and push it back in as it warms.\n" +
        "\n" +
        "F1 close   F5 save   F9 load   escape release the mouse";
}
