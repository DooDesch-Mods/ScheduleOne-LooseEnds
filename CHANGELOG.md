# Changelog

All notable changes to Loose Ends are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-20

Initial release.

### Added
- **Witnesses react.** Living NPCs (citizens and police) who actually see a dead NPC report it: citizens
  get the police called, police enter the game's real "Investigating" pursuit. Bodies hidden from sight
  (dumpster, indoors, underwater, behind cover) are not seen, so the gameplay is "hide your bodies".
- **Real line of sight** via each NPC's own vision cone (`VisionCone.IsPointWithinSight`), honoring field
  of view and the game's occlusion layers.
- **Killer attribution** captured at attack time, so the heat goes to whoever did it; unknown
  (environmental / NPC-vs-NPC) deaths fall back sensibly (single-player blames the local player, co-op uses
  a scene response).
- **Heavier corpses.** A carried corpse is made heavier (configurable, default ~5x) so disposal has weight
  and stress - it slows the carrier down and the body drags more sluggishly.
- **Configurable realism.** Hunt the killer / Scene investigation / Escalating, line-of-sight on/off, which
  NPC types react, pursuit level, reaction delay, cooldowns, weight, and more - in the in-game Mod Manager /
  Phone App or `MelonPreferences.cfg`.
- **Cheap when idle** - the throttled witness scan only runs while a corpse exists.
- **Host-authoritative;** multiplayer is off by default (`EnableInMultiplayer`) until tested with two players.
