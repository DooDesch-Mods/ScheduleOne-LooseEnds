using System;
using HarmonyLib;
using LooseEnds.Config;
using LooseEnds.Killer;

namespace LooseEnds.Patches
{
    /// <summary>
    /// Captures killer attribution from the NPC health system. NotifyAttackedByPlayer fires whenever a player damages
    /// an NPC (the uniform hook across civilians / police / employees and all attack types), so we remember the most
    /// recent attacker; Die() promotes that to the confirmed killer. These postfixes only record references and are
    /// therefore safe on every peer - only the host later acts on the data.
    /// </summary>
    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.NotifyAttackedByPlayer))]
    internal static class NPCHealth_NotifyAttackedByPlayer_Patch
    {
        private static void Postfix(NPCHealth __instance, Player player)
        {
            try { KillerRegistry.RecordAttacker(__instance._npc, player); }
            catch (Exception e) { Core.Failed("patch NPCHealth.NotifyAttackedByPlayer (attacker attribution)", e); }
        }
    }

    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.Die))]
    internal static class NPCHealth_Die_Patch
    {
        private static void Postfix(NPCHealth __instance)
        {
            try
            {
                NPC npc = __instance._npc;
                if (npc == null)
                {
                    Core.Log?.Warning("[Killer] NPCHealth.Die on a health component without an NPC - body not tracked.");
                    return;
                }
                if (!__instance.IsDead) return;   // invincible NPCs return from Die without dying
                Player killer = KillerRegistry.RecordKill(npc);
                Detection.CorpseTracker.OnNpcDied(npc, killer);
                Core.Log?.Msg($"[Killer] {Reaction.ReactionDispatcher.SafeName(npc)} died, killer={Reaction.ReactionDispatcher.SafeCode(killer)}.");
            }
            catch (Exception e) { Core.Failed("patch NPCHealth.Die (track the body)", e); }
        }
    }

    /// <summary>
    /// Knockouts never go through Die(), so without this they were only picked up by the ~2s reconcile poll. When
    /// "react to knocked-out NPCs" is enabled, register the body immediately (the periodic reconcile still handles the
    /// pref being toggled on later, despawns, and revives). Attribution uses the recent-attacker fallback since a KO
    /// never promotes a confirmed kill.
    /// </summary>
    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.KnockOut))]
    internal static class NPCHealth_KnockOut_Patch
    {
        private static void Postfix(NPCHealth __instance)
        {
            try
            {
                if (!Preferences.ReactToKnockedOut) return;   // KOs are not witnessed with the default settings
                NPC npc = __instance._npc;
                if (npc == null || !__instance.IsKnockedOut) return;
                Player killer = KillerRegistry.GetKiller(npc);
                Detection.CorpseTracker.OnNpcDied(npc, killer);
                Core.Log?.Msg($"[Killer] {Reaction.ReactionDispatcher.SafeName(npc)} knocked out, attacker={Reaction.ReactionDispatcher.SafeCode(killer)}.");
            }
            catch (Exception e) { Core.Failed("patch NPCHealth.KnockOut (track the body)", e); }
        }
    }
}
