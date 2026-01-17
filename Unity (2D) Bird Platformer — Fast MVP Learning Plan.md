# Problem statement
Build a simple, playable 2D Unity game where a bird travels from Point A (start) to Point B (family nest) without touching obstacles. The bird can walk/run on the ground and fly left/right (and gain altitude) to navigate hazards. The focus is learning Unity fundamentals and reaching a working end-to-end game as fast as possible.
# Current state
No project structure or gameplay systems are assumed to exist yet.
# Constraints and decisions
* Engine: Unity, scripting in C# (Unity gameplay scripting is C#; Python is optional only for external tooling and is not needed for the MVP).
* 2D workflow: Rigidbody2D/Collider2D, Tilemap optional!
* MVP target: one short level, one player, a few obstacle types, a clear win/lose loop.
# MVP scope (what “done” means)
* Start screen → Play
* One level scene with Start (A) and Goal (B)
* Player can move left/right, run (optional), jump/flap, and fly/glide enough to clear obstacles
* Obstacles that cause failure on contact
* Win on reaching the family nest/goal trigger
* Simple UI overlay: “You Win” / “Game Over” + Restart
* Basic audio is optional; basic visuals can be placeholders
# High-level architecture
## Scenes
* `MainMenu` (very simple)
* `Level01` (the only level for MVP)
## Prefabs
* `BirdPlayer`
* `Obstacle_Static` (spike/branch)
* `Obstacle_Moving` (optional extension)
* `GoalNest`
* `Checkpoint` (optional)
## Scripts (minimal set)
* `PlayerController2D.cs` (movement + animation hooks)
* `PlayerHealth.cs` or `PlayerState.cs` (alive/dead, triggers)
* `Hazard.cs` (tag/behavior for obstacles)
* `Goal.cs` (win trigger)
* `GameManager.cs` (state machine: playing/win/lose, restart)
* `UIController.cs` (simple screens)
* `CameraFollow2D.cs` (follow player)
# Implementation plan (vertical slice first)
## Phase 1: Project setup and first playable loop (fastest milestone)
* Create Unity 2D project.
* Create `MainMenu` and `Level01` scenes.
* Add a `GameManager` (singleton or scene object) that can:
    * Start game (load `Level01`)
    * Restart level
    * Return to menu
* In `Level01`, place:
    * Ground plane (SpriteRenderer + BoxCollider2D)
    * A simple start area
    * A goal object (family nest placeholder) with `Goal` script and Trigger collider
* Define tags/layers early:
    * Tag: `Player`
    * Layer: `Ground`
    * Layer: `Hazard`
## Phase 2: Bird movement (walk/run + flight) 
Goal: movement feels controllable quickly, even with placeholder art.
* Create `BirdPlayer` prefab:
    * Rigidbody2D (Dynamic)
    * Collider2D (CapsuleCollider2D or BoxCollider2D)
    * SpriteRenderer (placeholder square/bird)
* Implement `PlayerController2D` with these inputs:
    * Horizontal: A/D or Left/Right
    * Jump/Flap: Space
    * Run (optional): Shift
* Movement model (MVP-friendly):
    * Grounded detection via small overlap check (circle/box at feet) against `Ground` layer.
    * Horizontal movement sets target velocity (different speeds for walking vs running).
    * Flap/jump provides an upward impulse when grounded.
    * Air control allows steering left/right.
    * “Flight” simplest option: holding Space reduces gravity (glide) and/or applies small upward force capped by max vertical speed.
* Expose tuning variables in Inspector:
    * walkSpeed, runSpeed, jumpImpulse, airControl, glideGravityScale, maxFallSpeed
## Phase 3: Obstacles and lose condition
* Create `Hazard` behavior:
    * Any collider on `Hazard` layer causes loss when player touches it.
* Implement loss flow:
    * On hazard collision: disable player input, play small feedback (flash/sound), show Game Over UI.
    * Restart button reloads `Level01`.
* Add 2–3 obstacle types using only Unity primitives:
    * Static spikes/branches
    * Narrow gaps requiring glide timing
    * Vertical “tunnel” requiring controlled ascent/descent
## Phase 4: Camera + feel improvements (minimal polish that helps learning)
* Add `CameraFollow2D`:
    * Smooth-damp follow on X/Y, clamped to level bounds.
* Add quick feedback:
    * Simple animation placeholders (flip sprite based on direction)
    * Particles on flap (optional)
    * Basic sound effects (optional)
## Phase 5: Win condition + story wrapper (family reunion)
* Implement win trigger:
    * Entering `GoalNest` trigger calls `GameManager.Win()`.
* Win UI:
    * Short text: “Reunited!” + “Play Again”
* Add light narrative:
    * Menu text: “Find your family. Reach the nest.”
# Level design (keep it small)
* One linear level, 30–90 seconds long.
* Teach mechanics in order:
    * Walk/run → first jump/flap
    * Introduce glide/flight control
    * Combine with 2–3 hazards
    * Final approach to nest
# Learning-focused checkpoints (what you’ll learn by building this)
* Scene setup, prefabs, colliders, rigidbodies
* Input handling, tuning via Inspector
* Triggers vs collisions
* Basic game state management (menu/play/win/lose)
* Simple UI workflow
# Optional extensions (only after MVP works)
* Stamina/energy meter for flight (prevents infinite hovering)
* Checkpoints
* Moving hazards (patrolling birds, swinging branches)
* Collectibles (feathers) for score
* Second level with a new mechanic (wind zones)
# Definition of done
* From app launch: Start → Play → Navigate level → Win or Lose → Restart works reliably.
* No obvious physics glitches (player falling through ground, double-triggering win/lose).
* All key values are tunable in the Inspector without editing code.
