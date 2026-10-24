using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.GameTime;        // TimeManager
using Il2CppScheduleOne.Law;             // LawManager, DeadlyAssault
using Il2CppScheduleOne.Map;             // PoliceStation
using Il2CppScheduleOne.NPCs.Behaviour;  // FootPatrolRoute, PatrolGroup
using Il2CppScheduleOne.Police;          // PoliceOfficer
using Il2CppScheduleOne.Vision;          // VisionCone
using LooseEnds.Detection;
using UnityEngine.AI;

namespace LooseEnds.Reaction
{
    /// <summary>
    /// A report without a description of the killer: officers walk to the body as a foot patrol and circle it for a
    /// while. Nobody is marked wanted. A player an officer sees close to the body becomes the suspect (Investigating,
    /// the officer starts the pursuit); the killer is also charged with the murder, anyone else is only questioned.
    /// Each further report on the same in-game day sends one officer more, up to <see cref="MaxOfficers"/>. When every
    /// officer of the station is out (a pursuit, a patrol), the report waits up to <see cref="WaitForOfficerSeconds"/>
    /// for one to come back. Host only.
    /// </summary>
    internal static class SceneSearch
    {
        private const float SearchSeconds = 120f;     // circling time, counted from the first officer's arrival
        private const float TravelSeconds = 240f;     // give up when nobody reaches the scene in this time
        private const float ArriveRadius = 10f;
        private const float SuspectRadius = 20f;
        private const float WaypointRadius = 6f;
        private const int WaypointCount = 4;
        private const int MaxOfficers = 3;
        private const float TickSeconds = 0.25f;
        private const float WaitForOfficerSeconds = 120f;
        private const float RetrySeconds = 5f;

        private sealed class Search
        {
            public int CorpseId;
            public Player Killer;
            public Vector3 Center;
            public float StartTime;
            public float ArriveTime = -1f;
            public GameObject Root;
            public PatrolGroup Group;
            public int Officers;
        }

        private sealed class Waiting
        {
            public CorpseRecord Rec;
            public Vector3 Center;
            public int Officers;
            public float Since;
            public float NextTry;
        }

        private static readonly List<Search> _active = new List<Search>();
        private static readonly List<Waiting> _waiting = new List<Waiting>();
        private static int _reportsDay = int.MinValue;
        private static int _reportsToday;
        private static float _tickAccum;

        internal static int ActiveCount => _active.Count;
        internal static int WaitingCount => _waiting.Count;
        internal static int ReportsToday => _reportsToday;

        /// <summary>
        /// Counts one connected report for today and returns how many officers it sends: 1 for the first body of the
        /// day, one more for each further body, at most <see cref="MaxOfficers"/>.
        /// </summary>
        internal static int CountReport()
        {
            int day = CurrentDay();
            if (day != _reportsDay)
            {
                _reportsDay = day;
                _reportsToday = 0;
            }
            _reportsToday++;
            return Math.Min(MaxOfficers, _reportsToday);
        }

        internal static void Start(CorpseRecord rec, Vector3 center, int officers)
        {
            if (TryStart(rec, center, officers)) return;
            float now = Time.time;
            _waiting.Add(new Waiting { Rec = rec, Center = center, Officers = officers, Since = now, NextTry = now + RetrySeconds });
            Core.Log?.Msg($"[Search] corpse={rec.Id}: every officer is busy - the report waits up to {WaitForOfficerSeconds:F0}s for one.");
        }

        /// <summary>Free officers at the station nearest the point, or -1 when there is no station.</summary>
        internal static int FreeOfficers(Vector3 point)
        {
#if DEBUG
            if (LooseEnds.Config.DevOverrides.ForceStationBusy) return 0;
#endif
            try
            {
                PoliceStation station = PoliceStation.GetClosestPoliceStation(point);
                if (station == null || station.OfficerPool == null) return -1;
                return station.OfficerPool.Count;
            }
            catch (Exception e) { Core.Caught("search: read the station's officer pool", e); return -1; }
        }

        /// <summary>Sends the patrol now. False when no officer is free; a missing LawManager or station also logs why.</summary>
        private static bool TryStart(CorpseRecord rec, Vector3 center, int officers)
        {
            if (!Singleton<LawManager>.InstanceExists)
            {
                Core.Log?.Warning($"[Search] corpse={rec.Id}: no LawManager in the scene - nobody searches the scene.");
                return true;
            }
            int free = FreeOfficers(center);
            if (free < 0)
            {
                Core.Log?.Warning($"[Search] corpse={rec.Id}: no police station in the scene - nobody searches the scene.");
                return true;
            }
            if (free == 0) return false;

            GameObject root = null;
            try
            {
                root = new GameObject($"LooseEnds_SceneSearch_{rec.Id}");
                root.transform.position = center;
                var points = new Il2CppReferenceArray<Transform>(WaypointCount);
                for (int i = 0; i < WaypointCount; i++)
                {
                    float a = i * Mathf.PI * 2f / WaypointCount;
                    Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * WaypointRadius;
                    if (NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas)) p = hit.position;
                    else if (NavMesh.SamplePosition(center, out NavMeshHit near, 4f, NavMesh.AllAreas)) p = near.position;
                    var wp = new GameObject($"wp{i}");
                    wp.transform.SetParent(root.transform, false);
                    wp.transform.position = p;
                    points[i] = wp.transform;
                }
                FootPatrolRoute route = root.AddComponent<FootPatrolRoute>();
                route.RouteName = $"Loose Ends scene {rec.Id}";
                route.Waypoints = points;
                route.StartWaypointIndex = 0;

                PatrolGroup group = Singleton<LawManager>.Instance.StartFootpatrol(route, officers);
                if (group == null || group.Members == null || group.Members.Count == 0)
                {
                    UnityEngine.Object.Destroy(root);
                    Core.Log?.Warning($"[Search] corpse={rec.Id}: the station had {free} free officer(s), but none joined the patrol.");
                    return false;
                }

                _active.Add(new Search
                {
                    CorpseId = rec.Id,
                    Killer = rec.Killer,
                    Center = center,
                    StartTime = Time.time,
                    Root = root,
                    Group = group,
                    Officers = group.Members.Count,
                });
                Core.Log?.Msg($"[Search] corpse={rec.Id}: {group.Members.Count} of {officers} officer(s) walk to the scene " +
                              $"({center.x:F1},{center.y:F1},{center.z:F1}) - nobody is wanted yet.");
                return true;
            }
            catch (Exception e)
            {
                Core.Failed("search: send officers to the scene", e);
                if (root != null) UnityEngine.Object.Destroy(root);
                return true;
            }
        }

        private static void TickWaiting(float now)
        {
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                Waiting w = _waiting[i];
                if (w.Rec.Settled)
                {
                    _waiting.RemoveAt(i);
                    Core.Log?.Msg($"[Search] corpse={w.Rec.Id}: the waiting report was settled by an arrest.");
                    continue;
                }
                if (now < w.NextTry) continue;
                if (TryStart(w.Rec, w.Center, w.Officers))
                {
                    _waiting.RemoveAt(i);
                    continue;
                }
                if (now - w.Since >= WaitForOfficerSeconds)
                {
                    _waiting.RemoveAt(i);
                    Core.Log?.Warning($"[Search] corpse={w.Rec.Id}: no officer came free within {WaitForOfficerSeconds:F0}s - nobody searches the scene.");
                    continue;
                }
                w.NextTry = now + RetrySeconds;
            }
        }

        /// <summary>Per-frame driver, throttled: arrival, suspects near the body, end of the search.</summary>
        internal static void Tick(float dt)
        {
            if (_active.Count == 0 && _waiting.Count == 0) return;
            _tickAccum += dt;
            if (_tickAccum < TickSeconds) return;
            _tickAccum = 0f;

            float now = Time.time;
            if (_waiting.Count > 0) TickWaiting(now);
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Search s = _active[i];
                List<PoliceOfficer> officers = Officers(s);
                if (officers.Count == 0) { End(i, "every officer left the search"); continue; }

                if (s.ArriveTime < 0f)
                {
                    if (AnyWithin(officers, s.Center, ArriveRadius))
                    {
                        s.ArriveTime = now;
                        Core.Log?.Msg($"[Search] corpse={s.CorpseId}: officers reached the scene after {now - s.StartTime:F0}s, " +
                                      $"searching for {SearchSeconds:F0}s.");
                    }
                    else if (now - s.StartTime > TravelSeconds)
                    {
                        End(i, $"no officer reached the scene within {TravelSeconds:F0}s");
                        continue;
                    }
                }
                else if (now - s.ArriveTime > SearchSeconds)
                {
                    End(i, "time is up, nobody seen near the body");
                    continue;
                }

                if (TryFindSuspect(s, officers, out PoliceOfficer officer, out Player suspect))
                {
                    End(i, $"{ReactionDispatcher.SafeName(officer)} saw {ReactionDispatcher.SafeCode(suspect)} near the body");
                    Suspect(s, officer, suspect);
                }
            }
        }

        private static List<PoliceOfficer> Officers(Search s)
        {
            var list = new List<PoliceOfficer>();
            try
            {
                var members = s.Group.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    NPC m = members[i];
                    PoliceOfficer o = m != null ? m.TryCast<PoliceOfficer>() : null;
                    if (o != null && o.IsConscious) list.Add(o);
                }
            }
            catch (Exception e) { Core.Caught("search: read the patrol members", e); }
            return list;
        }

        private static bool AnyWithin(List<PoliceOfficer> officers, Vector3 center, float radius)
        {
            float r2 = radius * radius;
            foreach (PoliceOfficer o in officers)
            {
                try { if ((o.transform.position - center).sqrMagnitude <= r2) return true; }
                catch (Exception e) { Core.Caught("search: read officer position", e); }
            }
            return false;
        }

        private static bool TryFindSuspect(Search s, List<PoliceOfficer> officers, out PoliceOfficer officer, out Player suspect)
        {
            officer = null;
            suspect = null;
            var players = Player.PlayerList;
            if (players == null) return false;
            float r2 = SuspectRadius * SuspectRadius;
            for (int p = 0; p < players.Count; p++)
            {
                Player pl = players[p];
                try
                {
                    if (pl == null || pl.IsArrested || pl.Health == null || !pl.Health.IsAlive) continue;
                    if (pl.CrimeData == null || pl.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None) continue;
                    if ((pl.transform.position - s.Center).sqrMagnitude > r2) continue;
                    foreach (PoliceOfficer o in officers)
                    {
                        VisionCone cone = o.Awareness != null ? o.Awareness.VisionCone : null;
                        if (cone != null && cone.IsPlayerVisible(pl))
                        {
                            officer = o;
                            suspect = pl;
                            return true;
                        }
                    }
                }
                catch (Exception e) { Core.Caught("search: look for a suspect near the body", e); }
            }
            return false;
        }

        private static void Suspect(Search s, PoliceOfficer officer, Player suspect)
        {
            bool isKiller = s.Killer != null && s.Killer.GetInstanceID() == suspect.GetInstanceID();
            try
            {
                PlayerCrimeData cd = suspect.CrimeData;
                cd.RecordLastKnownPosition(true);
                cd.SetPursuitLevel(PlayerCrimeData.EPursuitLevel.Investigating);
                if (isKiller) cd.AddCrime(new DeadlyAssault(), 1);
                officer.BeginFootPursuit_Networked(suspect.PlayerCode, true);
                Core.Log?.Msg($"[Search] corpse={s.CorpseId}: {ReactionDispatcher.SafeCode(suspect)} is the suspect - " +
                              $"Investigating{(isKiller ? ", charged with the murder" : ", not the killer: questioned only")}.");
            }
            catch (Exception e) { Core.Failed("search: make the player near the body the suspect", e); }
        }

        private static void End(int index, string reason)
        {
            Search s = _active[index];
            _active.RemoveAt(index);
            try { s.Group?.DisbandGroup(); }
            catch (Exception e) { Core.Caught("search: disband the patrol", e); }
            try { if (s.Root != null) UnityEngine.Object.Destroy(s.Root); }
            catch (Exception e) { Core.Caught("search: remove the waypoints", e); }
            Core.Log?.Msg($"[Search] corpse={s.CorpseId}: search over - {reason}.");
        }

        /// <summary>Stop every search (save or scene change).</summary>
        internal static void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--) End(i, "state reset");
            _waiting.Clear();
            _reportsDay = int.MinValue;
            _reportsToday = 0;
        }

        internal static int CurrentDay()
        {
            try { return NetworkSingleton<TimeManager>.InstanceExists ? NetworkSingleton<TimeManager>.Instance.ElapsedDays : 0; }
            catch (Exception e) { Core.Caught("search: read the in-game day", e); return 0; }
        }
    }
}
