using System;
using UnityEngine;
using Il2CppScheduleOne.VoiceOver;       // EVOLineType
using Il2CppScheduleOne.Police;          // PoliceOfficer
using Il2CppScheduleOne.Employees;       // Employee

namespace LooseEnds.Reaction
{
    /// <summary>
    /// Makes the NPC that DISCOVERED a body visibly react, so a witness no longer just stands there. This is purely
    /// COSMETIC: the actual police dispatch is owned by <see cref="ReactionDispatcher"/> (which aims the game's
    /// investigation at the scene). A civilian plays an alerted voice line and turns to face the body; they do NOT
    /// flee (we want them to stop and look, not run) and do NOT call the police themselves (that would double-dispatch
    /// and aim officers at the player, breaking the scene flow). Police/employees just get a brief alerted VO.
    /// </summary>
    internal static class CitizenReaction
    {
        /// <summary>Cosmetic-only witness reaction for the instant path. Does not dispatch.</summary>
        internal static void React(NPC discoverer, Vector3 bodyPos)
        {
            if (discoverer == null) return;

            Alert(discoverer);

            bool civilian;
            try { civilian = discoverer.TryCast<PoliceOfficer>() == null && discoverer.TryCast<Employee>() == null; }
            catch (Exception e) { Core.Caught("reaction: witness role", e); return; }
            if (!civilian) return;

            // A civilian stops and turns to face the body (no fleeing, no dispatch).
            try
            {
                NPCMovement mv = discoverer.Movement;
                Vector3 forward = bodyPos - discoverer.transform.position;
                forward.y = 0f;
                if (mv != null && forward.sqrMagnitude > 0.0001f) mv.FaceDirection(forward.normalized);
            }
            catch (Exception e) { Core.Caught("reaction: witness faces the body", e); }
        }

        /// <summary>Just the alerted gasp - used when the native call behaviour drives the rest (phone + facing).</summary>
        internal static void Alert(NPC npc)
        {
            if (npc == null) return;
            try { npc.PlayVO(EVOLineType.Alerted, false); }
            catch (Exception e) { Core.Caught("reaction: alerted voice line", e); }
        }
    }
}
