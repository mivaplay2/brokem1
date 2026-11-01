using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// Entry point. Owns the world and routes input to whatever the crosshair is on.
/// </summary>
public partial class Main : Node3D
{
    private GameWorld _world;

    public override void _Ready()
    {
        GameInput.Register();

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0)
        {
            AddChild(new HeadlessSmokeTest { Name = "SmokeTest" });
            return;
        }

        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;

        _world = GameWorld.Create(this);
    }

    public override void _Process(double delta)
    {
        if (_world == null) return;   // the headless scene test drives itself

        float dt = (float)delta;
        var ctx = _world.Context;
        var player = _world.Player;

        player.Aim.Poll(ctx);
        var target = player.Aim.Target;

        if (player.SeatedIn == null)
        {
            if (Input.IsActionJustPressed(GameInput.Interact)) target?.Use(ctx);
            if (Input.IsActionPressed(GameInput.Interact)) target?.Hold(ctx, dt);
            if (Input.IsActionPressed(GameInput.Tighten)) target?.Turn(ctx, 1, dt);
            if (Input.IsActionPressed(GameInput.Loosen)) target?.Turn(ctx, -1, dt);
            if (Input.IsActionJustPressed(GameInput.Drop)) player.DropHeld();

            for (int slot = 0; slot < GameInput.ToolSlots.Length; slot++)
            {
                if (!Input.IsActionJustPressed(GameInput.ToolSlots[slot].Action)) continue;
                if (_world.Tools.Select(slot)) ctx.Say($"tool: {_world.Tools.SelectedLabel}");
                else ctx.Say("you need the toolbox off the bench first");
            }
        }
        else if (Input.IsActionJustPressed(GameInput.ExitCar))
        {
            player.ExitCar();
        }

        if (Input.IsActionJustPressed(GameInput.SaveGame)) ctx.Say(GameSave.Save(_world));
        if (Input.IsActionJustPressed(GameInput.LoadGame)) ctx.Say(GameSave.Load(_world));
        if (Input.IsActionJustPressed(GameInput.ToggleHelp)) _world.Hud.ToggleHelp();

        if (Input.IsActionJustPressed(GameInput.ReleaseMouse))
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;

        _world.Hud.Refresh(ctx, player.Aim.Prompt);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) GetTree().Quit();
    }
}
