using System;
using System.Collections.Generic;
using MelonLoader;
using S1API.Lifecycle;
using LooseEnds.Config;
using LooseEnds.Detection;
using LooseEnds.Killer;
using LooseEnds.Networking;
using LooseEnds.Reaction;
#if SNITCH
using Snitch.Api;                 // Profiler section timing (Debug + EnableSnitch only; no-op when host absent)
#endif

[assembly: MelonInfo(typeof(LooseEnds.Core), "Loose Ends", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-LooseEnds")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace LooseEnds
{
    /// <summary>
    /// MelonLoader entry point for LooseEnds. Living NPCs who SEE a dead body react (citizens call the police,
    /// police begin the "Investigating" pursuit), and carried corpses are made heavier. The witness system is
    /// host-authoritative (runs only on the FishNet server, true even in single-player) and stays off in a real
    /// co-op lobby when EnableInMultiplayer is turned off. The throttled scan only does work while a corpse exists.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        // How often the witness scan runs. Fast enough that a body in plain view is noticed within half a second.
        private const float ScanIntervalSeconds = 0.4f;
        // Safety-net reconcile cadence for the live corpse set.
        private const float ReconcileIntervalSeconds = 2f;

        private bool _inWorld;
        private float _scanAccum;
        private float _reconcileAccum;
        // Last logged activation state (-1 unknown, 0 off-pref, 1 off-MP, 2 not-authority, 3 active) - dedups re-arm logs.
        private int _activeLogState = -1;

        // Keys already reported by Caught / Failed. Several callers sit in per-frame paths, so each site logs once.
        private static readonly HashSet<string> _reported = new HashSet<string>();

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string msg) { Log?.Msg(msg); }

        /// <summary>A caught exception the mod deliberately carries on after. Logs a warning once per <paramref name="where"/>.</summary>
        internal static void Caught(string where, Exception e)
        {
            if (!_reported.Add(where)) return;
            Log?.Warning("[Caught] " + where + ": " + e.GetType().Name + ": " + e.Message);
        }

        /// <summary>A tolerated state without an exception. Logs a warning once per <paramref name="where"/>.</summary>
        internal static void WarnOnce(string where, string message)
        {
            if (!_reported.Add(where)) return;
            Log?.Warning("[" + where + "] " + message);
        }

        /// <summary>A caught exception that made an action fail. Logs an error once per <paramref name="where"/>.</summary>
        internal static void Failed(string where, Exception e)
        {
            if (!_reported.Add(where)) return;
            Log?.Error("[Failed] " + where + ": " + e.GetType().Name + ": " + e.Message);
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            Preferences.Initialize();
            // MelonLoader has applied every [HarmonyPatch] class of this assembly before this method runs. A PatchAll
            // here would apply each a second time, and every postfix would then run twice per call.

            // Discovered-corpse + killer-attribution maps are in-memory only; wipe them on save/scene change so state
            // never leaks across saves (the players' own crime/pursuit state is persisted by the game, not by us).
            GameLifecycle.OnSaveStart += ResetState;
            GameLifecycle.OnPreSceneChange += ResetState;

#if SNITCH
            // The Snitch host finds Profiling.SnitchProbe.Register by name, but only after something asks it to look.
            Profiler.AutoRegister();
#endif
#if DEBUG
            Log.Msg($"Loose Ends v{DooDesch.ModVersion.Current} (DEBUG). Dev console: 'le help'.");
#else
            Log.Msg($"Loose Ends v{DooDesch.ModVersion.Current} loaded.");
#endif
        }

        /// <summary>Clears all in-memory mod state (corpse tracking, killer attribution) on save / scene change.</summary>
        private static void ResetState()
        {
            ReactionDispatcher.CancelAllCalls();
            SceneSearch.Clear();
            WitnessMemory.Clear();
            CorpseTracker.Clear();
            KillerRegistry.Clear();
            ArrestWatcher.Clear();
            Weight.CorpseWeight.RestoreAll();   // restore (not just drop) carried-body physics before wiping the cache
            Log?.Msg("[Core] State reset (save or scene change).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _inWorld = sceneName == "Main";
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName != "Main") return;
            _inWorld = false;
            _activeLogState = -1;
            WitnessStatus = "[not in world]";
            ResetState();
        }

        /// <summary>Short human-readable activation state, surfaced by the Snitch panel and 'le status' (Debug only).</summary>
        internal static string WitnessStatus = "init";

        /// <summary>
        /// Decides whether the witness system should be doing work this frame, honoring the master switch, the
        /// host-authority requirement (server only) and the co-op setting. Logs each state transition once.
        /// </summary>
        private bool WitnessActive()
        {
            if (!Preferences.Enabled)
            {
                WitnessStatus = "[OFF: disabled in settings]";
                if (_activeLogState != 0) { Log.Msg("[Core] Disabled via preferences - running vanilla."); _activeLogState = 0; }
                return false;
            }
            if (Net.IsCoop() && !Preferences.EnableInMultiplayer)
            {
                WitnessStatus = "[OFF: co-op - EnableInMultiplayer is off]";
                if (_activeLogState != 1) { Log.Msg("[Core] Co-op session - witness system off (EnableInMultiplayer = false)."); _activeLogState = 1; }
                return false;
            }
            if (!Net.IsServer)
            {
                // Not the authority (a connected client): the host runs detection + dispatch for everyone.
                WitnessStatus = "[client - host runs detection]";
                if (_activeLogState != 2) { Log.Msg("[Core] Not the network authority - witness detection runs on the host."); _activeLogState = 2; }
                return false;
            }
            WitnessStatus = "[ACTIVE]";
            if (_activeLogState != 3) { Log.Msg("[Core] Witness system ACTIVE."); _activeLogState = 3; }
            return true;
        }

        public override void OnUpdate()
        {
            if (!_inWorld) return;

            if (!WitnessActive()) return;

            // One arrest settles that player's spree - clear their killer attribution and resolve their bodies.
            ArrestWatcher.Tick();

            float dt = Time.deltaTime;

            // Both outlive the body: a search runs after the corpse is gone, a witness remembers until the next day.
            SceneSearch.Tick(dt);
            WitnessMemory.Tick(dt);

            // Safety-net reconcile: keep the live corpse set in sync with the world.
            _reconcileAccum += dt;
            if (_reconcileAccum >= ReconcileIntervalSeconds)
            {
                _reconcileAccum = 0f;
#if SNITCH
                using (Profiler.Sample("LooseEnds.Reconcile")) CorpseTracker.Reconcile();
#else
                CorpseTracker.Reconcile();
#endif
            }

            // The big perf lever: with no corpses in the world, do nothing else.
            if (CorpseTracker.Count == 0)
            {
                _scanAccum = 0f;
                return;
            }

            _scanAccum += dt;
            if (_scanAccum < ScanIntervalSeconds) return;
            _scanAccum = 0f;

#if SNITCH
            using (Profiler.Sample("LooseEnds.WitnessTick")) WitnessTick();
#else
            WitnessTick();
#endif
        }

        /// <summary>The throttled detection + reaction pass: scan for sightings, then run pending reactions.</summary>
        private static void WitnessTick()
        {
            // 1) find new sightings (latches Discovered on the corpse records).
#if SNITCH
            using (Profiler.Sample("LooseEnds.Scan")) SightingScanner.Scan();
#else
            SightingScanner.Scan();
#endif

            // 2) fire reactions for corpses whose reaction delay has elapsed; re-arm re-triggers when allowed.
            float now = Time.time;
            float delay = Preferences.ReactionDelaySeconds;
            bool oncePerCorpse = Preferences.OncePerCorpse;
            float cooldown = Preferences.ResponseCooldownSeconds;

            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                // A call already in progress: advance it (connect -> scene, or witness silenced -> re-arm).
                if (rec.Calling)
                {
                    ReactionDispatcher.UpdateCall(rec, now);
                    continue;
                }

                if (!rec.Discovered) continue;
                if (rec.Dispatched)
                {
                    if (!oncePerCorpse && !rec.Settled && now - rec.DispatchedTime >= cooldown)
                    {
                        rec.Discovered = false;
                        rec.Dispatched = false;
                        rec.Discoverer = null;   // re-arm: the scanner can re-discover it
                    }
                    continue;
                }
                if (now - rec.FirstSeenTime < delay) continue;
                ReactionDispatcher.TryStartResponse(rec);
            }
        }
    }
}
