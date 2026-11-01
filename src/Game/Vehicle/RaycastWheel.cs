using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// One corner: a raycast suspension strut and a tyre. Five of these on a body
/// behave better than a built-in vehicle component for a car whose mass and
/// balance change while you are building it, because nothing here caches the
/// body's properties.
/// </summary>
public partial class RaycastWheel : Node3D
{
    public string PartId { get; private set; }
    public bool Driven { get; private set; }
    public bool Steers { get; private set; }

    public float Radius { get; set; } = 0.29f;
    public float RestLength { get; set; } = 0.34f;
    public float Travel { get; set; } = 0.17f;
    public float StiffnessNPerM { get; set; } = 46000f;
    public float DampingNPerMs { get; set; } = 4200f;
    public float Friction { get; set; } = 1.15f;
    public float LateralGainPerMs { get; set; } = 1.1f;
    public float RollingResistance { get; set; } = 0.014f;

    public float SteerAngle { get; set; }
    public float DriveTorqueNm { get; set; }
    public float BrakeTorqueNm { get; set; }

    public bool Grounded { get; private set; }
    public float LoadN { get; private set; }
    public float ForwardSpeedMs { get; private set; }
    public float SpringLength { get; private set; }

    /// <summary>Where the wheel and its mount marker hang, moved by the suspension.</summary>
    public Node3D Anchor { get; private set; }

    private RayCast3D _ray;

    public static RaycastWheel Create(string partId, Vector3 hubLocalPosition, bool driven, bool steers)
    {
        var wheel = new RaycastWheel
        {
            Name = $"Wheel_{partId}",
            PartId = partId,
            Driven = driven,
            Steers = steers,
        };
        // The node sits at the top of the strut so the hub rests at the catalog
        // position with the spring at its natural length.
        wheel.Position = hubLocalPosition + Vector3.Up * wheel.RestLength;
        wheel.SpringLength = wheel.RestLength;
        return wheel;
    }

    public void Attach(CarNode car)
    {
        _ray = new RayCast3D
        {
            Name = "Ray",
            TargetPosition = Vector3.Down * (RestLength + Radius),
            CollisionMask = Layers.GroundRayMask,
            Enabled = true,
            HitFromInside = false,
        };
        _ray.AddException(car);
        AddChild(_ray);

        Anchor = new Node3D { Name = "Anchor", Position = Vector3.Down * RestLength };
        AddChild(Anchor);
    }

    /// <summary>
    /// Applies this corner's forces for one tick. <paramref name="fitted"/> is the
    /// simulation's verdict on whether there is actually a bolted wheel here; an
    /// unbolted corner produces nothing and the car drops onto its chassis.
    /// </summary>
    public void Simulate(CarNode car, float dt, bool fitted)
    {
        DriveTorqueNm = fitted ? DriveTorqueNm : 0f;

        if (!fitted)
        {
            Grounded = false;
            LoadN = 0f;
            ForwardSpeedMs = 0f;
            SpringLength = RestLength;
            if (Anchor != null) Anchor.Position = Vector3.Down * RestLength;
            return;
        }

        _ray.ForceRaycastUpdate();
        if (!_ray.IsColliding())
        {
            Grounded = false;
            LoadN = 0f;
            SpringLength = RestLength;
            Anchor.Position = Vector3.Down * RestLength;
            ForwardSpeedMs = ProjectedForwardSpeed(car, GlobalPosition, Vector3.Up);
            return;
        }

        Grounded = true;
        Vector3 contact = _ray.GetCollisionPoint();
        Vector3 normal = _ray.GetCollisionNormal();
        Vector3 up = GlobalTransform.Basis.Y;

        float distance = GlobalPosition.DistanceTo(contact);
        SpringLength = Mathf.Clamp(distance - Radius, RestLength - Travel, RestLength);
        Anchor.Position = Vector3.Down * SpringLength;

        float compression = RestLength - SpringLength;

        Vector3 comGlobal = car.GlobalTransform * car.CenterOfMass;
        Vector3 arm = contact - comGlobal;
        Vector3 pointVelocity = car.LinearVelocity + car.AngularVelocity.Cross(arm);
        Vector3 offset = contact - car.GlobalPosition;

        float spring = StiffnessNPerM * compression;
        float damper = -DampingNPerMs * pointVelocity.Dot(up);
        LoadN = Mathf.Max(0f, spring + damper);
        if (LoadN <= 0f) return;

        car.ApplyForce(up * LoadN, offset);

        // ----- tyre frame, flattened onto the surface it is standing on
        Basis steered = GlobalTransform.Basis.Rotated(up, SteerAngle);
        Vector3 forward = Flatten(-steered.Z, normal);
        Vector3 right = Flatten(steered.X, normal);
        if (forward.LengthSquared() < 0.0001f || right.LengthSquared() < 0.0001f) return;

        float vForward = pointVelocity.Dot(forward);
        float vLateral = pointVelocity.Dot(right);
        ForwardSpeedMs = vForward;

        float fx = DriveTorqueNm / Radius;
        fx -= Mathf.Sign(vForward) * RollingResistance * LoadN;

        if (BrakeTorqueNm > 0f)
        {
            // Capped at the force that brings this corner's share of the mass to a
            // stop this tick, so braking settles instead of oscillating.
            float brake = BrakeTorqueNm / Radius;
            float toStop = Mathf.Abs(vForward) * (car.Mass * 0.25f) / Mathf.Max(dt, 0.0001f);
            fx -= Mathf.Sign(vForward) * Mathf.Min(brake, toStop);
        }

        float fy = -LoadN * Mathf.Clamp(vLateral * LateralGainPerMs, -1.4f, 1.4f);

        float limit = Friction * LoadN;
        var planar = new Vector2(fx, fy);
        if (planar.Length() > limit) planar = planar.Normalized() * limit;

        car.ApplyForce(forward * planar.X + right * planar.Y, offset);
    }

    /// <summary>Wheel spin that matches the ground it is rolling on, in rad/s.</summary>
    public float SpinRateRadPerSec => ForwardSpeedMs / Mathf.Max(0.05f, Radius);

    private static Vector3 Flatten(Vector3 v, Vector3 normal)
    {
        Vector3 flat = v - normal * v.Dot(normal);
        return flat.LengthSquared() < 0.0001f ? Vector3.Zero : flat.Normalized();
    }

    private static float ProjectedForwardSpeed(CarNode car, Vector3 at, Vector3 up)
    {
        Vector3 comGlobal = car.GlobalTransform * car.CenterOfMass;
        Vector3 velocity = car.LinearVelocity + car.AngularVelocity.Cross(at - comGlobal);
        return velocity.Dot(-car.GlobalTransform.Basis.Z);
    }
}
