#if DEBUG
using System;
using HarmonyLib;
using UnityEngine;
using LooseEnds.Config;
using LooseEnds.Detection;
using LooseEnds.Networking;
using LooseEnds.Reaction;
using LooseEnds.Weight;

namespace LooseEnds.Debugging
{
    /// <summary>
    /// DEBUG-only dev console command <c>le</c>. The game's console does not log its own output, so every answer goes
    /// to the MelonLoader log. Both Console.SubmitCommand overloads are patched (the string overload calls the List
    /// overload, and depending on the build either prefix is the one that fires), so dispatch dedupes per frame and
    /// command line. Compiled out of Release.
    /// </summary>
    internal static class DevConsole
    {
        private const string Usage =
            "le status | le kill [other] | le ko [other] | le silence | le find | le drag | le drop | le arrest [other] | le arsenal | le scanlog on|off | le pursuit 0-4 | le mp on|off";

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        /// <summary>True when the line was ours and the game must not see it.</summary>
        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0 || !string.Equals(parts[0], "le", StringComparison.OrdinalIgnoreCase)) return false;

            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;   // the second overload of the same submission
            _lastFrame = frame; _lastSig = sig;

            string sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";
            string arg = parts.Length > 2 ? parts[2].ToLowerInvariant() : "";
            try
            {
                switch (sub)
                {
                    case "status": Status(); break;
                    case "kill": HarmNearest(lethal: true, arg == "other"); break;
                    case "ko": HarmNearest(lethal: false, arg == "other"); break;
                    case "find": ForceDiscoverNearest(); break;
                    case "silence": SilenceCaller(); break;
                    case "drag": DragNearestBody(); break;
                    case "drop": DropBody(); break;
                    case "arrest": Arrest(arg == "other"); break;
                    case "arsenal": DebugArsenal.Give(); break;
                    case "scanlog":
                        DevOverrides.LogWitnessScan = arg != "off";
                        Core.Log?.Msg($"[Dev] witness scan log {(DevOverrides.LogWitnessScan ? "on" : "off")}.");
                        break;
                    case "mp":
                        DevOverrides.ForceEnableInMultiplayer = arg != "off";
                        Core.Log?.Msg($"[Dev] witness system in co-op {(DevOverrides.ForceEnableInMultiplayer ? "forced on" : "back to the setting")}.");
                        break;
                    case "pursuit":
                        if (int.TryParse(arg, out int lvl) && lvl >= 0 && lvl <= 4)
                        {
                            DevOverrides.ForcePursuitLevel = lvl;
                            Core.Log?.Msg(lvl == 0
                                ? $"[Dev] pursuit level back to the setting ({Preferences.PursuitLevelInt})."
                                : $"[Dev] pursuit level forced to {lvl}.");
                        }
                        else Core.Log?.Warning("[Dev] le pursuit <0-4>  (0 = use the setting)");
                        break;
                    default: Core.Log?.Msg("[Dev] " + Usage); break;
                }
            }
            catch (Exception e) { Core.Failed("dev console 'le " + sub + "'", e); }
            return true;
        }

        private static bool RequireHost(string what)
        {
            if (Net.IsServer) return true;
            Core.Log?.Warning($"[Dev] le {what}: run this on the host.");
            return false;
        }

        /// <summary>The first player in the session who is not the local one (co-op tests from the host), or null.</summary>
        private static Player OtherPlayer()
        {
            var list = Player.PlayerList;
            Player local = Player.Local;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                Player p = list[i];
                if (p != null && (local == null || p.GetInstanceID() != local.GetInstanceID())) return p;
            }
            return null;
        }

        private static Vector3 LocalPos()
        {
            Player local = Player.Local;
            if (local == null) throw new InvalidOperationException("no local player");
            return local.transform.position;
        }

        private static void Status()
        {
            int discovered = 0, calling = 0, dispatched = 0, settled = 0;
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (rec.Discovered) discovered++;
                if (rec.Calling) calling++;
                if (rec.Dispatched) dispatched++;
                if (rec.Settled) settled++;
            }
            string pursuit = "?";
            Player local = Player.Local;
            if (local != null && local.CrimeData != null) pursuit = local.CrimeData.CurrentPursuitLevel.ToString();
            Core.Log?.Msg($"[Dev] active={Core.WitnessStatus} server={Net.IsServer} coop={Net.IsCoop()} bodies={CorpseTracker.Count}"
                + $" discovered={discovered} calling={calling} dispatched={dispatched} settled={settled}"
                + $" carried={CorpseWeight.ActiveCount} checks={SightingScanner.LastChecks} pursuit={pursuit}");
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                float d = -1f;
                if (local != null && rec.Npc != null) d = Vector3.Distance(CorpseTracker.BodyPosition(rec.Npc), local.transform.position);
                Core.Log?.Msg($"[Dev]   {ReactionDispatcher.SafeName(rec.Npc)} id={rec.Id} {d:F0}m killer={ReactionDispatcher.SafeCode(rec.Killer)}"
                    + $" discovered={rec.Discovered} calling={rec.Calling} dispatched={rec.Dispatched} settled={rec.Settled}");
            }
        }

        /// <summary>
        /// Kill or knock out the living NPC nearest the attacker through the game's own health path. The attacker is the
        /// local player, or with 'other' the first other player in the session.
        /// </summary>
        private static void HarmNearest(bool lethal, bool other)
        {
            string what = lethal ? "kill" : "ko";
            if (!RequireHost(what)) return;
            Player attacker = other ? OtherPlayer() : Player.Local;
            if (attacker == null) { Core.Log?.Warning($"[Dev] le {what}: no {(other ? "other " : "")}player."); return; }
            Vector3 ppos = attacker.transform.position;
            var reg = NPCManager.NPCRegistry;
            if (reg == null) { Core.Log?.Warning($"[Dev] le {what}: NPC registry not ready."); return; }

            NPC best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < reg.Count; i++)
            {
                NPC npc = reg[i];
                NPCHealth h = npc != null ? npc.Health : null;
                if (h == null || h.IsDead || h.IsKnockedOut || !npc.gameObject.activeInHierarchy) continue;
                float d = (npc.transform.position - ppos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = npc; }
            }
            if (best == null) { Core.Log?.Warning($"[Dev] le {what}: no living NPC found."); return; }

            // Attribute it to the local player the way a real attack does, so killer tracking runs too.
            Killer.KillerRegistry.RecordAttacker(best, attacker);
            Core.Log?.Msg($"[Dev] le {what}: {ReactionDispatcher.SafeName(best)} at {Mathf.Sqrt(bestD):F1}m from {ReactionDispatcher.SafeCode(attacker)}.");
            if (lethal) best.Health.Die();
            else best.Health.KnockOut();
        }

        /// <summary>Start the police response for the tracked body nearest the player, skipping sight and delay.</summary>
        private static void ForceDiscoverNearest()
        {
            if (!RequireHost("find")) return;
            Vector3 ppos = LocalPos();
            CorpseRecord best = null;
            float bestD = float.MaxValue;
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (rec.Npc == null || rec.Settled) continue;
                float d = (CorpseTracker.BodyPosition(rec.Npc) - ppos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = rec; }
            }
            if (best == null) { Core.Log?.Warning("[Dev] le find: no open body tracked. Make one with 'le kill'."); return; }

            best.Discovered = true;
            best.FirstSeenTime = Time.time - 1000f;   // past any reaction delay
            best.Dispatched = false;
            best.Calling = false;
            best.Discoverer = null;
            ReactionDispatcher.TryStartResponse(best);
            Core.Log?.Msg($"[Dev] le find: response started for {ReactionDispatcher.SafeName(best.Npc)} (dispatched={best.Dispatched}).");
        }

        /// <summary>
        /// Pick up the body nearest the player through the game's own DragManager, the same call a click on a body makes.
        /// Runs the body-weight patch, so the [Weight] lines show what was changed.
        /// </summary>
        private static void DragNearestBody()
        {
            Vector3 ppos = LocalPos();
            Il2CppScheduleOne.Dragging.Draggable best = null;
            float bestD = float.MaxValue;
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                var d = rec.Npc != null && rec.Npc.Movement != null ? rec.Npc.Movement._ragdollDraggable : null;
                if (d == null) continue;
                float dist = (CorpseTracker.BodyPosition(rec.Npc) - ppos).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            if (best == null) { Core.Log?.Warning("[Dev] le drag: no tracked body with a ragdoll draggable."); return; }
            var dm = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Dragging.DragManager>.Instance;
            if (dm == null) { Core.Log?.Warning("[Dev] le drag: no DragManager."); return; }
            dm.StartDragging(best);
            Core.Log?.Msg($"[Dev] le drag: dragging the body {Mathf.Sqrt(bestD):F1}m away, weighted bodies={CorpseWeight.ActiveCount}.");
        }

        private static void DropBody()
        {
            var dm = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Dragging.DragManager>.Instance;
            if (dm == null || dm.CurrentDraggable == null) { Core.Log?.Warning("[Dev] le drop: not dragging anything."); return; }
            dm.StopDragging(Vector3.zero);
            Core.Log?.Msg($"[Dev] le drop: dropped, weighted bodies={CorpseWeight.ActiveCount}.");
        }

        /// <summary>Knock out the witness whose police call is ringing, the way a player silences them in time.</summary>
        private static void SilenceCaller()
        {
            if (!RequireHost("silence")) return;
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (!rec.Calling || rec.Discoverer == null) continue;
                NPC witness = rec.Discoverer;
                Killer.KillerRegistry.RecordAttacker(witness, Player.Local);
                witness.Health.KnockOut();
                Core.Log?.Msg($"[Dev] le silence: knocked out {ReactionDispatcher.SafeName(witness)}, who was calling about {ReactionDispatcher.SafeName(rec.Npc)}.");
                return;
            }
            Core.Log?.Warning("[Dev] le silence: no witness is calling right now.");
        }

        /// <summary>
        /// Arrest a player through the game's arrest RPC: the local one (works on a client too, the RPC goes to the
        /// host), or with 'other' the first other player, from the host.
        /// </summary>
        private static void Arrest(bool other)
        {
            if (other && !RequireHost("arrest other")) return;
            Player p = other ? OtherPlayer() : Player.Local;
            if (p == null) { Core.Log?.Warning($"[Dev] le arrest: no {(other ? "other" : "local")} player."); return; }
            p.Arrest_Server();
            Core.Log?.Msg($"[Dev] le arrest: arrest sent for {ReactionDispatcher.SafeCode(p)}.");
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Le_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !DevConsole.TryHandle(args); }
            catch (Exception e) { Core.Failed("dev console (string overload)", e); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Le_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !DevConsole.TryHandle(args); }
            catch (Exception e) { Core.Failed("dev console (list overload)", e); return true; }
        }
    }
}
#endif
