using MelonLoader;
using UnityEngine;

namespace LooseEnds.Config
{
    /// <summary>
    /// MelonPreferences wrapper (category "LooseEnds_01_Main"). The police response is a single behaviour: officers
    /// are sent to investigate the SCENE (the body), and the game's own Investigating behaviour drives the search,
    /// escalation and timeout from there. Values are clamped on read, so a hand-edited cfg can never leave the range.
    /// Test and debug tools are dev console commands (Debug/DevConsole.cs), not settings.
    /// </summary>
    internal static class Preferences
    {
        private const string CategoryId = "LooseEnds_01_Main";

        private static MelonPreferences_Category _category;

        // ----- master -----
        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<bool> _enableInMp;

        // ----- detection -----
        private static MelonPreferences_Entry<bool> _reactCitizens;
        private static MelonPreferences_Entry<bool> _reactPolice;
        private static MelonPreferences_Entry<bool> _reactEmployees;
        private static MelonPreferences_Entry<bool> _requireLos;
        private static MelonPreferences_Entry<float> _bodySightRange;
        private static MelonPreferences_Entry<float> _noticeRadius;
        private static MelonPreferences_Entry<float> _reactionDelay;
        private static MelonPreferences_Entry<bool> _reactToKnockedOut;

        // ----- response -----
        private static MelonPreferences_Entry<bool> _witnessCallsPolice;
        private static MelonPreferences_Entry<bool> _requirePlayerSeen;
        private static MelonPreferences_Entry<int> _pursuitLevel;
        private static MelonPreferences_Entry<bool> _oncePerCorpse;
        private static MelonPreferences_Entry<float> _responseCooldown;
        private static MelonPreferences_Entry<bool> _attributeUnknownToLocal;

        // ----- weight -----
        private static MelonPreferences_Entry<float> _weightMultiplier;

        internal static void Initialize()
        {
            if (_category != null) return;

            _category = MelonPreferences.CreateCategory(CategoryId, "Loose Ends");

            // ----- master -----
            _enabled = Create("Enabled", true, "Enabled",
                "Master switch. ON: NPCs who see a dead body react and dragged bodies are heavier. OFF: vanilla.");
            _enableInMp = Create("EnableInMultiplayer", true, "Enable in co-op",
                "Witnesses also react in a co-op lobby. The host runs detection for everyone; only the host's value counts.");

            // ----- detection -----
            _reactCitizens = Create("ReactCitizens", true, "Citizens react",
                "Civilian NPCs who see a body call the police.");
            _reactPolice = Create("ReactPolice", true, "Police react",
                "Police officers who see a body report it at once.");
            _reactEmployees = Create("ReactEmployees", false, "Employees react",
                "Your own employees call the police on a body they see.");
            _requireLos = Create("RequireLineOfSight", true, "Require line of sight",
                "ON: walls, dumpsters, buildings, water and cover hide a body. OFF: anyone within 'Body sight range' " +
                "notices it, through walls too.");
            _bodySightRange = Create("BodySightRange", 12f, "Body sight range (m)",
                "How far an NPC notices a body lying in their field of view. 4-30.",
                new MelonLoader.Preferences.ValueRange<float>(4f, 30f));
            _noticeRadius = Create("NoticeRadius", 6f, "Close-range notice radius (m)",
                "An NPC this close notices a body in any direction, not only in front of them. Cover still hides it. 2-15.",
                new MelonLoader.Preferences.ValueRange<float>(2f, 15f));
            _reactionDelay = Create("ReactionDelaySeconds", 3f, "Reaction delay (s)",
                "Time between seeing a body and reacting to it. 0-30.",
                new MelonLoader.Preferences.ValueRange<float>(0f, 30f));
            _reactToKnockedOut = Create("ReactToKnockedOut", false, "React to unconscious NPCs",
                "Witnesses also react to knocked-out NPCs, not only to dead ones.");

            // ----- response -----
            _witnessCallsPolice = Create("WitnessCallsPolice", true, "Witness phones the police",
                "ON: a civilian witness calls the police over about 4 seconds; knock them out or kill them before the " +
                "call connects to stop it. OFF: the police are alerted at once. Police officers always report at once.");
            _requirePlayerSeen = Create("RequirePlayerAlsoSeen", false, "Killer must also be seen",
                "ON: a witness reports only once they also see the killer. OFF: a witness who sees only the body " +
                "reports it, and officers search the scene.");
            _pursuitLevel = Create("PursuitLevel", 1, "Pursuit level (1-4)",
                "Police status an instant report (police witness, or phone call off) gives a killer the witness saw: " +
                "1 Investigating, 2 Under arrest, 3 Wanted, 4 Wanted dead. A phoned report always gives Investigating.",
                new MelonLoader.Preferences.ValueRange<int>(1, 4));
            _oncePerCorpse = Create("OncePerCorpse", true, "Report each body once",
                "ON: a body is reported at most once. OFF: it can be reported again after the cooldown below.");
            _responseCooldown = Create("ResponseCooldownSeconds", 60f, "Report cooldown (s)",
                "With 'Report each body once' OFF: time before the same body can be reported again. 5-600.",
                new MelonLoader.Preferences.ValueRange<float>(5f, 600f));
            _attributeUnknownToLocal = Create("AttributeUnknownToLocalPlayer", true, "Blame you when the killer is unknown",
                "Single-player only: a body nobody killed (lightning, a fight between NPCs) is blamed on you.");

            // ----- weight -----
            _weightMultiplier = Create("CorpseWeightMultiplier", 5f, "Dragged body weight x",
                "How hard a dragged body (dead or knocked out) resists being pulled and thrown. 1 = vanilla. " +
                "Your own speed does not change. 1-20.",
                new MelonLoader.Preferences.ValueRange<float>(1f, 20f));

            Core.Log?.Msg($"[Config] Loaded: Enabled={Enabled}, EnableInMultiplayer={EnableInMultiplayer}, " +
                          $"LineOfSight={RequireLineOfSight}, BodySight={BodySightRange:F0}m, Notice={NoticeRadius:F0}m, " +
                          $"Call={WitnessCallsPolice}, Pursuit={PursuitLevelInt}, Weight={CorpseWeightMultiplier:F1}x");
        }

        private static MelonPreferences_Entry<T> Create<T>(string id, T def, string name, string desc = null,
            MelonLoader.Preferences.ValueValidator validator = null)
        {
            return validator == null
                ? _category.CreateEntry(id, def, name, desc)
                : _category.CreateEntry(id, def, name, desc, false, false, validator);
        }

        // ----- accessors -----

        internal static bool Enabled => _enabled?.Value ?? true;
        internal static bool EnableInMultiplayer
        {
            get
            {
#if DEBUG
                if (DevOverrides.ForceEnableInMultiplayer) return true;
#endif
                return _enableInMp?.Value ?? false;
            }
        }

        internal static bool ReactCitizens => _reactCitizens?.Value ?? true;
        internal static bool ReactPolice => _reactPolice?.Value ?? true;
        internal static bool ReactEmployees => _reactEmployees?.Value ?? false;
        internal static bool RequireLineOfSight => _requireLos?.Value ?? true;
        internal static float BodySightRange => Mathf.Clamp(_bodySightRange?.Value ?? 12f, 4f, 30f);
        internal static float NoticeRadius => Mathf.Clamp(_noticeRadius?.Value ?? 6f, 2f, 15f);
        internal static float ReactionDelaySeconds => Mathf.Clamp(_reactionDelay?.Value ?? 3f, 0f, 30f);
        internal static bool ReactToKnockedOut => _reactToKnockedOut?.Value ?? false;

        internal static bool WitnessCallsPolice => _witnessCallsPolice?.Value ?? true;
        internal static bool RequirePlayerAlsoSeen => _requirePlayerSeen?.Value ?? false;
        /// <summary>Configured pursuit level as a raw int (1-4). The dispatcher casts it to the game enum.</summary>
        internal static int PursuitLevelInt
        {
            get
            {
#if DEBUG
                if (DevOverrides.ForcePursuitLevel > 0) return Mathf.Clamp(DevOverrides.ForcePursuitLevel, 1, 4);
#endif
                return Mathf.Clamp(_pursuitLevel?.Value ?? 1, 1, 4);
            }
        }
        internal static bool OncePerCorpse => _oncePerCorpse?.Value ?? true;
        internal static float ResponseCooldownSeconds => Mathf.Clamp(_responseCooldown?.Value ?? 60f, 5f, 600f);
        internal static bool AttributeUnknownToLocalPlayer => _attributeUnknownToLocal?.Value ?? true;

        internal static float CorpseWeightMultiplier => Mathf.Clamp(_weightMultiplier?.Value ?? 5f, 1f, 20f);
    }

#if DEBUG
    /// <summary>Runtime-only dev switches set from the dev console ('le scanlog', 'le pursuit', 'le mp', 'le unseen', 'le busy'). Never saved.</summary>
    internal static class DevOverrides
    {
        internal static bool LogWitnessScan;
        internal static int ForcePursuitLevel;
        internal static bool ForceEnableInMultiplayer;
        internal static bool ForceKillerUnseen;   // every witness reports only the body (scene search)
        internal static bool ForceStationBusy;    // the station reports no free officer (scene search waits)
    }
#endif
}
