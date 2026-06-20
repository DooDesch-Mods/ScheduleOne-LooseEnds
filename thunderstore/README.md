# Loose Ends - Bodies That Matter for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

> **Hide your bodies, or face the heat.** In vanilla, police and citizens walk over a corpse like it
> isn't there. Loose Ends makes living NPCs who actually *see* a dead body react - citizens call the
> police, police enter the game's real **"Investigating"** pursuit - and makes carried corpses heavier,
> so hauling a body to its hiding spot has real weight and stress.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)

## What it does

1. **Witnesses react.** A living NPC (citizen or police) who actually *sees* a dead NPC reports it:
   citizens get the police called, police enter the game's real **"Investigating"** pursuit state. A
   body that is hidden from sight - in a dumpster, behind a wall, indoors, underwater, in bushes - is
   never seen, so nothing happens. That is the whole point: hide your bodies.
2. **Corpses are heavier.** While you carry a body you move noticeably slower (and the body drags more
   sluggishly), scaled by a configurable multiplier - adding weight and stress to hauling a corpse to
   its hiding spot.

## Features

- **Real line of sight.** Detection uses each nearby NPC's own vision cone (`VisionCone.IsPointWithinSight`),
  so it honors field of view and the game's occlusion layers. A hidden body is genuinely unseen.
- **Killer attribution.** The heat goes to whoever actually did it, captured at attack time. Unknown
  (environmental / NPC-vs-NPC) deaths fall back sensibly (single-player blames the local player; co-op
  uses a scene response).
- **Configurable realism.** Hunt the killer / Scene investigation / Escalating, line-of-sight on/off,
  which NPC types react, pursuit level, reaction delay, cooldowns, weight, and more.
- **Cheap when idle.** With no corpses in the world the mod does effectively nothing; the witness scan is
  throttled and only runs while a body exists.
- **Host-authoritative.** Detection and every police/pursuit write run only on the server/host through the
  game's own networked calls, so they replicate to clients.

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API** (pulled in as a dependency).
- Optional: **Mod Manager & Phone App** for the in-game settings UI.

## Settings

Editable in the Mod Manager & Phone App UI or `UserData/MelonPreferences.cfg`
(category `LooseEnds_01_Main`):

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | on | Master switch. Off = fully vanilla. |
| `EnableInMultiplayer` | off | Force the witness system on in co-op (experimental, host-authoritative). |
| `RequireLineOfSight` | on | Use the NPC vision cone so hidden bodies are unseen. Off = pure radius. |
| `ResponseMode` | 0 (Hunt) | 0 Hunt the killer (reliable), 1 Scene investigation (experimental), 2 Escalating. |
| `PursuitLevel` | 1 | 1 Investigating, 2 Arresting, 3 NonLethal, 4 Lethal. |
| `ReactionDelaySeconds` | 3 | Delay after the first sighting before the response fires. |
| `OncePerCorpse` | on | Respond at most once per body. |
| `CorpseWeightMultiplier` | 5 | How heavy a carried corpse feels (1 = vanilla, up to 20). |

Plus `ReactCitizens`, `ReactPolice`, `ReactEmployees`, `DetectionRange`, `ScanIntervalSeconds`,
`ResponseCooldownSeconds`, `WeightMode` and more - see the in-game settings for the full list.

## Multiplayer

The witness system is host-authoritative and **off by default in a real co-op lobby**
(`EnableInMultiplayer`) until it has been tested with two players. Set `EnableInMultiplayer = on` to opt in.

## License

MIT. See the included LICENSE.md.
