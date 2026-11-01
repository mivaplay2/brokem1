# my autumn car

A part-by-part car assembly and survival sim in the vein of *My Summer Car*: the
car arrives as a bare chassis and a floor full of components, and it only moves
once you have fitted every part, torqued every bolt with the right spanner and
poured in the fluids it needs. Run it dry and you will seize the engine for good.

This is a **vertical slice**, not a finished game. It is deliberately the hardest
third of the project — the assembly model, the mechanical simulation, the save
format and the vehicle physics — because everything after it is content built on
these foundations rather than new problems.

Godot 4.5 (.NET) and C# 12. No art pipeline: every mesh is a primitive built in
code, so the project runs straight from a clone.

## The one architectural decision everything else follows from

The mechanical simulation is pure C# in `src/Sim` and contains **no engine
types**. The scene is a mirror of it, never the other way round.

That buys three things:

- **The engine is testable without launching the game.** `tests/SimTests`
  compiles `src/Sim` straight into a plain console project — no Godot, no
  graphics, no test framework — and exercises the whole mechanical model in under
  a second.
- **A save is a serialisation, not a scene dump.** Every bolt's tightness, every
  part's wear, the pistons you ruined — all of it is plain data. Nothing about
  the node tree is written out, so a save made before a scene change still loads.
- **Parts are data.** Growing the car from ten parts to a hundred and fifty is an
  edit to `PartCatalog`, not a hundred and forty new scripts.

The usual way this kind of project dies is by mixing the mechanical state into
the physics bodies and the scene. Avoiding that is the whole point of the layout
below.

## Layout

```
src/Sim/                   pure C#, no Godot — the game's actual rules
  PartDefinition.cs        a part as data: mass, mount, bolts, dependencies
  BoltSpec.cs              one fastener: position, required spanner, how fast it turns
  PartState.cs             per-bolt tightness, wear, "is this actually holding?"
  PartCatalog.cs           the parts list and the mount points on the chassis
  AssemblyModel.cs         install and removal rules, mass, centre of mass
  EngineSim.cs             a 1.6 carburetted four as one rotating inertia
  Drivetrain.cs            slipping clutch, five speeds and reverse
  FluidTanks.cs            petrol, oil, coolant, battery charge
  CarSim.cs                the four of those as one tickable object
  SaveData.cs / SaveSystem.cs

src/Game/                  the Godot layer — presentation and input only
  GameWorld.cs             builds the garage, the chassis, the parts, the player
  CarNode.cs               the body: mounts, four corners, driving controls
  Vehicle/RaycastWheel.cs  raycast strut and tyre, one per corner
  PartNode.cs              one node that is the part on the floor, in your
                           hands and bolted to the car
  MountNode.cs             a slot that only accepts the part that belongs in it
  BoltNode.cs              one aimable fastener
  Items.cs                 cans, fillers, the toolbox, the seat
  PlayerController.cs      first person, one pair of hands
  Interactor.cs            what the crosshair is on
  UI/Hud.cs                gauges, prompts, the controls panel
  GameSave.cs              model serialisation plus the few transforms it owns
  HeadlessSmokeTest.cs     builds and drives the car with nobody at the keyboard

tests/SimTests/            32 cases, 91 checks, no engine required
scripts/check.sh           runs both suites
```

## Building and running

Needs the **.NET 8 SDK** and **Godot 4.5 .NET** (the "mono"/C# build — the plain
build cannot run C#).

```bash
# open it in the editor and press F5
godot --path .

# or build and run from the command line
dotnet build MyAutumnCar.csproj
godot --path .
```

On Windows the same two commands work with `Godot_v4.5-stable_mono_win64.exe` in
place of `godot`.

### Tests

```bash
# the mechanical simulation, no engine needed
cd tests/SimTests && dotnet run -c Release

# the scene: builds the car, starts it, drives it out of the garage, exits 0 or 1
godot --headless --path . -- --smoke

# both
GODOT=/path/to/Godot_v4.5-stable_mono_linux.x86_64 scripts/check.sh
```

## Controls

**On foot** — `WASD` move, `shift` run, `space` jump, `E` pick up / fit / take
off / get in, hold `E` to pour, `G` drop, `1`-`7` pick a spanner (after you take
the toolbox off the bench), left mouse tighten a bolt, right mouse loosen it.

Spanners: `1` 8 mm, `2` 10 mm, `3` 12 mm, `4` 14 mm, `5` 17 mm, `6` plug spanner,
`7` screwdriver.

**Driving** — `I` ignition, hold `R` to crank, `[` and `]` push the choke in and
pull it out, `W` throttle, `S` brake, `A`/`D` steer, `space` handbrake, `shift`
clutch, `Z` down a gear, `X` up a gear, `N` neutral, `F` get out.

`F1` controls, `F5` save, `F9` load, `escape` release the mouse.

## Building the car in the game

1. Take the spanners off the bench, or nothing will turn.
2. **Engine block** into the bay, six 17 mm bolts. Nothing goes on top of it
   until every one of them is home — a part refuses to fit while the part
   underneath it is loose, not merely while it is absent.
3. **Cylinder head** on the block, eight 14 mm. **Carburettor**, two 10 mm.
   **Spark plugs**, plug spanner.
4. **Battery** (two 10 mm) or the starter will not turn. **Radiator** (four
   10 mm) or you will cook the head gasket within a couple of minutes.
5. **Four wheels**, four 17 mm studs each. One loose stud and that corner is not
   carrying the car.
6. Pour **oil** in before you start it. The oil light is the one warning that
   matters: thirty seconds of running dry and the pistons are scrap, permanently,
   through a save and back.
7. **Petrol** in the filler, **coolant** in the radiator cap.
8. Get in, `I`, pull the choke out with `]`, hold `R`. Push the choke back in as
   it warms up or it will run rich and die.
9. Clutch down, `X` into first, feed the clutch out with some throttle. Dumping
   it at idle stalls the engine, as it should.

## What is actually simulated

- **Assembly**: per-bolt tightness, correct-spanner enforcement, dependency order
  in both directions (a part cannot come off while something sits on it), mass
  and centre of mass that move as parts go on. A car with no engine really is
  light at the nose; with no wheels it sits on its chassis.
- **The engine**: torque curve, idle screw, choke and mixture against coolant
  temperature (cold wants the choke, warm drowns on it), starter torque and
  battery drain, fuel burn, oil pressure from sump level and revs, piston wear
  when pressure is lost, cooling against radiator and airflow, head gasket damage
  above 117 °C, rev limiter, and the specific reason it stopped kept on the gauge.
- **Drivetrain**: a slipping clutch modelled as a torsional coupling, five speeds
  and reverse, no shift without the pedal down.
- **Chassis**: raycast strut per corner with spring and damper, tyre forces inside
  a friction circle, speed-sensitive steering lock, aerodynamic drag.

## Known limitations

Stated plainly, because the gap between this and a game is mostly in here.

- **No wheelspin.** Driven-wheel speed is taken from ground speed, so the clutch
  cannot light up the tyres. Longitudinal force is limited by the friction circle,
  but wheel angular velocity is kinematic rather than simulated.
- **The lateral tyre model is a slip-velocity spring**, not Pacejka. It is grippy
  and forgiving; it will not reward a handbrake turn.
- **Gearbox, clutch and axles are not parts yet.** They are assumed present and
  perfect. They belong in the catalog next.
- **Placeholder geometry and no sound at all.** Everything is a box or a cylinder.
- **The world is a garage and a strip of tarmac.** No traffic, shops, jobs, phone
  or radio.
- **No player needs** — no hunger, thirst, fatigue or drunkenness.
- **`PartState.Wear` is carried everywhere but almost nothing raises it yet**;
  only pistons and the head gasket take damage.
- **The save does not record what you were holding.** You load with empty hands.
- Keyboard and mouse only; no gamepad.

## Next, in the order that pays off

1. Gearbox, clutch and driveshaft as fitted parts, so the drivetrain can be
   wrong too.
2. Real wheel angular velocity, which gives wheelspin, lockup and engine braking
   through the same clutch model that is already there.
3. Player needs and a day cycle — the other half of what makes the original tense.
4. Meshes and sound. The loop has to be right first; it is cheaper to replace a
   box than to rebuild the model under it.
5. A road network and somewhere to drive to.

## Verification

- `tests/SimTests`: **32 cases, 91 checks, all passing** on .NET 8.0.427.
- `--smoke` on Godot 4.5.stable.mono, headless, Linux x86_64: the car is built
  from loose parts through the real model, stands up on its suspension (the body
  lifts from −0.27 m to 0.05 m as the wheels go on), starts cold on the choke at
  847 rpm, pulls away in first and **drives 42.05 m out of the garage at a peak
  of 37.2 km/h**, tracking within 0.20 m of straight, then stops on the brakes.
- `dotnet build`: clean, no warnings.
- **Not checked:** running with a window, rendering, or anything on Windows or
  macOS. Everything above was verified headless on Linux. There is no frame-rate
  or performance measurement, and no one has played it for fun yet.
