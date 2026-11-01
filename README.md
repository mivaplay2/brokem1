# my autumn car

A part-by-part car assembly and survival sim in the vein of *My Summer Car*: the
car arrives as a bare chassis and a floor full of components, and it only moves
once you have fitted every part — engine, transmission and all — torqued every
bolt with the right spanner and poured in the fluids it needs. Run it dry and you
will seize the engine for good; ride the clutch and you will burn it out.

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
  Drivetrain.cs            clutch, five speeds and driveshaft — all fitted parts
  FluidTanks.cs            petrol, oil, coolant, battery charge
  CarSim.cs                the four of those as one tickable object
  SaveData.cs / SaveSystem.cs

src/Game/                  the Godot layer — presentation and input only
  GameWorld.cs             builds the garage, the chassis, the parts, the player
  CarNode.cs               the body: mounts, four corners, driving controls
  Vehicle/RaycastWheel.cs  raycast strut and a tyre with its own rotation
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
4. **Clutch** against the back of the block, six 12 mm. Then the **gearbox** onto
   the clutch, eight 14 mm, then the **driveshaft** onto the gearbox, four 12 mm
   flange bolts. The chain is enforced in both directions: the gearbox will not
   go on until the clutch is bolted down, and the clutch cannot come out from
   under the gearbox.
5. **Battery** (two 10 mm) or the starter will not turn. **Radiator** (four
   10 mm) or you will cook the head gasket within a couple of minutes.
6. **Four wheels**, four 17 mm studs each. One loose stud and that corner is not
   carrying the car.
7. Pour **oil** in before you start it. The oil light is the one warning that
   matters: thirty seconds of running dry and the pistons are scrap, permanently,
   through a save and back.
8. **Petrol** in the filler, **coolant** in the radiator cap.
9. Get in, `I`, pull the choke out with `]`, hold `R`. Push the choke back in as
   it warms up or it will run rich and die.
10. Clutch down, `X` into first, feed the clutch out with some throttle. Dumping
    it at idle stalls the engine, as it should.

The engine will happily start and idle with no transmission in the car at all.
It simply will not move, and the gear lever reads `-` because there is no box to
put in gear.

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
- **The transmission, as three fitted parts**: the clutch is a slipping torsional
  coupling whose lining wears from the energy it dissipates, so riding it costs
  you — a hard pull-away burns about half a percent, and a worn clutch loses
  torque capacity and slips instead of driving. Five speeds and reverse, no shift
  without the pedal down, and no shift at all without a bolted gearbox: loosen its
  bolts while driving and it drops out of gear. A loose clutch or driveshaft
  passes nothing to the axle while the engine carries on as though nothing were
  wrong, and the dash says which link is broken.
- **Chassis**: raycast strut per corner with spring and damper, tyre forces inside
  a friction circle, speed-sensitive steering lock, aerodynamic drag.
- **Wheel rotation**: each wheel integrates its own angular velocity from the
  torques acting on it — drive, brake, rolling resistance and the reaction from
  the contact patch — rather than reading it off the ground speed. Wheelspin,
  lockup and engine braking are not written anywhere; they fall out of that, and
  so does open-differential behaviour, because an equal torque split between two
  wheels free to turn at different speeds *is* an open diff. The rotation is
  solved semi-implicitly: the engine, clutch and tyre form a stiff spring chain
  that explicit integration at a 60 Hz step misses stability on by about an order
  of magnitude, so the two stiffnesses that matter are linearised about the
  current speed and carried in the denominator.

## Known limitations

Stated plainly, because the gap between this and a game is mostly in here.

- **The lateral tyre model is a slip-velocity spring**, not Pacejka. Longitudinal
  slip is modelled properly; lateral is not, so the car will not reward a
  handbrake turn and has no camber, no load sensitivity and no combined-slip
  shaping beyond the friction circle.
- **The surface is uniform.** Every wheel uses the same friction coefficient
  wherever it is, so there is no tarmac, gravel or mud.
- **Clutch wear is capped at 95%, not 100%.** A part at full wear becomes
  unfittable, and with no parts shop in the slice that would be a dead end. A
  ruined clutch therefore slips uselessly but can still be taken off and put back
  on. Remove the cap once there is somewhere to buy a new one.
- **The differential and rear axle are still assumed perfect.** The driveshaft is
  a part; what it drives is not.
- **Placeholder geometry and no sound at all.** Everything is a box or a cylinder.
- **The world is a garage and a strip of tarmac.** No traffic, shops, jobs, phone
  or radio.
- **No player needs** — no hunger, thirst, fatigue or drunkenness.
- **`PartState.Wear` is carried everywhere but almost nothing raises it yet**;
  only pistons and the head gasket take damage.
- **The save does not record what you were holding.** You load with empty hands.
- Keyboard and mouse only; no gamepad.

## Next, in the order that pays off

1. A road to drive on — a loop, a hill and somewhere to go. The car is now worth
   driving and there is nowhere to drive it.
2. Player needs and a day cycle — the other half of what makes the original tense.
3. Per-surface friction, which is a one-line hook into the tyre model, and the
   thing that makes a gravel shortcut mean something.
4. Gearbox oil, and a differential that can be wrong too.
5. Meshes and sound. The loop has to be right first; it is cheaper to replace a
   box than to rebuild the model under it.

## Verification

- `tests/SimTests`: **43 cases, 131 checks, all passing** on .NET 8.0.427.
- `--smoke` on Godot 4.5.stable.mono, headless, Linux x86_64: the car is built
  from loose parts through the real model, a bare chassis refuses a gear, the body
  lifts from −0.27 m to 0.04 m as the wheels go on, it starts cold on the choke at
  847 rpm, **spins the driven wheels up on a dropped clutch** (the tyre runs
  3.7 m/s faster than the road, peak slip ratio 1.47, 0.59% of the lining gone),
  **drives 94.71 m out of the garage at a peak of 45.3 km/h** within 1.2% of
  straight, **brakes on the engine alone from 45.3 to 36.4 km/h** with the engine
  measurably behind the geared speed (4299 rpm against 4453) and −76 Nm at the
  axle, and **locks a wheel under full brakes at 34.4 km/h** before stopping.
- `dotnet build`: clean, no warnings.
- **Not checked:** running with a window, rendering, or anything on Windows or
  macOS. Everything above was verified headless on Linux. There is no frame-rate
  or performance measurement, and no one has played it for fun yet.
