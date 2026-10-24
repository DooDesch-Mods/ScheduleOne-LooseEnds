using System;
using Il2CppFishNet;
using Il2CppFishNet.Managing;
using Il2CppScheduleOne.Networking;   // Lobby (PersistentSingleton<Lobby>)

namespace LooseEnds.Networking
{
    /// <summary>
    /// Two DISTINCT network signals, used for two different purposes (do not conflate them):
    ///
    ///   IsServer  - "am I the write authority?" via FishNet InstanceFinder.NetworkManager.IsServer. This is TRUE
    ///               even in single-player (the host runs a local FishNet server), which is exactly what we want:
    ///               the detection loop and all police/pursuit writes run only on the authority.
    ///
    ///   IsCoop    - "is this a real multi-human co-op session?" via the game's Lobby singleton
    ///               (Il2CppScheduleOne.Networking.Lobby : PersistentSingleton&lt;Lobby&gt;), IsInLobby && PlayerCount > 1.
    ///               This gates the EnableInMultiplayer setting. KNOWN GAP (same as Trashville): Lobby is
    ///               Steam-matchmaking-driven, so a direct-UDP dedicated server (no Steam lobby) reads as not-coop.
    ///
    /// All accessors are conservative: any failure returns the safe value (false) and is logged once.
    /// </summary>
    internal static class Net
    {
        /// <summary>True only on the write authority (the host/server). True in single-player.</summary>
        internal static bool IsServer
        {
            get
            {
                try
                {
                    NetworkManager nm = InstanceFinder.NetworkManager;
                    return nm != null && nm.IsServer;
                }
                catch (Exception e) { Core.Caught("net: read NetworkManager.IsServer", e); return false; }
            }
        }

        /// <summary>True only in a real Steam co-op lobby with more than the local player.</summary>
        internal static bool IsCoop()
        {
            try
            {
                Lobby lobby = PersistentSingleton<Lobby>.Instance;
                return lobby != null && lobby.IsInLobby && lobby.PlayerCount > 1;   // no lobby manager yet: single-player
            }
            catch (Exception e) { Core.Caught("net: read the Steam lobby", e); return false; }
        }
    }
}
