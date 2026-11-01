namespace MyAutumnCar.Game;

/// <summary>
/// What a driver is doing with the pedals, the wheel and the key. Keeping this as
/// data rather than direct keyboard reads is what lets the headless test drive
/// the same car the player does, through the same code path.
/// </summary>
public sealed class DriverControls
{
    public float Throttle;
    public float Brake;
    public float Steer;
    public float Clutch;
    public bool Handbrake = true;
    public bool Starter;

    public void Reset()
    {
        Throttle = 0f;
        Brake = 0f;
        Steer = 0f;
        Clutch = 0f;
        Handbrake = true;
        Starter = false;
    }
}
