using System;
using Il2CppScheduleOne.Law;             // LawManager, DeadlyAssault
using Il2CppScheduleOne.Vision;          // VisionCone
using Il2CppScheduleOne.Police;          // PoliceOfficer
using Il2CppScheduleOne.NPCs.Behaviour;  // CallPoliceBehaviour
using LooseEnds.Config;
using LooseEnds.Detection;
using LooseEnds.Networking;
#if SNITCH
using Snitch.Api;                 // Profiler.Log -> the "LooseEnds" panel log channel (Debug + EnableSnitch only)
#endif

namespace LooseEnds.Reaction
{
    /// <summary>
    /// Turns a discovered corpse into a police response. The default flow mirrors how the game already handles a
    /// witnessed crime: a civilian witness pulls out their phone and CALLS the police over the game's own ~4s call
    /// window (native <see cref="CallPoliceBehaviour"/>, with a progress icon over their head). The player can knock the
    /// witness out or kill them before the call connects to stop it. A witness reacts to EVERY body they see - even if
    /// the player is already wanted - the call just refreshes the pursuit and redirects officers to THAT body. When the
    /// call connects, the game raises the suspect to "Investigating" and dispatches officers; we redirect those officers
    /// to the SCENE (the body) rather than the player's live position.
    ///
    /// A police officer who finds a body reports it instantly (you cannot phone-block a cop), as does the instant path
    /// when "Witness phones the police" is off. There is no global throttle: each body triggers its own response once
    /// (OncePerCorpse), so a body left in front of someone is never silently ignored. All writes assume the caller
    /// gated on the server/host.
    /// </summary>
    internal static class ReactionDispatcher
    {
        private const float CallWindowSeconds = 4f;    // game's CallPoliceBehaviour.CALL_POLICE_TIME
        private const float CallTimeoutSeconds = 25f;  // safety net: generous enough for a panicking witness (~15s) to settle then connect
        private const float CallActivationGrace = 1.5f; // after Enable, how long to wait for the call to actually run
        private const float CallRetryBackoff = 2f;     // after a stalled attempt, wait this long before retrying

        /// <summary>
        /// A corpse's reaction delay has elapsed: begin the response. Either starts an interruptible phone call (civilian
        /// witness, default) or dispatches instantly (police witness / call window disabled). Respects only the
        /// "killer must also be seen" gate - there is deliberately no cross-corpse cooldown, so every body a witness
        /// sees gets its own response.
        /// </summary>
        internal static void TryStartResponse(CorpseRecord rec)
        {
            if (rec == null || rec.Dispatched || rec.Calling) return;

            float now = Time.time;
            Player killer = ResolveKiller(rec);

            if (killer == null)
            {
                // No culprit to pursue - nothing to investigate. Mark handled so we do not retry forever.
                Core.Log?.Msg($"[Reaction] corpse={rec.Id} seen, but nobody is to blame - no report.");
                rec.Dispatched = true;
                rec.DispatchedTime = now;
                return;
            }

            if (rec.Discoverer != null && !SafeConscious(rec.Discoverer))   // null: 'le find' forces a report without one
            {
                // The witness died, was knocked out or despawned before reacting: forget them so another NPC can find it.
                Core.Log?.Msg($"[Reaction] corpse={rec.Id}: witness {SafeName(rec.Discoverer)} is down before reacting - body re-armed.");
                rec.Discovered = false;
                rec.Discoverer = null;
                rec.WaitReason = null;
                return;
            }

            if (Preferences.RequirePlayerAlsoSeen && !DiscovererSees(rec.Discoverer, killer))
            {
                Wait(rec, "the witness does not see the killer yet");
                return;
            }

            Vector3 bodyPos = rec.Npc != null ? CorpseTracker.BodyPosition(rec.Npc) : Vector3.zero;

            // A civilian/employee witness visibly phones the police (interruptible 4s window) for EVERY body they see -
            // even if the player is already wanted, and even while panicking (a witness to a fresh murder panics AND
            // calls, exactly like vanilla). We do NOT pre-gate on the NPC's behaviour: the game decides if/when the call
            // runs, and UpdateCall reads the real outcome. We only avoid double-dialling. Police report it instantly.
            if (Preferences.WitnessCallsPolice && !IsPolice(rec.Discoverer))
            {
                if (now < rec.CallRetryAfter) { Wait(rec, "the witness was fighting, retry shortly"); return; }
                if (IsBusyCalling(rec.Discoverer)) { Wait(rec, "the witness is already on the phone"); return; }
                rec.WaitReason = null;
                if (StartCall(rec, killer, bodyPos, now)) return;
                Core.Log?.Warning($"[Reaction] corpse={rec.Id}: the witness cannot phone - police alerted at once instead.");
            }

            // Instant path: visible reaction + immediate on-scene dispatch (police witness / call window off / fallback).
            CitizenReaction.React(rec.Discoverer, bodyPos);
            DispatchToScene(rec, killer, bodyPos);
            rec.Dispatched = true;
            rec.DispatchedTime = now;
        }

        /// <summary>Log once per body why its response waits; the check repeats every scan tick.</summary>
        private static void Wait(CorpseRecord rec, string reason)
        {
            if (rec.WaitReason == reason) return;
            rec.WaitReason = reason;
            Core.Log?.Msg($"[Reaction] corpse={rec.Id} waits: {reason}.");
        }

        /// <summary>
        /// Begin the native phone call on the discovering witness. Sets the reported crime + target and enables the
        /// game's CallPoliceBehaviour (phone out, progress icon, ~4s). Returns false (so the caller falls back to an
        /// instant dispatch) if the witness has no usable call behaviour.
        /// </summary>
        private static bool StartCall(CorpseRecord rec, Player killer, Vector3 bodyPos, float now)
        {
            NPC witness = rec.Discoverer;
            if (witness == null)
            {
                Core.Log?.Warning($"[Reaction] corpse={rec.Id}: no witness to call.");
                return false;
            }
            try
            {
                NPCBehaviour beh = witness.Behaviour;
                CallPoliceBehaviour cpb = beh != null ? beh.CallPoliceBehaviour : null;
                if (cpb == null)
                {
                    Core.Log?.Warning($"[Reaction] witness {SafeName(witness)} has no CallPoliceBehaviour.");
                    return false;
                }

                CitizenReaction.Alert(witness);   // quick alerted gasp; the behaviour drives the phone + facing

                cpb.ReportedCrime = new DeadlyAssault();
                cpb.Target = killer;
                cpb.Enable_Networked();

                rec.Calling = true;
                rec.CallStartTime = now;
                rec.CallWasActive = false;
                rec.CallKiller = killer;
                rec.ScenePos = bodyPos;
                Core.Log?.Msg($"[Reaction] witness {SafeName(witness)} CALLING police on {SafeCode(killer)} for corpse={rec.Id} - {CallWindowSeconds:F0}s window.");
#if SNITCH
                Profiler.Log("LooseEnds", $"CALLING police on {SafeCode(killer)} for corpse={rec.Id} ({CallWindowSeconds:F0}s window).");
#endif
                return true;
            }
            catch (Exception e)
            {
                Core.Failed("reaction: start the witness call", e);
                return false;
            }
        }

        /// <summary>
        /// Advance an in-progress call each tick. "Connected" is detected from the BEHAVIOUR, not just elapsed time: the
        /// phone must have genuinely come out (CallWasActive) and the behaviour must have finalised (the native
        /// FinalizeCall disables it). This prevents a false connect when a hostile/busy NPC's higher-priority behaviour
        /// pre-empts the call so it never actually runs. Outcomes: the witness is downed -> SILENCED; the call genuinely
        /// finalised -> CONNECTED (redirect officers to the scene); the witness is occupied so the call never ran or was
        /// pre-empted -> hold the body at SEEN and retry once the NPC is free.
        /// </summary>
        internal static void UpdateCall(CorpseRecord rec, float now)
        {
            if (rec == null || !rec.Calling) return;
            float age = now - rec.CallStartTime;
            NPC witness = rec.Discoverer;
            CallPoliceBehaviour cpb = SafeCpb(witness);
            bool conscious = SafeConscious(witness);
            bool active = cpb != null && SafeActive(cpb);
            bool enabled = cpb != null && SafeEnabled(cpb);
            if (active) rec.CallWasActive = true;

            // Connected: the phone came out AND the call ran the full window AND finalised. The age gate is essential -
            // when the witness is killed/downed mid-call the death disables the behaviour too, and without the gate that
            // would read as a finalised call and falsely dispatch. Requiring age >= the call window means only a call
            // that actually lasted the ~4s (i.e. genuinely connected) counts.
            if (rec.CallWasActive && !enabled && age >= CallWindowSeconds)
            {
                try
                {
                    PlayerCrimeData cd = rec.CallKiller != null ? rec.CallKiller.CrimeData : null;
                    if (cd != null) cd.LastKnownPosition = rec.ScenePos;
                    else Core.Log?.Warning($"[Reaction] corpse={rec.Id}: suspect has no CrimeData - officers go to the suspect, not the scene.");
                }
                catch (Exception e) { Core.Failed("reaction: redirect officers to the scene", e); }
                rec.Calling = false;
                rec.Dispatched = true;
                rec.DispatchedTime = now;
                Core.Log?.Msg($"[Reaction] call CONNECTED for corpse={rec.Id} - officers sent to the scene ({rec.ScenePos.x:F1},{rec.ScenePos.y:F1},{rec.ScenePos.z:F1}).");
#if SNITCH
                Profiler.Log("LooseEnds", $"call CONNECTED for corpse={rec.Id} - officers sent to the scene.");
#endif
                return;
            }

            // Silenced: the witness was downed / removed before the call connected.
            if (!conscious)
            {
                CancelCall(witness);
                rec.Calling = false;
                rec.CallWasActive = false;
                rec.Discovered = false;     // re-arm: the scanner can re-discover this body
                rec.Discoverer = null;
                Core.Log?.Msg($"[Reaction] witness SILENCED before the call connected for corpse={rec.Id} - no police called.");
#if SNITCH
                Profiler.Log("LooseEnds", $"witness SILENCED for corpse={rec.Id} - no police called.");
#endif
                return;
            }

            // The call has not run yet (never activated, or activated then got pre-empted while still enabled). The only
            // state we treat as "cannot call, hold at SEEN" is the witness actively FIGHTING the player (combat genuinely
            // out-prioritises the phone) - we cancel and retry once combat ends. Every other occupied state (panicking /
            // cowering / momentarily busy) is left PENDING: the call stays enabled and connects once the witness settles,
            // exactly like a vanilla civilian who reports the murder after the initial panic.
            bool notRunningYet = (!rec.CallWasActive && age > CallActivationGrace) || (rec.CallWasActive && enabled && !active);
            if (notRunningYet && IsFighting(witness))
            {
                CancelCall(witness);
                rec.Calling = false;
                rec.CallWasActive = false;
                rec.CallRetryAfter = now + CallRetryBackoff;   // keep Discovered + Discoverer so it retries once combat ends
                Core.Log?.Msg($"[Reaction] witness is fighting for corpse={rec.Id} - holding at SEEN, calls once combat ends.");
                return;
            }

            // Safety net: stuck far past the window (e.g. the behaviour hung). Re-arm.
            if (age > CallTimeoutSeconds)
            {
                CancelCall(witness);
                rec.Calling = false;
                rec.CallWasActive = false;
                rec.Discovered = false;
                rec.Discoverer = null;
                Core.Log?.Warning($"[Reaction] call for corpse={rec.Id} did not resolve within {CallTimeoutSeconds:F0}s - re-armed.");
            }
            // else: within the activation grace, or actively ringing - wait.
        }

        /// <summary>Stop a call that is still ringing without it ever connecting (killer arrested, debug reset).</summary>
        internal static void AbortCall(CorpseRecord rec, string reason)
        {
            if (rec == null || !rec.Calling) return;
            CancelCall(rec.Discoverer);
            rec.Calling = false;
            rec.CallWasActive = false;
            Core.Log?.Msg($"[Reaction] call for corpse={rec.Id} aborted: {reason}.");
        }

        /// <summary>Hang up every ringing call (save or scene change), so no witness is left with the phone out.</summary>
        internal static void CancelAllCalls()
        {
            foreach (CorpseRecord rec in CorpseTracker.Records)
                if (rec.Calling) AbortCall(rec, "state reset");
        }

        private static void CancelCall(NPC witness)
        {
            if (witness == null) return;   // the witness object is gone, and its call with it
            try
            {
                CallPoliceBehaviour cpb = witness.Behaviour?.CallPoliceBehaviour;
                if (cpb != null && cpb.Enabled) cpb.Disable_Networked(null);   // disables an active OR queued/paused call
            }
            catch (Exception e) { Core.Caught("reaction: cancel the witness call", e); }
        }

        /// <summary>True if the witness is already running their call behaviour (busy dialling for another body).</summary>
        private static bool IsBusyCalling(NPC witness)
        {
            try
            {
                CallPoliceBehaviour cpb = witness?.Behaviour?.CallPoliceBehaviour;
                return cpb != null && cpb.Active;
            }
            catch (Exception e) { Core.Caught("reaction: read whether the witness is calling", e); return false; }
        }

        /// <summary>
        /// True only if the witness is actively FIGHTING (their active behaviour IS the CombatBehaviour). That is the
        /// one state where the phone genuinely cannot run, so we hold the body at SEEN and retry once combat ends.
        /// We deliberately do NOT treat panicking/cowering/fleeing as "cannot call" - a vanilla civilian calls the police
        /// while panicking, and blocking those is what made nobody call after a murder in a crowd.
        /// </summary>
        private static bool IsFighting(NPC witness)
        {
            try
            {
                NPCBehaviour beh = witness != null ? witness.Behaviour : null;
                if (beh == null) return false;
                var active = beh.activeBehaviour;
                if (active == null) return false;
                var combat = beh.CombatBehaviour;
                return combat != null && combat.GetInstanceID() == active.GetInstanceID();
            }
            catch (Exception e) { Core.Caught("reaction: read whether the witness is fighting", e); return false; }
        }

        private static CallPoliceBehaviour SafeCpb(NPC witness)
        {
            try { return witness != null && witness.Behaviour != null ? witness.Behaviour.CallPoliceBehaviour : null; }
            catch (Exception e) { Core.Caught("reaction: read CallPoliceBehaviour", e); return null; }
        }

        private static bool SafeActive(CallPoliceBehaviour cpb)
        {
            try { return cpb.Active; }
            catch (Exception e) { Core.Caught("reaction: read CallPoliceBehaviour.Active", e); return false; }
        }

        private static bool SafeEnabled(CallPoliceBehaviour cpb)
        {
            try { return cpb.Enabled; }
            catch (Exception e) { Core.Caught("reaction: read CallPoliceBehaviour.Enabled", e); return false; }
        }

        private static Player ResolveKiller(CorpseRecord rec)
        {
            if (rec.Killer != null) return rec.Killer;
            // Unknown killer (environmental / NPC-vs-NPC). In single-player, optionally blame the local player so there
            // is a suspect to drive the game's investigation; in co-op never blame a random player.
            if (Preferences.AttributeUnknownToLocalPlayer && !Net.IsCoop()) return Player.Local;
            return null;
        }

        /// <summary>
        /// Send the police to investigate the body's position. Mirror the game's murder-report sequence (pursuit level +
        /// crime + networked dispatch), then CRITICALLY overwrite the suspect's LastKnownPosition with the body position
        /// - PoliceCalled internally records the player's CURRENT position, so we overwrite it AFTER to redirect officers
        /// to the scene rather than chasing the player. Used for the instant path (police witness / call disabled).
        /// </summary>
        private static void DispatchToScene(CorpseRecord rec, Player killer, Vector3 bodyPos)
        {
            PlayerCrimeData crimeData = killer.CrimeData;
            if (crimeData == null)
            {
                Core.Log?.Warning($"[Reaction] SCENE corpse={rec.Id} - suspect has no CrimeData; skipping dispatch.");
                return;
            }

            PlayerCrimeData.EPursuitLevel level = (PlayerCrimeData.EPursuitLevel)Preferences.PursuitLevelInt;
            try
            {
                if ((int)crimeData.CurrentPursuitLevel < (int)level)
                    crimeData.SetPursuitLevel(level);
            }
            catch (Exception e) { Core.Failed("dispatch: SetPursuitLevel", e); }

            try { crimeData.AddCrime(new DeadlyAssault(), 1); }
            catch (Exception e) { Core.Failed("dispatch: AddCrime", e); }

            try
            {
                if (Singleton<LawManager>.InstanceExists)
                    Singleton<LawManager>.Instance.PoliceCalled(killer, new DeadlyAssault());
                else
                    Core.Log?.Warning($"[Reaction] SCENE corpse={rec.Id} - no LawManager in the scene; no officers dispatched.");
            }
            catch (Exception e) { Core.Failed("dispatch: LawManager.PoliceCalled", e); }

            try { crimeData.LastKnownPosition = bodyPos; }
            catch (Exception e) { Core.Failed("dispatch: redirect LastKnownPosition to the scene", e); }

            Core.Log?.Msg($"[Reaction] SCENE corpse={rec.Id} suspect={SafeCode(killer)} pursuit={(int)level} body=({bodyPos.x:F1},{bodyPos.y:F1},{bodyPos.z:F1})");
#if SNITCH
            Profiler.Log("LooseEnds", $"SCENE dispatch for corpse={rec.Id} suspect={SafeCode(killer)} pursuit={(int)level}.");
#endif
        }

        /// <summary>True if the witness can see the killer right now (their own vision cone, the game's player check).</summary>
        private static bool DiscovererSees(NPC discoverer, Player killer)
        {
            if (discoverer == null || killer == null) return false;
            try
            {
                NPCAwareness aw = discoverer.Awareness;
                VisionCone cone = aw != null ? aw.VisionCone : null;
                return cone != null && cone.IsPlayerVisible(killer);
            }
            catch (Exception e) { Core.Caught("reaction: VisionCone.IsPlayerVisible", e); return false; }
        }

        private static bool IsPolice(NPC npc)
        {
            try { return npc != null && npc.TryCast<PoliceOfficer>() != null; }
            catch (Exception e) { Core.Caught("reaction: cast witness to PoliceOfficer", e); return false; }
        }

        private static bool SafeConscious(NPC npc)
        {
            try { return npc != null && npc.IsConscious; }
            catch (Exception e) { Core.Caught("reaction: read witness IsConscious (a destroyed witness reads as silenced)", e); return false; }
        }

        /// <summary>"you" for the local player, otherwise the player name, for log lines.</summary>
        internal static string SafeCode(Player p)
        {
            if (p == null) return "nobody";
            try
            {
                if (Player.Local != null && Player.Local.GetInstanceID() == p.GetInstanceID()) return "you";
                string n = p.PlayerName;
                return string.IsNullOrEmpty(n) ? p.PlayerCode : n;
            }
            catch (Exception e) { Core.Caught("log: player name", e); return "?"; }
        }

        /// <summary>The NPC's full name for log lines.</summary>
        internal static string SafeName(NPC npc)
        {
            if (npc == null) return "nobody";
            try { return npc.FullName; }
            catch (Exception e) { Core.Caught("log: NPC name", e); return npc.GetInstanceID().ToString(); }
        }
    }
}
