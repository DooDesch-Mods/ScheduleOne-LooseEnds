using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Config;

namespace LooseEnds.Weight
{
    /// <summary>
    /// Makes a dragged body (dead OR knocked-out) feel heavy to PULL - the body resists, instead of slowing the
    /// player (which felt bad). While a body is carried we weaken its follow spring (lower DragForceMultiplier so it
    /// lags behind the carry point), crank its linear/angular drag (it moves sluggishly and stops fast), and raise its
    /// mass a little (heavier to throw: DragManager divides the throw by sqrt(mass)). All cached per-Draggable and
    /// restored exactly on drop, so the effect is scoped to carrying and never leaks. Independent of the witness
    /// system; runs on every peer, since each peer applies the drag forces of whoever drags.
    /// </summary>
    internal static class CorpseWeight
    {
        private struct Saved
        {
            public Draggable D;     // the draggable we modified (to restore DragForceMultiplier without being handed it)
            public int NpcId;       // owning NPC instance id (to restore proactively when that NPC is un-tracked)
            public float DragMult;
            public Rigidbody Rb;
            public float Drag;
            public float AngularDrag;
            public float Mass;
        }

        private static readonly Dictionary<int, Saved> _active = new Dictionary<int, Saved>();
        private static readonly List<int> _scratch = new List<int>();

        /// <summary>How many bodies are currently weighted (i.e. being carried). For the Snitch panel.</summary>
        internal static int ActiveCount => _active.Count;

        internal static void OnStartDragging(Draggable d)
        {
            if (!Preferences.Enabled || d == null) return;

            float mult = Preferences.CorpseWeightMultiplier;
            if (mult <= 1.0001f) return;   // vanilla weight

            int id = d.GetInstanceID();
            if (_active.ContainsKey(id)) return;   // already weighted (idempotent)

            NPC owner = BodyOwner(d);
            if (owner == null) return;   // a crate, a trash bag, a living NPC: not ours

            Rigidbody rb = d._rigidbody;
            if (rb == null)
            {
                Core.Log?.Warning($"[Weight] body draggable id={id} has no Rigidbody - left at vanilla weight.");
                return;
            }

            // The drag spring uses ForceMode.Acceleration (mass-independent), so the felt weight comes from a weaker
            // follow (DragForceMultiplier) plus real resistance (Rigidbody.drag). Mass only adds throw/collision heft.
            Saved s = new Saved
            {
                D = d,
                NpcId = owner.GetInstanceID(),
                DragMult = d.DragForceMultiplier,
                Rb = rb,
                Drag = rb.drag,
                AngularDrag = rb.angularDrag,
                Mass = rb.mass,
            };
            _active[id] = s;
            d.DragForceMultiplier = Mathf.Max(0.05f, s.DragMult / mult);
            rb.drag = s.Drag + (mult - 1f) * 2f;             // strong linear resistance (the "heavy to pull" feel)
            rb.angularDrag = s.AngularDrag + (mult - 1f) * 2f;
            rb.mass = s.Mass * Mathf.Sqrt(mult);            // gentle - heavier to throw, not immovable
            Core.LogDebug($"[Weight] drag start id={id} mult={mult:F1} dragMult {s.DragMult:F2}->{d.DragForceMultiplier:F2} rbDrag {s.Drag:F1}->{rb.drag:F1} mass {s.Mass:F1}->{rb.mass:F1}");
        }

        internal static void OnStopDragging(Draggable d)
        {
            if (d == null) return;
            int id = d.GetInstanceID();
            if (!_active.TryGetValue(id, out Saved s)) return;   // not a body we weighted
            RestoreEntry(s);
            _active.Remove(id);
            Core.LogDebug($"[Weight] drag stop id={id} restored");
        }

        /// <summary>Put one body's physics back exactly as it was (idempotent, guarded against destroyed objects).</summary>
        private static void RestoreEntry(Saved s)
        {
            try { if (s.D != null) s.D.DragForceMultiplier = s.DragMult; }
            catch (Exception e) { Core.Caught("weight: restore DragForceMultiplier (draggable destroyed)", e); }
            try
            {
                if (s.Rb != null)
                {
                    s.Rb.drag = s.Drag;
                    s.Rb.angularDrag = s.AngularDrag;
                    s.Rb.mass = s.Mass;
                }
            }
            catch (Exception e) { Core.Caught("weight: restore Rigidbody (ragdoll destroyed)", e); }
        }

        /// <summary>
        /// Proactively restore a carried body's physics when its NPC stops being a corpse (e.g. revived in place on a new
        /// day). The game disables the ragdoll's Draggable WITHOUT firing StopDragging, so OnStopDragging never runs and
        /// the heavy-physics modification would otherwise leak onto the now-living NPC. Called from the corpse-prune path.
        /// </summary>
        internal static void RestoreForNpcId(int npcId)
        {
            if (npcId == 0 || _active.Count == 0) return;
            _scratch.Clear();
            foreach (KeyValuePair<int, Saved> kv in _active)
            {
                if (kv.Value.NpcId != npcId) continue;
                RestoreEntry(kv.Value);
                _scratch.Add(kv.Key);
            }
            for (int i = 0; i < _scratch.Count; i++) _active.Remove(_scratch[i]);
            if (_scratch.Count > 0) Core.Log?.Msg($"[Weight] restored the physics of a carried body whose NPC revived (npc={npcId}).");
        }

        /// <summary>Restore every carried body's physics, then drop the cache. Use on save/scene reset so the originals
        /// are never lost before they are applied (the cache is wiped at the sleep autosave AFTER NPCs revive).</summary>
        internal static void RestoreAll()
        {
            foreach (KeyValuePair<int, Saved> kv in _active) RestoreEntry(kv.Value);
            _active.Clear();
        }

        /// <summary>
        /// The NPC whose dead or knocked-out ragdoll this draggable is, or null. Since 0.4.7 a ragdoll is an
        /// NPCRagdollDraggable held by the NPC's movement component (NPCMovement._ragdollDraggable). The parent walk is
        /// the cheap path; the registry match finds the owner wherever the draggable sits in the hierarchy.
        /// </summary>
        private static NPC BodyOwner(Draggable d)
        {
            try
            {
                if (d.TryCast<NPCRagdollDraggable>() == null) return null;
                int did = d.GetInstanceID();
                NPC npc = d.GetComponentInParent<NPC>();
                if (npc == null)
                {
                    var reg = NPCManager.NPCRegistry;
                    int n = reg != null ? reg.Count : 0;
                    for (int i = 0; i < n && npc == null; i++)
                    {
                        NPC cand = reg[i];
                        Draggable rd = cand != null && cand.Movement != null ? cand.Movement._ragdollDraggable : null;
                        if (rd != null && rd.GetInstanceID() == did) npc = cand;
                    }
                }
                if (npc == null)
                {
                    Core.Log?.Warning($"[Weight] ragdoll draggable id={did} belongs to no known NPC - left at vanilla weight.");
                    return null;
                }
                NPCHealth h = npc.Health;
                return h != null && (h.IsDead || h.IsKnockedOut) ? npc : null;
            }
            catch (Exception e) { Core.Caught("weight: identify the dragged body", e); return null; }
        }
    }
}
