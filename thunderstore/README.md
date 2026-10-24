# Loose Ends - Bodies That Matter for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/looseends](https://support.doodesch.de/looseends).

> **Hide your bodies, or face the heat.** In vanilla, police and citizens walk over a corpse like it
> isn't there. Loose Ends makes living NPCs who actually *see* a dead body react - citizens call the
> police, police enter the game's real **"Investigating"** pursuit - and makes carried corpses heavier,
> so hauling a body to its hiding spot has real weight and stress.

![Version](https://img.shields.io/badge/version-1.1.1-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
[![Polyfill](https://polyfill.doomods.com/badge/loose-ends.svg)](https://polyfill.doomods.com/mod/loose-ends)

[![MLVScan LooseEnds.dll](https://mlvscan.com/attestations/att_bEggRi5D-070aL4nvAywhxSi/badge.svg)](https://mlvscan.com/attestations/att_bEggRi5D-070aL4nvAywhxSi)

## What it does

1. **Witnesses react.** A living NPC who actually *sees* a dead NPC reports it. A citizen pulls out
   their phone and calls the police over about 4 seconds; knock them out or kill them before the call
   connects and nobody comes. A police officer reports at once.
   - **The witness saw only the body:** the police do not know who did it. An officer walks to the
     body and searches the area for about 2 minutes. Nobody is wanted. If the officer sees you near
     the body, you become the suspect. Each further body that day sends one more officer, up to 3.
   - **The witness saw you at the body:** they describe you and you become the suspect. They also
     remember your face until the next day and call again if they see you.

   A body that is hidden from sight - in a dumpster, behind a wall, indoors, underwater, in bushes - is
   never seen, so nothing happens. That is the whole point: hide your bodies.
2. **Corpses are heavier.** While you drag a body it resists being pulled: it lags behind, drags
   sluggishly and is heavier to throw, scaled by a configurable multiplier. Your own speed does not
   change.

## Features

- **Real line of sight.** Detection uses each nearby NPC's own vision cone (`VisionCone.IsPointWithinSight`),
  so it honors field of view and the game's occlusion layers. A hidden body is genuinely unseen.
- **Killer attribution.** The heat goes to whoever actually did it, captured at attack time. Unknown
  (environmental / NPC-vs-NPC) deaths are blamed on you in single-player, and on nobody in co-op.
- **One arrest settles your spree.** After an arrest the bodies you left lying around are not reported
  again. In co-op this is per player.
- **Configurable realism.** Line of sight on/off, which NPC types react, sight range, pursuit level,
  reaction delay, phone call or instant report, weight, and more.
- **Cheap when idle.** With no corpses in the world the mod does effectively nothing; the witness scan is
  throttled and only runs while a body exists.
- **Host-authoritative.** Detection and every police/pursuit write run only on the server/host through the
  game's own networked calls, so they replicate to clients.

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API** (pulled in as a dependency).

## Settings

Editable in `UserData/MelonPreferences.cfg`
(category `LooseEnds_01_Main`):

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | on | Master switch. Off = fully vanilla. |
| `EnableInMultiplayer` | on | Witnesses also react in co-op. Only the host's value counts. |
| `RequireLineOfSight` | on | Walls, dumpsters, buildings, water and cover hide a body. |
| `BodySightRange` | 12 m | How far an NPC notices a body in their field of view. |
| `NoticeRadius` | 6 m | An NPC this close notices a body in any direction. |
| `ReactionDelaySeconds` | 3 | Delay after the first sighting before the witness reacts. |
| `WitnessCallsPolice` | on | A citizen phones the police over about 4 s. Off = instant report. |
| `PursuitLevel` | 1 | Instant report on a killer the witness saw: 1 Investigating, 2 Under arrest, 3 Wanted, 4 Wanted dead. |
| `OncePerCorpse` | on | Report each body at most once. |
| `CorpseWeightMultiplier` | 5 | How hard a dragged body resists (1 = vanilla, up to 20). |

Plus `ReactCitizens`, `ReactPolice`, `ReactEmployees`, `ReactToKnockedOut`, `RequirePlayerAlsoSeen`,
`ResponseCooldownSeconds` and `AttributeUnknownToLocalPlayer`.

## Multiplayer

The witness system is host-authoritative: the host runs it for every player, and only the host's
`EnableInMultiplayer` counts.

Updating from 1.1.1 keeps your old setting, which was off. Set `EnableInMultiplayer = true` in
`UserData/MelonPreferences.cfg` to turn it on.

## License

MIT. See the included LICENSE.md.
