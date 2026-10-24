# Loose Ends

A MelonLoader (IL2CPP) mod for **Schedule I** that makes dead bodies actually matter.

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/looseends](https://support.doodesch.de/looseends).

[![Polyfill](https://polyfill.doomods.com/badge/loose-ends.svg)](https://polyfill.doomods.com/mod/loose-ends)

[![MLVScan LooseEnds.dll](https://mlvscan.com/attestations/att_bEggRi5D-070aL4nvAywhxSi/badge.svg)](https://mlvscan.com/attestations/att_bEggRi5D-070aL4nvAywhxSi)

![A witness phones the police, officers walk to the body](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-LooseEnds/main/docs/img/header.jpg)

📖 **Documentation:** [docs.doodesch.de/mods/looseends/](https://docs.doodesch.de/mods/looseends/)

In vanilla, police and citizens walk over a corpse like it isn't there, and a body drags around as if it weighs as
much as a plastic bag. Loose Ends fixes both:

1. **Witnesses react.** A living NPC who actually *sees* a dead NPC reacts. A civilian witness pulls out their phone
   and **calls the police** over the game's own ~4 second call window - phone out, a progress icon over their head.
   That window is your chance: knock the witness out or kill them before the call connects and it stops, silencing the
   only witness. What the police do depends on what the witness saw:
   - **Only the body:** the police do not know who did it. One officer walks to the scene and searches around the body
     for about 2 minutes. Nobody is wanted. If an officer sees you near the body, you become the suspect
     (**Investigating**). Each further body on the same day sends one more officer, up to 3.
   - **You at the body:** the witness describes you. You become the suspect (**Investigating**) and the police look
     for you. The witness also remembers your face until the next day and calls again if they see you.

   A **police** officer who finds a body reports it instantly (you cannot phone-block a cop). A body hidden from
   sight - in a dumpster, behind a wall, indoors, underwater, behind cover - is never seen, so nothing happens. That
   is the whole point: hide your bodies or face the heat.
2. **Corpses are heavier.** While you drag a body it **resists being pulled** - it lags behind the carry point and
   drags sluggishly (and is heavier to throw), scaled by a configurable multiplier. It does **not** slow you down; the
   weight is in the body, not your legs.

Everything is configurable in `UserData/MelonPreferences.cfg`, including how punishing the police
response is.

## Screenshots

| The witness | The search |
|---|---|
| ![A witness on the phone, the body lies in the bushes](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-LooseEnds/main/docs/img/call.jpg) | ![Two officers walk to the body at dusk](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-LooseEnds/main/docs/img/search.jpg) |
| ![A witness calls the police](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-LooseEnds/main/docs/img/phone.jpg) | ![Two bodies left on the sidewalk](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-LooseEnds/main/docs/img/left-behind.jpg) |

## Requirements

- MelonLoader 0.7.3+ (IL2CPP build of Schedule I)
- [S1API](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) (Il2Cpp). Loose Ends does not load without it.

## How it works

- The mod keeps a small live set of NPC corpses (updated instantly on death and reconciled every ~2 s). When there are
  no corpses, it does effectively nothing, so there is no idle cost.
- A throttled scan asks each nearby living NPC whether it can see the body. There are two sight checks: the NPC's own
  vision cone (`VisionCone.IsPointWithinSight`, field-of-view + occlusion) within a believable `BodySightRange`, and a
  close-range notice within `NoticeRadius` (NPCs never look down at their feet, so a body lying flat is below their
  forward cone - this is what makes someone standing over a corpse actually react). Close-range line of sight is
  resolved by the game's own visibility solver (`EntityVisibility`), so a body in a dumpster / behind a wall / in
  bushes stays hidden, and partial cover counts as hidden.
- On the first sighting, after a short reaction delay, the witness reacts. A **civilian/employee** witness pulls out
  their phone via the game's native `CallPoliceBehaviour` (~4 s call, visible progress icon) - interruptible: down the
  witness before it connects and no police are called. A witness in active combat with you holds at "seen" and calls
  once the fight ends (a panicking witness still calls, exactly like vanilla). A **police** witness reports instantly.
- When the call connects (or for the instant police/`WitnessCallsPolice` off path), the response depends on whether
  the witness saw the killer at discovery:
  - **Killer seen:** the game's own call finishes and raises the killer to Investigating (the instant path uses
    `PursuitLevel`). The witness remembers the killer until the next in-game day; when they see the killer again while
    the killer is not wanted, they phone the police once more (30 s apart at most).
  - **Body only:** the mod hangs up in the last frame of the call, before the game marks anyone. A foot patrol of 1
    officer (2 for the second body of the day, 3 from the third) walks a small route around the body for 120 s after
    it arrives. An officer who sees a player within 20 m of the body makes that player the suspect (Investigating) and
    starts the pursuit. The killer is also charged with the murder; anyone else is only questioned. Nobody is wanted
    while the search runs. When every officer of the station is out, the report waits up to 120 s for one.
- Every body a witness sees gets its own response (`OncePerCorpse`); there is no global throttle, so a body left in
  front of someone is never silently ignored.
- Killer attribution is captured at attack time (the game stores no "killer" on a corpse), so the heat goes to whoever
  actually did it; a knockout falls back to the recent attacker. If the killer is unknown (an environmental or
  NPC-vs-NPC death), single-player blames the local player so there is still a response; co-op falls back sensibly.
- Getting arrested settles your spree: every body blamed on you (or on nobody) is marked resolved and a call still
  ringing about it is dropped, so you are not arrested again for each corpse you left lying around. In co-op this is
  per player: your friend's bodies stay open when you are arrested.
- It is host-authoritative: detection and all police/pursuit writes run only on the server/host and use the game's own
  networked calls, so they replicate to clients. In a co-op lobby only the host's `EnableInMultiplayer` counts.

## Settings (category `LooseEnds_01_Main`)

**Master**
- `Enabled` (true) - master switch.
- `EnableInMultiplayer` (true) - witnesses also react in a co-op lobby. Only the host's value counts; the host runs
  detection for every player. A config file from 1.1.1 or older keeps its old value (false).

**Detection**
- `ReactCitizens` (true), `ReactPolice` (true), `ReactEmployees` (false) - which NPC types can witness (your crew
  ignores bodies by default).
- `RequireLineOfSight` (true) - walls, dumpsters, buildings, water and cover hide a body. Off = anyone in range notices
  it, through walls too.
- `BodySightRange` (12 m, range 4-30) - how far an NPC notices a body in their field of view. Capped below normal sight
  range, because a body lying flat is far less conspicuous than a standing person. Only NPCs this close to a body are
  checked at all.
- `NoticeRadius` (6 m, range 2-15) - an NPC this close notices a body in any direction (NPCs never look down at their
  feet). Cover still hides it.
- `ReactionDelaySeconds` (3 s, range 0-30) - delay after the first sighting before the response fires.
- `ReactToKnockedOut` (false) - also react to knocked-out NPCs, not only dead ones. A knocked-out witness stays silenced.

**Response (the "realism" dial)**
- `WitnessCallsPolice` (true) - a civilian witness calls over the game's own ~4 s window (visible progress icon);
  silence them before it connects to stop it. Off = police are alerted instantly. A police officer always reports
  instantly.
- `RequirePlayerAlsoSeen` (false) - on: a witness reports only once they also see the killer. Off: a witness who sees
  only the body reports it, and officers search the scene.
- `PursuitLevel` (1, range 1-4) - what an instant report (police witness, or `WitnessCallsPolice` off) gives a killer
  the witness saw: 1 Investigating, 2 Under arrest, 3 Wanted, 4 Wanted dead. A phoned report always gives
  Investigating.
- `OncePerCorpse` (true) - report each body at most once. Off = it can be reported again after the cooldown below.
- `ResponseCooldownSeconds` (60 s, range 5-600) - per-body re-report gap, used only when `OncePerCorpse` is off. It
  does not throttle different bodies.
- `AttributeUnknownToLocalPlayer` (true) - single-player only: a body nobody killed (lightning, an NPC fight) is blamed
  on you, so the investigation still has a suspect.

**Weight**
- `CorpseWeightMultiplier` (5, range 1-20) - how hard a dragged body (dead or knocked out) resists being pulled and
  thrown. 1 = vanilla. It does not slow the player.

## Building

```
dotnet build -c Debug     # dev build: the 'le' console command, verbose logging, Snitch panel
dotnet build -c Release   # release build: all dev surface compiled out
```

In the DooDesch workspace a build deploys only into the test slot of the session's lease (`Directory.Build.targets`).

## Verifying in-game

Build **Debug**, load a save, open the dev console. Every `le` answer goes to the MelonLoader log.

1. **It loads cleanly.** The log shows `Loose Ends v...`, `[Config] Loaded: ...` and, once in a save on the host,
   `[Core] Witness system ACTIVE`.
2. **Make a body.** `le kill` kills the living NPC nearest you (`le ko` knocks it out). The log shows
   `[Killer] ... died` and `[Corpse] tracked ...`.
3. **Witnessing (open).** With the body in the open near other NPCs, the log shows `[Witness] ... SEEN ...`, then
   `[Reaction] ... CALLING police ... describes you`. Once the call connects your pursuit shows **Investigating** and
   the log shows `[Memory] ... will recognise you`. After `clearwanted` the same witness calls again when they see you.
4. **Scene search.** `le unseen on` makes every witness miss the killer. `le kill`: the call ends with
   `body reported, killer unknown` and `[Search] ... officer(s) walk to the scene`. Stay at the body: the log shows
   `reached the scene`, then `is the suspect - Investigating, charged with the murder`. Walk away: after 120 s
   `search over - time is up`.
5. **Silence the witness.** `le arsenal` gives you weapons. Knock out or kill the calling witness inside the ~4 s
   window (or run `le silence`): the log shows `[Reaction] witness SILENCED ...` and that witness never calls.
6. **Hidden body.** A body in a dumpster, indoors, underwater or behind cover is not seen.
7. **Weight.** Drag the body: it lags behind and is heavy to throw, your speed stays the same. `le drag` / `le drop`
   pick up and drop the nearest body and log the `[Weight]` values.
8. **Arrest.** `le arrest` arrests you: the log shows `[Arrest] ... settled N bodies, ... M witness memories`.

More: `le status` (bodies and their state), `le find` (start the response on the nearest body, no sight or delay),
`le scanlog on|off`, `le pursuit 0-4` (0 = the setting), `le mp on|off` (witnesses in co-op without the setting),
`le unseen on|off` (witnesses never see the killer), `le busy on|off` (the station has no free officer).

## Status / known limitations

- **Multiplayer** is host-authoritative and on by default in co-op (`EnableInMultiplayer`). Tested with two local
  instances, not yet with two players on separate machines.
- Killer attribution covers player-inflicted deaths; environmental / NPC-vs-NPC deaths are still detected as bodies but
  have no player to blame (single-player can fall back to the local player; co-op falls back to no attribution).

This mod is written with help from generative AI tools (Anthropic Claude, OpenAI Codex).
