using System;
using HarmonyLib;
using Il2CppScheduleOne.NPCs.Behaviour;  // CallPoliceBehaviour
using LooseEnds.Detection;
using LooseEnds.Reaction;

namespace LooseEnds.Patches
{
    /// <summary>
    /// A witness who found a body but did not see the killer reports the body, not a person. The game's call always
    /// ends in FinalizeCall, which marks its Target wanted and charges them; for such a call the mod ends it itself in
    /// the frame before, so the game never runs FinalizeCall, and sends a scene search instead. While the call runs the
    /// witness looks at the body instead of at the killer. On a client the mod holds no records, so the game runs as is.
    /// </summary>
    [HarmonyPatch(typeof(CallPoliceBehaviour), nameof(CallPoliceBehaviour.BehaviourUpdate))]
    internal static class CallPoliceBehaviour_BehaviourUpdate_Patch
    {
        private const float CallSeconds = 4f;   // CallPoliceBehaviour finalizes at currentCallTime >= 4
        private const int LookPriority = 2;     // the game's own look target for the call uses priority 1

        private static bool Prefix(CallPoliceBehaviour __instance)
        {
            try
            {
                CorpseRecord rec = ReactionDispatcher.SceneOnlyCall(__instance);
                if (rec == null) return true;

                NPC npc = __instance.Npc;
                if (npc != null && npc.Avatar != null && npc.Avatar.LookController != null)
                    npc.Avatar.LookController.OverrideLookTarget(rec.ScenePos + Vector3.up * 0.3f, LookPriority, true);

                if (__instance.currentCallTime + Time.deltaTime < CallSeconds) return true;
                ReactionDispatcher.EndSceneOnlyCall(rec, __instance);
                return false;
            }
            catch (Exception e)
            {
                Core.Failed("call patch: end a scene-only call before the game marks the killer", e);
                return true;
            }
        }
    }
}
