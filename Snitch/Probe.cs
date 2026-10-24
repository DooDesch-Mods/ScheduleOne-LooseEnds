#if SNITCH
using System;
using System.Text;
using UnityEngine;
using Il2CppScheduleOne.PlayerScripts;   // Player, PlayerCrimeData
using Snitch.Api;                 // Profiler, Panel, StateSnapshot
using LooseEnds.Detection;        // CorpseTracker, SightingScanner, CorpseRecord
using LooseEnds.Reaction;         // ReactionDispatcher.SafeCode
using LooseEnds.Weight;           // CorpseWeight

namespace LooseEnds.Profiling
{
    /// <summary>
    /// DEBUG-only Snitch instrumentation for LooseEnds: the witness and body pipeline's counts and live state in a
    /// Snitch panel ("Loose Ends"). Found by Profiler.AutoRegister (called from Core) by its name and Register()
    /// method. Everything is a no-op when the Snitch host is absent. Compiled only when SNITCH is defined (Debug +
    /// EnableSnitch); excluded from Release. See Workspace/build/Snitch.props.
    ///
    /// The panel id "LooseEnds" matches the "LooseEnds." counter prefix so the counters group under it.
    /// </summary>
    internal static class SnitchProbe
    {
        // Reused per poll so the main-thread Text provider does not allocate a fresh builder each frame.
        private static readonly StringBuilder _sb = new StringBuilder(256);

        public static void Register()
        {
            Panel p = Profiler.RegisterPanel("LooseEnds", "Loose Ends");

            p.Counter("Corpses", () => CorpseTracker.Count, "bodies");
            p.Counter("ScanChecks", () => SightingScanner.LastChecks, "checks");
            p.Counter("WeightedBodies", () => CorpseWeight.ActiveCount, "carried");

            p.State(() =>
            {
                int discovered = 0, calling = 0, dispatched = 0, settled = 0;
                foreach (CorpseRecord rec in CorpseTracker.Records)
                {
                    if (rec.Discovered) discovered++;
                    if (rec.Calling) calling++;
                    if (rec.Dispatched) dispatched++;
                    if (rec.Settled) settled++;
                }
                return new StateSnapshot { Title = "Corpses" }
                    .Add("tracked", CorpseTracker.Count)
                    .Add("discovered", discovered)
                    .Add("calling", calling)
                    .Add("dispatched", dispatched)
                    .Add("settled", settled)
                    .Add("carried", CorpseWeight.ActiveCount);
            });

            p.Text(BuildStateText);
            p.Action("Give Arsenal", () => LooseEnds.Debugging.DebugArsenal.Give());
            p.Log();

            // 'snitch ablate looseends.scan': causal cost of the whole sighting pipeline (distance pre-cull plus the
            // engine's vision-cone and occlusion raycasts). Host-only by construction; measure with a body present.
            Profiler.RegisterAblationLever("looseends.scan",
                apply: () => SightingScanner.ScanDisabled = true,
                restore: () => SightingScanner.ScanDisabled = false);

            Core.Log?.Msg("[Snitch] Loose Ends panel registered.");
        }

        /// <summary>Multi-line readout: activation, bodies, nearest body and its killer, your pursuit level.</summary>
        private static string BuildStateText()
        {
            _sb.Clear();
            _sb.Append("active: ").Append(Core.WitnessStatus).Append('\n');

            int npcCount = -1;
            try { var reg = NPCManager.NPCRegistry; if (reg != null) npcCount = reg.Count; }
            catch (Exception e) { Core.Caught("snitch: NPC count", e); }

            Vector3 ppos = Vector3.zero;
            bool havePlayer = false;
            string pursuit = "?";
            try
            {
                Player local = Player.Local;
                if (local != null)
                {
                    ppos = local.transform.position;
                    havePlayer = true;
                    PlayerCrimeData cd = local.CrimeData;
                    if (cd != null) pursuit = cd.CurrentPursuitLevel.ToString();
                }
            }
            catch (Exception e) { Core.Caught("snitch: local player state", e); }

            float nearestDist = -1f;
            string nearestKiller = "-";
            if (havePlayer)
            {
                foreach (CorpseRecord rec in CorpseTracker.Records)
                {
                    if (rec.Npc == null) continue;
                    float d = Vector3.Distance(CorpseTracker.BodyPosition(rec.Npc), ppos);
                    if (nearestDist >= 0f && d >= nearestDist) continue;
                    nearestDist = d;
                    nearestKiller = rec.Killer != null ? ReactionDispatcher.SafeCode(rec.Killer) : "unknown";
                }
            }

            _sb.Append("corpses: ").Append(CorpseTracker.Count)
               .Append("  NPCs: ").Append(npcCount)
               .Append("  carrying: ").Append(CorpseWeight.ActiveCount > 0 ? "YES" : "no").Append('\n');
            _sb.Append("nearest corpse: ")
               .Append(nearestDist >= 0f ? nearestDist.ToString("F0") + "m" : "-")
               .Append("  killer: ").Append(nearestKiller).Append('\n');
            _sb.Append("pursuit: ").Append(pursuit);
            return _sb.ToString();
        }
    }
}
#endif
