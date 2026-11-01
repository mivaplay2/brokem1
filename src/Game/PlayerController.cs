using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// First-person player. Carries at most one thing in its hands, which is enough
/// friction to make fetching a part feel like a trip rather than a menu click,
/// and keeps the spanners on a separate belt so you are not juggling them.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    private const float WalkSpeed = 3.1f;
    private const float SprintSpeed = 5.4f;
    private const float Acceleration = 12f;
    private const float JumpSpeed = 4.2f;
    private const float Gravity = 9.8f;
    private const float MouseSensitivity = 0.0022f;
    private const float PitchLimit = 1.45f;
    private const float SeatedYawLimit = 2.2f;

    public Camera3D Head { get; private set; }
    public Node3D Hand { get; private set; }
    public Interactor Aim { get; private set; }
    public CarryableNode Held { get; private set; }
    public CarNode SeatedIn { get; private set; }

    private float _yaw;
    private float _pitch;
    private CollisionShape3D _shape;
    private MeshInstance3D _body;

    public static PlayerController Create(Vector3 position)
    {
        var player = new PlayerController
        {
            Name = "Player",
            Position = position,
            CollisionLayer = Layers.Player,
            CollisionMask = Layers.PlayerMask,
            FloorMaxAngle = Mathf.DegToRad(52f),
        };

        var capsule = new CapsuleShape3D { Radius = 0.32f, Height = 1.78f };
        player._shape = new CollisionShape3D { Name = "Shape", Shape = capsule, Position = new Vector3(0f, 0.89f, 0f) };
        player.AddChild(player._shape);

        player._body = Build.Box(new Vector3(0.4f, 1.7f, 0.3f), Build.Material("3c4a52", 0.9f, 0f), "Body");
        player._body.Position = new Vector3(0f, 0.88f, 0f);
        player._body.Visible = false;   // first person; it exists for shadows later
        player.AddChild(player._body);

        player.Head = new Camera3D { Name = "Head", Position = new Vector3(0f, 1.66f, 0f), Fov = 78f };
        player.AddChild(player.Head);

        player.Hand = new Node3D { Name = "Hand", Position = new Vector3(0.30f, -0.26f, -0.62f) };
        player.Head.AddChild(player.Hand);

        player.Aim = new Interactor(player.Head);
        player.Aim.AddException(player);
        return player;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -PitchLimit, PitchLimit);
            if (SeatedIn != null) _yaw = Mathf.Clamp(_yaw, -SeatedYawLimit, SeatedYawLimit);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Head.Rotation = new Vector3(_pitch, 0f, 0f);

        if (SeatedIn != null)
        {
            // Riding the car: position comes from the seat, the head still turns.
            var seat = SeatedIn.DriverView.GlobalTransform;
            seat.Basis *= Basis.FromEuler(new Vector3(0f, _yaw, 0f));
            GlobalTransform = seat;
            Velocity = Vector3.Zero;
            return;
        }

        Rotation = new Vector3(0f, _yaw, 0f);

        Vector3 velocity = Velocity;
        if (!IsOnFloor()) velocity.Y -= Gravity * dt;
        else if (Input.IsActionJustPressed(GameInput.Jump)) velocity.Y = JumpSpeed;
        else velocity.Y = Mathf.Min(velocity.Y, 0f);

        var input = new Vector2(
            Input.GetActionStrength(GameInput.MoveRight) - Input.GetActionStrength(GameInput.MoveLeft),
            Input.GetActionStrength(GameInput.MoveBack) - Input.GetActionStrength(GameInput.MoveForward));

        if (input.LengthSquared() > 1f) input = input.Normalized();

        float speed = Input.IsActionPressed(GameInput.Sprint) ? SprintSpeed : WalkSpeed;
        // Carrying an engine block at a jog is not a thing.
        if (Held != null) speed *= Mathf.Lerp(1f, 0.52f, Mathf.Clamp(Held.Mass / 90f, 0f, 1f));

        Vector3 wish = (Transform.Basis * new Vector3(input.X, 0f, input.Y)) * speed;
        velocity.X = Mathf.MoveToward(velocity.X, wish.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, wish.Z, Acceleration * dt);

        Velocity = velocity;
        MoveAndSlide();
    }

    // ------------------------------------------------------------------ hands

    public void PickUp(CarryableNode item)
    {
        if (item == null || Held != null) return;
        Held = item;
        item.OnPickedUp(Hand);
    }

    /// <summary>Hands the item off without throwing it — used when a part is fitted.</summary>
    public void ReleaseHeld() => Held = null;

    public void DropHeld()
    {
        if (Held == null) return;
        Vector3 toss = Velocity + (-Head.GlobalTransform.Basis.Z) * 1.2f;
        Held.OnDropped(toss);
        Held = null;
    }

    // -------------------------------------------------------------------- car

    public void EnterCar(CarNode car)
    {
        if (car == null || SeatedIn != null) return;
        DropHeld();
        SeatedIn = car;
        car.DriverSeated = true;
        _yaw = 0f;
        _shape.Disabled = true;
    }

    public void ExitCar()
    {
        if (SeatedIn == null) return;
        var car = SeatedIn;
        car.DriverSeated = false;
        car.Sim.Engine.Throttle = 0f;
        car.Sim.Engine.StarterEngaged = false;

        GlobalPosition = car.ExitPoint.GlobalPosition;
        _yaw = car.GlobalRotation.Y;
        _shape.Disabled = false;
        SeatedIn = null;
    }
}
