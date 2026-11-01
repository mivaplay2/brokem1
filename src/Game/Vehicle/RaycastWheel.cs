using Godot;

namespace MyAutumnCar.Game;

/// <summary>
/// One corner: a raycast suspension strut and a tyre with its own rotation.
///
/// The wheel's angular velocity is integrated from the torques acting on it —
/// drive, brake, rolling resistance and the reaction from the contact patch —
/// rather than read off the ground speed. That is what makes wheelspin, lockup
/// and engine braking fall out of the model instead of having to be faked, and it
/// means two driven wheels at different speeds behave like an open differential
/// without one being written.
///
/// The engine, clutch and tyre form a stiff spring chain, so the rotation is
/// solved semi-implicitly: the two stiffnesses that matter are linearised about
/// the current speed and moved into the denominator. Explicit integration of this
/// at a 60 Hz physics step is unstable by roughly an order of magnitude.
/// </summary>
public partial class RaycastWheel : Node3D
{
    public string PartId { get; private set; }
    public bool Driven { get; private set; }
    public bool Steers { get; private set; }

    // ------------------------------------------------------------ suspension
    public float Radius { get; set; } = 0.29f;
    public float RestLength { get; set; } = 0.34f;
    public float Travel { get; set; } = 0.17f;
    public float StiffnessNPerM { get; set; } = 46000f;
    public float DampingNPerMs { get; set; } = 4200f;

    // ------------------------------------------------------------------ tyre
    public float Friction { get; set; } = 1.15f;

    /// <summary>Normalised longitudinal slip stiffness: grip per unit slip ratio.</summary>
    public float LongitudinalSlipStiffness { get; set; } = 12f;

    public float LateralGainPerMs { get; set; } = 1.1f;
    public float RollingResistance { get; set; } = 0.014f;

    /// <summary>
    /// Floor on the slip-ratio denominator. Without it the slip ratio and its
    /// derivative both diverge as the car approaches a standstill.
    /// </summary>
    public float SlipReferenceSpeedMs { get; set; } = 2.5f;

    /// <summary>Below this a braked wheel is held by static friction rather than slip.</summary>
    public float StickSpeedMs { get; set; } = 0.5f;

    public float StickGain { get; set; } = 1.6f;

    /// <summary>Wheel speed below which the brakes can take hold of it outright.</summary>
    public float LockSpeedRadPerSec { get; set; } = 3f;

    /// <summary>Wheel, tyre and hub about the axle.</summary>
    public float InertiaKgM2 { get; set; } = 0.8f;

    public float MaxAngularVelocity { get; set; } = 400f;

    // ----------------------------------------------------------------- inputs
    public float SteerAngle { get; set; }
    public float DriveTorqueNm { get; set; }
    public float BrakeTorqueNm { get; set; }

    /// <summary>
    /// Stiffness of the driveline behind this wheel, in Nm per rad/s. Written by
    /// the car from the clutch each tick so the solve stays stable in gear.
    /// </summary>
    public float DriveStiffnessNmPerRadPerSec { get; set; }

    // ------------------------------------------------------------------ state
    public bool Grounded { get; private set; }
    public float LoadN { get; private set; }
    public float ForwardSpeedMs { get; private set; }
    public float SpringLength { get; private set; }

    /// <summary>Wheel rotation, rad/s, positive when rolling forward.</summary>
    public float AngularVelocity { get; private set; }

    public float SlipRatio { get; private set; }
    public float LongitudinalForceN { get; private set; }

    /// <summary>Where the wheel and its mount marker hang, moved by the suspension.</summary>
    public Node3D Anchor { get; private set; }

    private RayCast3D _ray;

    /// <summary>Surface speed of the tyre, which is the ground speed only when it is not slipping.</summary>
    public float TyreSurfaceSpeedMs => AngularVelocity * Radius;

    /// <summary>Spinning up faster than the road is passing underneath it.</summary>
    public bool Spinning => Grounded && TyreSurfaceSpeedMs - ForwardSpeedMs > 1.5f;

    /// <summary>Stopped turning while the car is still moving.</summary>
    public bool Locked => Grounded && Mathf.Abs(AngularVelocity) < 0.6f && Mathf.Abs(ForwardSpeedMs) > 1.5f;

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
        if (!fitted)
        {
            Grounded = false;
            LoadN = 0f;
            ForwardSpeedMs = 0f;
            AngularVelocity = 0f;
            SlipRatio = 0f;
            LongitudinalForceN = 0f;
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
            ForwardSpeedMs = 0f;
            SlipRatio = 0f;
            LongitudinalForceN = 0f;
            // Nothing holding it back, so drive torque simply spins it up.
            IntegrateRotation(dt, 0f, 0f);
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
        Vector3 pointVelocity = car.LinearVelocity + car.AngularVelocity.Cross(contact - comGlobal);
        Vector3 offset = contact - car.GlobalPosition;

        float spring = StiffnessNPerM * compression;
        float damper = -DampingNPerMs * pointVelocity.Dot(normal);
        LoadN = Mathf.Max(0f, spring + damper);

        if (LoadN <= 0f)
        {
            SlipRatio = 0f;
            LongitudinalForceN = 0f;
            IntegrateRotation(dt, 0f, 0f);
            return;
        }

        // The ground reaction acts along the surface normal, not along the body's
        // own up. Using the body up couples pitch into a longitudinal force: under
        // sustained braking the nose dips, the whole 8.5 kN of weight tilts with it
        // and several hundred newtons of phantom drag appear out of nowhere.
        car.ApplyForce(normal * LoadN, offset);

        // ----- tyre frame, flattened onto the surface it is standing on
        Basis steered = GlobalTransform.Basis.Rotated(up, SteerAngle);
        Vector3 forward = Flatten(-steered.Z, normal);
        Vector3 right = Flatten(steered.X, normal);
        if (forward.LengthSquared() < 0.0001f || right.LengthSquared() < 0.0001f)
        {
            IntegrateRotation(dt, 0f, 0f);
            return;
        }

        float vForward = pointVelocity.Dot(forward);
        float vLateral = pointVelocity.Dot(right);
        ForwardSpeedMs = vForward;

        // ----- longitudinal force from slip ratio
        float vRef = Mathf.Max(Mathf.Abs(vForward), SlipReferenceSpeedMs);
        SlipRatio = (TyreSurfaceSpeedMs - vForward) / vRef;

        float demanded = SlipRatio * LongitudinalSlipStiffness;
        bool saturated = Mathf.Abs(demanded) >= Friction;
        float fx = Mathf.Clamp(demanded, -Friction, Friction) * LoadN;

        // A braked wheel at walking pace is held by static friction; slip alone
        // produces no force at a standstill, which would let a parked car creep.
        if (BrakeTorqueNm > 0f && Mathf.Abs(vForward) < StickSpeedMs)
            fx += -vForward * StickGain * LoadN;

        float fy = -LoadN * Mathf.Clamp(vLateral * LateralGainPerMs, -1.4f, 1.4f);

        float limit = Friction * LoadN;
        var planar = new Vector2(fx, fy);
        if (planar.Length() > limit) planar = planar.Normalized() * limit;

        LongitudinalForceN = planar.X;
        car.ApplyForce(forward * planar.X + right * planar.Y, offset);

        // ----- the wheel's own rotation
        // Slope of contact force against wheel speed, zero once the tyre is
        // sliding because the force has stopped depending on how fast it spins.
        float tyreStiffness = saturated
            ? 0f
            : LongitudinalSlipStiffness * LoadN * Radius / vRef;

        IntegrateRotation(dt, planar.X, tyreStiffness);
    }

    /// <summary>
    /// Semi-implicit step for the wheel. The tyre and driveline stiffnesses are
    /// linearised about the current speed and carried in the denominator, which is
    /// what keeps this stable at a 60 Hz step.
    /// </summary>
    private void IntegrateRotation(float dt, float contactForceN, float tyreStiffness)
    {
        float omega = AngularVelocity;

        float torque = DriveTorqueNm - contactForceN * Radius;

        // Rolling resistance, faded out through zero so it does not chatter.
        if (LoadN > 0f)
            torque -= Mathf.Clamp(omega * 2f, -1f, 1f) * RollingResistance * LoadN * Radius;

        if (BrakeTorqueNm > 0f)
        {
            // A wheel the brakes can hold against the contact patch is locked, and
            // stays locked. Without this it hovers at the speed where brake and
            // tyre torque happen to balance, and never actually stops turning.
            if (Mathf.Abs(omega) < LockSpeedRadPerSec && BrakeTorqueNm >= Mathf.Abs(torque))
            {
                AngularVelocity = 0f;
                return;
            }
            torque -= Mathf.Sign(omega) * BrakeTorqueNm;
        }

        float denominator = InertiaKgM2 / Mathf.Max(dt, 1e-5f)
                            + Radius * tyreStiffness
                            + DriveStiffnessNmPerRadPerSec;

        float next = omega + torque / Mathf.Max(denominator, 1e-3f);

        // A wheel the brakes have stopped stays stopped rather than reversing.
        if (BrakeTorqueNm > 0f && omega * next < 0f) next = 0f;

        AngularVelocity = Mathf.Clamp(next, -MaxAngularVelocity, MaxAngularVelocity);
        if (!float.IsFinite(AngularVelocity)) AngularVelocity = 0f;
    }

    private static Vector3 Flatten(Vector3 v, Vector3 normal)
    {
        Vector3 flat = v - normal * v.Dot(normal);
        return flat.LengthSquared() < 0.0001f ? Vector3.Zero : flat.Normalized();
    }
}
