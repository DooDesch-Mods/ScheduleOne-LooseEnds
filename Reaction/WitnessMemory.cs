using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Law;             // DeadlyAssault
using Il2CppScheduleOne.NPCs.Behaviour;  // CallPoliceBehaviour

namespace LooseEnds.Reaction
{
    /// <summary>
    /// A witness who saw the killer at a body remembers them until the next in-game day. When that witness sees the
    /// killer again while the killer is not wanted, they phone the police once more with the game's own call
    /// (Investigating at the killer's position when it connects). An arrest of the killer clears what witnesses
    /// remember about them. Host only.
    /// </summary>
    internal static class WitnessMemory
    {
        private const float RecallCooldownSeconds = 30f;
        private const float CallResultSeconds = 10f;
        private const float TickSeconds = 0.5f;

        private sealed class Entry
        {
            public NPC Witness;
            public int WitnessId;
            public Player Killer;
            public int Day;
            public float LastCall = float.MinValue;
            public float PendingUntil = -1f;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static float _tickAccum;

        internal static int Count => _entries.Count;

        internal static void Remember(NPC witness, Player killer)
        {
            if (witness == null || killer == null) return;
            int id = witness.GetInstanceID();
            int day = SceneSearch.CurrentDay();
            foreach (Entry e in _entries)
            {
                if (e.WitnessId == id && e.Killer != null && e.Killer.GetInstanceID() == killer.GetInstanceID())
                {
                    e.Day = day;
                    return;
                }
            }
            _entries.Add(new Entry { Witness = witness, WitnessId = id, Killer = killer, Day = day });
            Core.Log?.Msg($"[Memory] {ReactionDispatcher.SafeName(witness)} will recognise {ReactionDispatcher.SafeCode(killer)} until tomorrow.");
        }

        /// <summary>The killer was arrested: witnesses forget them. Returns how many memories were dropped.</summary>
        internal static int ForgetKiller(Player killer)
        {
            int id = killer.GetInstanceID();
            return _entries.RemoveAll(e => e.Killer == null || e.Killer.GetInstanceID() == id);
        }

        internal static void Tick(float dt)
        {
            if (_entries.Count == 0) return;
            _tickAccum += dt;
            if (_tickAccum < TickSeconds) return;
            _tickAccum = 0f;

            float now = Time.time;
            int day = SceneSearch.CurrentDay();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];
                if (e.Day != day)
                {
                    Core.Log?.Msg($"[Memory] a new day: {ReactionDispatcher.SafeName(e.Witness)} forgot {ReactionDispatcher.SafeCode(e.Killer)}.");
                    _entries.RemoveAt(i);
                    continue;
                }
                if (e.Witness == null || e.Killer == null)
                {
                    Core.Log?.Msg("[Memory] a witness or killer left the game - memory dropped.");
                    _entries.RemoveAt(i);
                    continue;
                }
                bool alive;
                try { alive = e.Witness.Health != null && !e.Witness.Health.IsDead; }
                catch (Exception ex) { Core.Caught("memory: read witness health", ex); alive = false; }
                if (!alive)
                {
                    Core.Log?.Msg($"[Memory] {ReactionDispatcher.SafeName(e.Witness)} is dead - memory of {ReactionDispatcher.SafeCode(e.Killer)} dropped.");
                    _entries.RemoveAt(i);
                    continue;
                }

                if (e.PendingUntil > 0f)
                {
                    PlayerCrimeData.EPursuitLevel level = Level(e.Killer);
                    if (level != PlayerCrimeData.EPursuitLevel.None)
                    {
                        Core.Log?.Msg($"[Memory] call by {ReactionDispatcher.SafeName(e.Witness)} CONNECTED - {ReactionDispatcher.SafeCode(e.Killer)} is {level}.");
                        e.PendingUntil = -1f;
                    }
                    else if (now > e.PendingUntil)
                    {
                        Core.Log?.Msg($"[Memory] call by {ReactionDispatcher.SafeName(e.Witness)} did not connect (silenced or interrupted).");
                        e.PendingUntil = -1f;
                    }
                    continue;
                }

                if (now - e.LastCall < RecallCooldownSeconds) continue;
                if (!SafeConscious(e.Witness)) continue;
                if (e.Killer.IsArrested || Level(e.Killer) != PlayerCrimeData.EPursuitLevel.None) continue;
                if (!ReactionDispatcher.WitnessSeesKiller(e.Witness, e.Killer)) continue;
                Call(e, now);
            }
        }

        private static void Call(Entry e, float now)
        {
            e.LastCall = now;
            try
            {
                CallPoliceBehaviour cpb = e.Witness.Behaviour != null ? e.Witness.Behaviour.CallPoliceBehaviour : null;
                if (cpb == null)
                {
                    Core.Log?.Warning($"[Memory] {ReactionDispatcher.SafeName(e.Witness)} has no CallPoliceBehaviour - cannot call.");
                    return;
                }
                if (cpb.Enabled)
                {
                    Core.Log?.Msg($"[Memory] {ReactionDispatcher.SafeName(e.Witness)} recognised {ReactionDispatcher.SafeCode(e.Killer)} but is already on the phone.");
                    return;
                }
                CitizenReaction.Alert(e.Witness);
                cpb.ReportedCrime = new DeadlyAssault();
                cpb.Target = e.Killer;
                cpb.Enable_Networked();
                e.PendingUntil = now + CallResultSeconds;
                Core.Log?.Msg($"[Memory] {ReactionDispatcher.SafeName(e.Witness)} recognised {ReactionDispatcher.SafeCode(e.Killer)} - CALLING the police.");
            }
            catch (Exception ex) { Core.Failed("memory: start the recognition call", ex); }
        }

        private static PlayerCrimeData.EPursuitLevel Level(Player p)
        {
            try { return p.CrimeData != null ? p.CrimeData.CurrentPursuitLevel : PlayerCrimeData.EPursuitLevel.None; }
            catch (Exception e) { Core.Caught("memory: read pursuit level", e); return PlayerCrimeData.EPursuitLevel.None; }
        }

        private static bool SafeConscious(NPC npc)
        {
            try { return npc.IsConscious; }
            catch (Exception e) { Core.Caught("memory: read witness IsConscious", e); return false; }
        }

        internal static void Clear() => _entries.Clear();
    }
}
