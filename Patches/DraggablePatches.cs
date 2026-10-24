using System;
using HarmonyLib;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Weight;

namespace LooseEnds.Patches
{
    /// <summary>
    /// Hooks corpse carrying to apply / restore the heavier-corpse weight. StartDragging(Player) and StopDragging()
    /// run on every peer (DragManager's SetDragger_Client RPC runs locally too), so each peer weights the body it
    /// simulates. Non-body draggables are ignored by CorpseWeight.
    /// </summary>
    [HarmonyPatch(typeof(Draggable), nameof(Draggable.StartDragging))]
    internal static class Draggable_StartDragging_Patch
    {
        private static void Postfix(Draggable __instance)
        {
            try { CorpseWeight.OnStartDragging(__instance); }
            catch (Exception e) { Core.Failed("patch Draggable.StartDragging (body weight)", e); }
        }
    }

    [HarmonyPatch(typeof(Draggable), nameof(Draggable.StopDragging))]
    internal static class Draggable_StopDragging_Patch
    {
        private static void Postfix(Draggable __instance)
        {
            try { CorpseWeight.OnStopDragging(__instance); }
            catch (Exception e) { Core.Failed("patch Draggable.StopDragging (restore body weight)", e); }
        }
    }
}
