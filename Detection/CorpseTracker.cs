using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Config;
using LooseEnds.Killer;
using LooseEnds.Reaction;
using LooseEnds.Weight;

namespace LooseEnds.Detection
{
    /// <summary>
    /// Maintains the small live set of NPC corpses in the world. Populated two ways: event-driven (the NPCHealth.Die
    /// postfix calls <see cref="OnNpcDied"/> for instant, low-latency tracking) and a periodic <see cref="Reconcile"/>
    /// safety net over NPCManager.NPCRegistry (catches NPC-vs-NPC / environmental deaths, bodies already down on load,
    /// and prunes revived/despawned corpses). Keeping this set tiny is the main performance lever: when it is empty,
    /// the per-frame scan is a single count check.
    /// </summary>
    internal static class CorpseTracker
    {
        private static readonly Dictionary<int, CorpseRecord> _corpses = new Dictionary<int, CorpseRecord>();
        private static readonly List<int> _toRemove = new List<int>();
        private static readonly List<string> _removeReason = new List<string>();

        internal static int Count => _corpses.Count;
        internal static Dictionary<int, CorpseRecord>.ValueCollection Records => _corpses.Values;

        /// <summary>Event-driven registration from the NPCHealth.Die / KnockOut postfixes.</summary>
        internal static void OnNpcDied(NPC npc, Player killer)
        {
            if (npc == null) return;
            int id = npc.GetInstanceID();
            if (!_corpses.TryGetValue(id, out CorpseRecord rec))
            {
                rec = new CorpseRecord { Npc = npc, Id = id };
                _corpses[id] = rec;
                Core.LogDebug($"[Corpse] tracked new corpse id={id} (now {_corpses.Count})");
            }
            if (killer != null) rec.Killer = killer;
        }

        /// <summary>
        /// Where the body actually rests. Since 0.4.7 the ragdoll's Draggable gets the ragdoll root Rigidbody handed in
        /// when the NPC falls, so that Rigidbody is the body; the Draggable's own transform and the NPC root stay
        /// behind at the spot where the NPC was standing. On the host, a ragdoll far from the host's own player
        /// reports a Rigidbody position of (0,0,0); its transform is used then, and the NPC root when that is zero too.
        /// </summary>
        internal static Vector3 BodyPosition(NPC npc)
        {
            try
            {
                NPCMovement mv = npc.Movement;
                Draggable drag = mv != null ? mv._ragdollDraggable : null;
                Rigidbody rb = drag != null ? drag._rigidbody : null;
                if (rb != null)
                {
                    Vector3 p = rb.position;
                    if (p.sqrMagnitude > 0.01f) return p;
                    p = rb.transform.position;
                    if (p.sqrMagnitude > 0.01f)
                    {
                        Core.WarnOnce("corpse-rb-zero", $"ragdoll Rigidbody of {ReactionDispatcher.SafeName(npc)} reports (0,0,0) - using its transform.");
                        return p;
                    }
                    Core.WarnOnce("corpse-rb-transform-zero", $"ragdoll of {ReactionDispatcher.SafeName(npc)} sits at (0,0,0) - using the NPC root.");
                }
            }
            catch (Exception e) { Core.Caught("corpse: ragdoll body position", e); }
            return npc.transform.position;
        }

        /// <summary>
        /// Periodic safety-net reconcile (call on a ~2s cadence). Adds dead-but-untracked NPCs and removes corpses
        /// that revived or whose object is gone. Robust to destroyed Il2Cpp objects (per-record try/catch).
        /// </summary>
        internal static void Reconcile()
        {
            bool includeKnockedOut = Preferences.ReactToKnockedOut;

            // 1) add dead-but-untracked
            try
            {
                var reg = NPCManager.NPCRegistry;
                if (reg != null)
                {
                    int n = reg.Count;
                    for (int i = 0; i < n; i++)
                    {
                        NPC npc = reg[i];
                        if (npc == null) continue;
                        bool down;
                        try
                        {
                            NPCHealth h = npc.Health;
                            if (h == null) continue;
                            down = h.IsDead || (includeKnockedOut && h.IsKnockedOut);
                        }
                        catch (Exception e) { Core.Caught("reconcile: read NPC health", e); continue; }
                        if (!down) continue;
                        int id = npc.GetInstanceID();
                        if (!_corpses.ContainsKey(id))
                        {
                            _corpses[id] = new CorpseRecord { Npc = npc, Id = id, Killer = KillerRegistry.GetKiller(npc) };
                            Core.LogDebug($"[Corpse] reconcile added corpse id={id} (now {_corpses.Count})");
                        }
                    }
                }
            }
            catch (Exception e) { Core.Caught("reconcile: walk NPC registry", e); }

            // 2) remove revived / destroyed
            _toRemove.Clear();
            _removeReason.Clear();
            foreach (KeyValuePair<int, CorpseRecord> kv in _corpses)
            {
                NPC npc = kv.Value.Npc;
                string reason = null;
                try
                {
                    if (npc == null) reason = "object gone";
                    else
                    {
                        NPCHealth h = npc.Health;
                        if (!(h != null && (h.IsDead || (includeKnockedOut && h.IsKnockedOut)))) reason = "revived";
                    }
                }
                catch (Exception e) { reason = "object destroyed (" + e.GetType().Name + ")"; }   // a destroyed Il2Cpp object throws on access
                if (reason == null) continue;
                _toRemove.Add(kv.Key);
                _removeReason.Add(reason);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                int id = _toRemove[i];
                // If this body was being carried and just got revived in place, the game disabled its Draggable without
                // firing StopDragging - restore its physics now so the heavy-corpse modifier never leaks onto the living NPC.
                CorpseWeight.RestoreForNpcId(id);
                _corpses.Remove(id);
                KillerRegistry.ForgetId(id);
                Core.Log?.Msg($"[Corpse] untracked id={id}: {_removeReason[i]} (now {_corpses.Count})");
            }

            KillerRegistry.PruneStale();
        }

        internal static void Clear()
        {
            _corpses.Clear();
        }

        /// <summary>
        /// The player was arrested: every body blamed on them (or on nobody) is settled - it stays in the world but never
        /// raises another response, and a call still ringing for it is cancelled so it cannot re-raise the pursuit the
        /// arrest just cleared. Bodies of other co-op players stay open. Returns how many bodies were newly settled.
        /// </summary>
        internal static int SettleFor(Player arrested)
        {
            int settled = 0;
            int arrestedId = arrested.GetInstanceID();
            foreach (CorpseRecord rec in _corpses.Values)
            {
                if (rec.Settled) continue;
                Player blamed = rec.Calling ? rec.CallKiller : rec.Killer;
                if (blamed != null && blamed.GetInstanceID() != arrestedId) continue;
                if (rec.Calling) Reaction.ReactionDispatcher.AbortCall(rec, "killer arrested");
                rec.Discovered = true;
                rec.Dispatched = true;
                rec.Settled = true;
                settled++;
            }
            return settled;
        }
    }
}
