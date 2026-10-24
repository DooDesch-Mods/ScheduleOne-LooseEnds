using System;
using System.Collections.Generic;
using LooseEnds.Detection;

namespace LooseEnds.Killer
{
    /// <summary>
    /// Detects that a player was arrested and settles that player's spree: their killer attribution is cleared and
    /// every body blamed on them is resolved, so one arrest does not lead straight into the next one for a body left
    /// lying around. Runs on the host for every player: vanilla's Arrest_Client is an observers RPC that also runs on
    /// the server and sets that player's CrimeData.MinsSinceLastArrested to 0, which otherwise only counts up.
    /// </summary>
    internal static class ArrestWatcher
    {
        // Last MinsSinceLastArrested per player instance id.
        private static readonly Dictionary<int, int> _lastMins = new Dictionary<int, int>();

        internal static void Tick()
        {
            var players = Player.PlayerList;
            if (players == null) return;
            int n = players.Count;
            for (int i = 0; i < n; i++)
            {
                Player p = players[i];
                if (p == null) continue;
                int id, mins;
                try
                {
                    PlayerCrimeData cd = p.CrimeData;
                    if (cd == null) continue;   // a joining player's CrimeData is set up a few frames later
                    id = p.GetInstanceID();
                    mins = cd.MinsSinceLastArrested;
                }
                catch (Exception e) { Core.Caught("arrest watcher: read player crime data", e); continue; }

                if (_lastMins.TryGetValue(id, out int prev) && mins < prev) OnArrested(p, id, prev, mins);
                _lastMins[id] = mins;
            }
        }

        private static void OnArrested(Player p, int id, int prevMins, int mins)
        {
            int attributions = KillerRegistry.ClearFor(p);
            int bodies = CorpseTracker.SettleFor(p);
            Core.Log?.Msg($"[Arrest] {Reaction.ReactionDispatcher.SafeCode(p)} (player id={id}, mins since arrest {prevMins}->{mins}) " +
                          $"arrested - settled {bodies} bodies, cleared {attributions} attributions.");
#if SNITCH
            Snitch.Api.Profiler.Log("LooseEnds", $"{Reaction.ReactionDispatcher.SafeCode(p)} arrested - settled {bodies} bodies.");
#endif
        }

        internal static void Clear() => _lastMins.Clear();
    }
}
