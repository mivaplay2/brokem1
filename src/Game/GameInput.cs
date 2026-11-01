using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// Input actions are registered in code rather than hand-written into
/// project.godot, so the bindings are reviewable and stay in sync with the
/// controls the HUD advertises.
/// </summary>
public static class GameInput
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Sprint = "sprint";
    public const string Jump = "jump";

    public const string Interact = "interact";
    public const string Drop = "drop";
    public const string Tighten = "tighten";
    public const string Loosen = "loosen";

    public const string Throttle = "car_throttle";
    public const string Brake = "car_brake";
    public const string SteerLeft = "car_steer_left";
    public const string SteerRight = "car_steer_right";
    public const string Clutch = "car_clutch";
    public const string Starter = "car_starter";
    public const string Ignition = "car_ignition";
    public const string ChokeOut = "car_choke_out";
    public const string ChokeIn = "car_choke_in";
    public const string ShiftUp = "car_shift_up";
    public const string ShiftDown = "car_shift_down";
    public const string Neutral = "car_neutral";
    public const string Handbrake = "car_handbrake";
    public const string ExitCar = "car_exit";

    public const string SaveGame = "save_game";
    public const string LoadGame = "load_game";
    public const string ToggleHelp = "toggle_help";
    public const string ReleaseMouse = "release_mouse";

    public static readonly (string Action, Key Key)[] ToolSlots =
    {
        ("tool_1", Key.Key1),
        ("tool_2", Key.Key2),
        ("tool_3", Key.Key3),
        ("tool_4", Key.Key4),
        ("tool_5", Key.Key5),
        ("tool_6", Key.Key6),
        ("tool_7", Key.Key7),
    };

    public static void Register()
    {
        BindKey(MoveForward, Key.W);
        BindKey(MoveBack, Key.S);
        BindKey(MoveLeft, Key.A);
        BindKey(MoveRight, Key.D);
        BindKey(Sprint, Key.Shift);
        BindKey(Jump, Key.Space);

        BindKey(Interact, Key.E);
        BindKey(Drop, Key.G);
        BindMouse(Tighten, MouseButton.Left);
        BindMouse(Loosen, MouseButton.Right);

        BindKey(Throttle, Key.W);
        BindKey(Brake, Key.S);
        BindKey(SteerLeft, Key.A);
        BindKey(SteerRight, Key.D);
        BindKey(Clutch, Key.Shift);
        BindKey(Starter, Key.R);
        BindKey(Ignition, Key.I);
        BindKey(ChokeOut, Key.Bracketright);
        BindKey(ChokeIn, Key.Bracketleft);
        BindKey(ShiftUp, Key.X);
        BindKey(ShiftDown, Key.Z);
        BindKey(Neutral, Key.N);
        BindKey(Handbrake, Key.Space);
        BindKey(ExitCar, Key.F);

        BindKey(SaveGame, Key.F5);
        BindKey(LoadGame, Key.F9);
        BindKey(ToggleHelp, Key.F1);
        BindKey(ReleaseMouse, Key.Escape);

        foreach (var (action, key) in ToolSlots)
            BindKey(action, key);
    }

    private static void BindKey(string action, Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = key };
        InputMap.ActionAddEvent(action, ev);
    }

    private static void BindMouse(string action, MouseButton button)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventMouseButton { ButtonIndex = button };
        InputMap.ActionAddEvent(action, ev);
    }
}
