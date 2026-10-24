using System;
using Il2CppScheduleOne.Employees;    // Employee
using Il2CppScheduleOne.Police;       // PoliceOfficer
using Il2CppScheduleOne.Vision;       // VisionCone, EntityVisibility
using LooseEnds.Config;
#if SNITCH
using Snitch.Api;                 // Profiler sub-section timing + ablation flag (Debug + EnableSnitch only)
#endif

namespace LooseEnds.Detection
{
    /// <summary>
    /// The witness test: for each undiscovered corpse, find a living NPC that can SEE it. The default path reuses the
    /// game's own per-NPC vision cone (VisionCone.IsPointWithinSight), which honors field-of-view and the occlusion
    /// layers - so a body behind a wall / in a dumpster / indoors / underwater is not seen. Cost is bounded by a
    /// distance pre-cull and a per-scan check cap (round-robin across observers), so it never spikes a frame.
    /// </summary>
    internal static class SightingScanner
    {
        // At most this many sight tests per scan; the rest continue next scan from where this one stopped.
        private const int MaxChecksPerScan = 64;
        // Added to the larger of the two notice ranges for the distance pre-cull: the eye sits ~1.6m above the
        // feet, so an NPC whose eye is within range can stand a little farther away than that range.
        private const float CullMargin = 2f;
        // The body point the sight tests aim at sits this far above the ragdoll root, roughly torso height.
        private const float BodyAimHeight = 0.5f;
        private const float EyeHeight = 1.6f;

        // Minimum fraction of the body's visibility points that must be clear for a close-range notice. The game's
        // EntityVisibility treats "VisionObscurer" cover (bushes / props) as PARTIAL - it still returns a small nonzero
        // exposure - so a bare > 0 would notice a body fully tucked behind a dumpster. A threshold keeps "hide the body"
        // intact: a body must be meaningfully exposed, not just a sliver through cover.
        private const float NoticeExposureThreshold = 0.4f;

        private static int _observerCursor;

        // Sight-test count of the last scan pass, for the optional Snitch counter.
        internal static int LastChecks;
#if SNITCH
        // Snitch ablation lever 'looseends.scan': when set, Scan() returns immediately so the profiler can measure
        // the causal cost of the whole sighting pipeline (incl. the engine's native vision raycasts). Debug only.
        internal static bool ScanDisabled;
#endif

        /// <summary>Runs one throttled scan. Any corpse seen this pass is latched (Discovered) with its discoverer.</summary>
        internal static void Scan()
        {
#if SNITCH
            if (ScanDisabled) return;   // ablation lever 'looseends.scan': skip the sighting pipeline
#endif
            var reg = NPCManager.NPCRegistry;
            if (reg == null) return;
            int regCount;
            try { regCount = reg.Count; }
            catch (Exception e) { Core.Caught("scan: NPC registry count", e); return; }
            if (regCount == 0) return;

            bool requireLos = Preferences.RequireLineOfSight;
            float sightRange = Preferences.BodySightRange;
            float noticeRadius = Preferences.NoticeRadius;
            float cull = Mathf.Max(sightRange, noticeRadius) + CullMargin;
            float cullSqr = cull * cull;
            int checksUsed = 0;
#if DEBUG
            int corpsesScanned = 0;
            int found = 0;
#endif

            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (rec.Discovered) continue;
                NPC corpse = rec.Npc;
                if (corpse == null) continue;

                Vector3 bodyPos;
                try { bodyPos = CorpseTracker.BodyPosition(corpse) + Vector3.up * BodyAimHeight; }
                catch (Exception e) { Core.Caught("scan: corpse position", e); continue; }
#if DEBUG
                corpsesScanned++;
                float diagMinSqr = float.MaxValue;
                bool diagMinSight = false;
#endif

                for (int k = 0; k < regCount; k++)
                {
                    if (checksUsed >= MaxChecksPerScan)
                    {
                        AdvanceCursor(checksUsed, regCount);
                        return;   // cap reached - resume next scan from where we left off
                    }

                    int idx = (_observerCursor + k) % regCount;
                    NPC obs = reg[idx];
                    if (obs == null) continue;
                    if (obs.GetInstanceID() == rec.Id) continue;

                    // observer must be alive, an enabled role, and within the pre-cull radius
                    Vector3 obsPos;
                    try
                    {
                        NPCHealth oh = obs.Health;
                        if (oh == null || oh.IsDead || oh.IsKnockedOut) continue;
                        if (!RoleEnabled(obs)) continue;
                        obsPos = obs.transform.position;
                    }
                    catch (Exception e) { Core.Caught("scan: read observer", e); continue; }
                    float dsq = (obsPos - bodyPos).sqrMagnitude;
                    if (dsq > cullSqr) continue;

                    checksUsed++;
#if SNITCH
                    bool seen;
                    using (Profiler.Sample("LooseEnds.Scan.Sight"))
                        seen = CanSee(obs, corpse, bodyPos, requireLos, sightRange, noticeRadius);
#else
                    bool seen = CanSee(obs, corpse, bodyPos, requireLos, sightRange, noticeRadius);
#endif
#if DEBUG
                    if (dsq < diagMinSqr) { diagMinSqr = dsq; diagMinSight = seen; }
#endif
                    if (!seen) continue;

                    rec.Discovered = true;
                    rec.FirstSeenTime = Time.time;
                    rec.Discoverer = obs;
                    rec.KillerSeen = Reaction.ReactionDispatcher.WitnessSeesKiller(obs, rec.Killer);
#if DEBUG
                    found++;
#endif
                    Core.Log?.Msg($"[Witness] corpse {rec.Id} SEEN by {Reaction.ReactionDispatcher.SafeName(obs)} " +
                                  $"({Mathf.Sqrt(dsq):F1}m, killer={Reaction.ReactionDispatcher.SafeCode(rec.Killer)}, " +
                                  $"killer seen={rec.KillerSeen}).");
                    break;   // stop scanning observers for this corpse
                }
#if DEBUG
                if (DevOverrides.LogWitnessScan && !rec.Discovered)
                    Core.LogDebug($"[Witness] corpse {rec.Id} pos=({bodyPos.x:F1},{bodyPos.y:F1},{bodyPos.z:F1}) nearestTestedObs={(diagMinSqr < float.MaxValue ? Mathf.Sqrt(diagMinSqr).ToString("F1") : "none")}m sight={diagMinSight}");
#endif
            }

            AdvanceCursor(checksUsed, regCount);
            LastChecks = checksUsed;
#if DEBUG
            if (DevOverrides.LogWitnessScan)
                Core.LogDebug($"[Witness] scan: corpses={CorpseTracker.Count} undiscovered-scanned={corpsesScanned} checks={checksUsed} found={found}");
#endif
        }

        private static void AdvanceCursor(int checksUsed, int regCount)
        {
            if (regCount <= 0) return;
            _observerCursor = (_observerCursor + Mathf.Max(1, checksUsed)) % regCount;
        }

        private static bool CanSee(NPC observer, NPC corpse, Vector3 bodyPos, bool requireLos, float sightRange, float noticeRadius)
        {
            VisionCone cone = null;
            try { NPCAwareness aw = observer.Awareness; cone = aw != null ? aw.VisionCone : null; }
            catch (Exception e) { Core.Caught("sight: observer vision cone", e); }

            Vector3 eye = EyeOf(observer, cone);
            float dist = (eye - bodyPos).magnitude;

            // Radius-only mode: anyone within the body sight range (or the close-range radius) notices, walls or not.
            if (!requireLos) return dist <= Mathf.Max(sightRange, noticeRadius);

            // 1) The NPC's own vision cone (FOV + occlusion), but only within a believable body-notice range. A body
            //    lying flat on the ground is far less conspicuous than a standing person, so the cone test is capped
            //    well below the NPC's full standing sight range.
            if (dist <= sightRange && cone != null)
            {
                try { if (cone.IsPointWithinSight(bodyPos, false, null)) return true; }
                catch (Exception e) { Core.Caught("sight: VisionCone.IsPointWithinSight", e); }
            }

            // 2) Close-range notice. NPCs never look down at their own feet, so a body lying flat right next to them is
            //    below the forward vision cone - which is why cops walk straight over a corpse. If a living NPC is within
            //    the notice radius with a clear line of sight, they notice it. Occlusion is delegated to the game's own
            //    visibility solver (EntityVisibility), so a body in a dumpster / behind a wall stays hidden.
            return dist <= noticeRadius && BodyExposedTo(corpse, observer, eye, noticeRadius, bodyPos, cone);
        }

        /// <summary>
        /// True if the corpse has a clear enough line of sight to the observer's eye at close range. Prefers the game's
        /// own <see cref="EntityVisibility.CalculateExposureToPoint"/> (correct self + observer exclusion, partial-cover
        /// handling) and falls back to a manual occlusion ray if the body has no visibility component.
        /// </summary>
        private static bool BodyExposedTo(NPC corpse, NPC observer, Vector3 eye, float range, Vector3 bodyPos, VisionCone cone)
        {
            try
            {
                EntityVisibility vis = corpse.Visibility;
                if (vis != null)
                {
                    // checkRange is measured from the body's transform to the eye; pad it so the ragdoll-vs-root offset
                    // never trips the helper's distance early-out for a body that is genuinely within the notice radius.
                    float exposure = vis.CalculateExposureToPoint(eye, range + 3f, observer);
                    return exposure >= NoticeExposureThreshold;
                }
            }
            catch (Exception e) { Core.Caught("sight: EntityVisibility.CalculateExposureToPoint (falls back to a ray)", e); }
            return HasClearPath(eye, bodyPos, cone);
        }

        private static Vector3 EyeOf(NPC observer, VisionCone cone)
        {
            try { if (cone != null && cone.VisionOrigin != null) return cone.VisionOrigin.position; }
            catch (Exception e) { Core.Caught("sight: VisionCone.VisionOrigin", e); }
            return observer.transform.position + Vector3.up * EyeHeight;
        }

        /// <summary>
        /// True if no occluder blocks the segment eye-&gt;body. The body's own collider at the far end is excluded by
        /// stopping the cast a little short, so a body is never its own blocker. If no occluder layer mask is available
        /// this returns true (proximity-only) - better to notice a body at your feet than to miss it.
        /// </summary>
        private static bool HasClearPath(Vector3 eye, Vector3 bodyPos, VisionCone cone)
        {
            Vector3 dir = bodyPos - eye;
            float dist = dir.magnitude;
            if (dist <= 0.6f) return true;   // basically on top of it
            dir /= dist;
            float castDist = dist - 0.5f;    // stop short so the body itself is not counted as the blocker
            try
            {
                if (cone != null)
                {
                    LayerMask mask = cone.VisibilityBlockingLayers;
                    if (mask.value != 0)
                        return !Physics.Raycast(eye, dir, castDist, mask.value);
                }
            }
            catch (Exception e) { Core.Caught("sight: occlusion raycast", e); }
            return true;
        }

        private static bool RoleEnabled(NPC obs)
        {
            if (obs.TryCast<PoliceOfficer>() != null) return Preferences.ReactPolice;
            if (obs.TryCast<Employee>() != null) return Preferences.ReactEmployees;
            return Preferences.ReactCitizens;
        }
    }
}
