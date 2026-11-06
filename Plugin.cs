using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChineseCanFly.OfflineCompanionsToolkit
{
    /// <summary>
    /// Two things for Maddhattr's Offline Companions Reborn:
    ///   1. a filter for what companions may pick up (the mod itself has none), applied in every
    ///      job and at the scanning stage so they never walk to, or drag in, a blocked item;
    ///   2. a small on-screen monitor showing each of your companions as a coloured dot, with its
    ///      current job and distance, so a companion that fell out of the loaded area (frozen,
    ///      doing nothing) is visible instead of silently idle.
    /// </summary>
    [BepInPlugin(Guid, "Offline Companions Toolkit", "1.0.0")]
    [BepInDependency("Maddhattr.OfflineCompanionsReborn", BepInDependency.DependencyFlags.SoftDependency)]
    public class CompanionToolkitPlugin : BaseUnityPlugin
    {
        public const string Guid = "ChineseCanFly.OfflineCompanionsToolkit";

        internal static ManualLogSource Log;

        // filter
        internal static ConfigEntry<bool> FilterEnabled;
        internal static ConfigEntry<string> FilterMode;
        internal static ConfigEntry<string> FilterItems;
        internal static ConfigEntry<bool> Verbose;

        // monitor
        internal static ConfigEntry<bool> MonitorEnabled;
        internal static ConfigEntry<string> WatchList;
        internal static ConfigEntry<float> PosX;
        internal static ConfigEntry<float> PosY;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<float> Refresh;
        internal static ConfigEntry<bool> ShowDistance;
        internal static ConfigEntry<bool> ShowJob;
        internal static ConfigEntry<bool> ShowBackground;
        internal static ConfigEntry<bool> FrozenAsYellow;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> MonitorDebug;
        internal static ConfigEntry<bool> MonitorOnlyMine;
        internal static ConfigEntry<float> MonitorRescan;
        internal static ConfigEntry<float> LineExtra;
        internal static ConfigEntry<bool> ShowIdle;

        // ground sweep
        internal static ConfigEntry<bool> SweepEnabled;
        internal static ConfigEntry<float> SweepRadius;
        internal static ConfigEntry<float> SweepInterval;
        internal static ConfigEntry<int> SweepPerPass;

        private void Awake()
        {
            Log = Logger;

            FilterEnabled = Config.Bind("1 - General", "Enabled", true,
                "Companions only pick up what the list allows. Off = the filter does nothing at all.");
            Verbose = Config.Bind("1 - General", "Verbose", false,
                "Log every blocked item (chatty).");

            FilterMode = Config.Bind("2 - Filter", "Mode", "AllowList",
                "AllowList: the list is what companions may pick up, everything else is refused. " +
                "DenyList: the list is what they must not pick up, everything else is allowed.");
            FilterItems = Config.Bind("2 - Filter", "Items", "Trophy*, *Spear*",
                "One list, comma separated, * and ? wildcards. A leading - makes an exception. " +
                "Default: every creature trophy plus thrown spears.");

            MonitorEnabled = Config.Bind("3 - Monitor", "Enabled", true,
                "Draw the companion monitor.");
            WatchList = Config.Bind("3 - Monitor", "Watch List", "",
                "Which companions to show, comma separated, * and ? wildcards. " +
                "EMPTY = every companion of yours. Changes apply on the next refresh, no restart.");
            PosX = Config.Bind("3 - Monitor", "X (%)", 72f,
                new ConfigDescription("Left edge of the panel, percent of screen width.",
                    new AcceptableValueRange<float>(0f, 100f)));
            PosY = Config.Bind("3 - Monitor", "Y (%)", 5f,
                new ConfigDescription("Top edge of the panel, percent of screen height.",
                    new AcceptableValueRange<float>(0f, 100f)));
            FontSize = Config.Bind("3 - Monitor", "Font Size", 15,
                new ConfigDescription("Text height in pixels.",
                    new AcceptableValueRange<int>(8, 40)));
            ShowDistance = Config.Bind("3 - Monitor", "Show Distance", true,
                "Show how far the companion is from you (from its last saved position).");
            ShowJob = Config.Bind("3 - Monitor", "Show Job", true,
                "Show what the companion is doing right now.");
            ShowBackground = Config.Bind("3 - Monitor", "Show Background", true,
                "Draw a dark plate behind the text.");
            FrozenAsYellow = Config.Bind("3 - Monitor", "Frozen As Yellow", true,
                "On: loaded but frozen = yellow, unloaded = red (three states). " +
                "Off: anything not running normally is red (two states).");
            Refresh = Config.Bind("3 - Monitor", "Refresh Seconds", 1f,
                new ConfigDescription("How often the states are re-checked.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            ToggleKey = Config.Bind("3 - Monitor", "Toggle Hotkey", new KeyboardShortcut(KeyCode.F7),
                "Show or hide the panel while playing.");
            MonitorDebug = Config.Bind("3 - Monitor", "Debug", false,
                "Draw a placeholder line even when no companion was found, and log the first draw.");
            MonitorOnlyMine = Config.Bind("3 - Monitor", "Only My Companions", true,
                "Hide companions that belong to another player (ownership is the owner id stored in the ZDO).");
            ShowIdle = Config.Bind("3 - Monitor", "Show Idle Time", true,
                "Append 'moving' or how long the companion has not moved. A green dot with a long " +
                "idle time means its area is running but it has nothing to do.");
            LineExtra = Config.Bind("3 - Monitor", "Line Spacing", 4f,
                new ConfigDescription("Extra pixels between rows (negative tightens it).",
                    new AcceptableValueRange<float>(-4f, 20f)));
            MonitorRescan = Config.Bind("3 - Monitor", "Rescan Minutes", 5f,
                new ConfigDescription(
                    "How often the whole ZDO table is searched for companions again. The list is also " +
                    "updated live, so this only picks up companions that were never loaded while you watched.",
                    new AcceptableValueRange<float>(1f, 60f)));

            SweepEnabled = Config.Bind("4 - Sweep", "Enabled", true,
                "Companions also pick up allowed items lying on the ground around them. " +
                "This covers meat you throw down and drops from kills nobody looted - the mod's own " +
                "auto-pickup ignores everything with auto-pickup switched off, which is what a " +
                "dropped item has.");
            SweepRadius = Config.Bind("4 - Sweep", "Radius", 6f,
                new ConfigDescription("How far around a companion it looks for ground items, in metres.",
                    new AcceptableValueRange<float>(1f, 20f)));
            SweepInterval = Config.Bind("4 - Sweep", "Interval Seconds", 1.5f,
                new ConfigDescription("Seconds between sweeps.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            SweepPerPass = Config.Bind("4 - Sweep", "Max Per Pass", 4,
                new ConfigDescription("At most this many items per companion per sweep, so a pile does not cause a spike.",
                    new AcceptableValueRange<int>(1, 20)));

            PatchFilter();
        }

        private void Update()
        {
            try
            {
                if (ToggleKey.Value.IsDown()) Monitor.Visible = !Monitor.Visible;
                Monitor.Tick();
                GroundSweep.Tick();
            }
            catch (Exception ex)
            {
                Log.LogWarning("monitor tick failed: " + ex.Message);
            }
        }

        private void OnGUI()
        {
            try { Monitor.Draw(); }
            catch (Exception ex) { Log.LogWarning("monitor draw failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ filter patches

        private void PatchFilter()
        {
            try
            {
                var harmony = new Harmony(Guid);

                var pickup = AccessTools.Method(typeof(Humanoid), "Pickup",
                    new[] { typeof(GameObject), typeof(bool), typeof(bool) });
                if (pickup != null)
                    harmony.Patch(pickup, prefix: new HarmonyMethod(typeof(FilterPatches)
                        .GetMethod(nameof(FilterPatches.PickupPrefix), BindingFlags.Static | BindingFlags.NonPublic)));

                var canAdd = AccessTools.Method(typeof(Inventory), "CanAddItem",
                    new[] { typeof(ItemDrop.ItemData), typeof(int) });
                if (canAdd != null)
                    harmony.Patch(canAdd, prefix: new HarmonyMethod(typeof(FilterPatches)
                        .GetMethod(nameof(FilterPatches.CanAddItemPrefix), BindingFlags.Static | BindingFlags.NonPublic)));

                PatchScan(harmony, "CompanionAI", "AutoPickup", new[] { typeof(float) });
                PatchScan(harmony, "MHR_Loot", "NextDrop", null);

                Logger.LogInfo("Offline Companions Toolkit v1.0.0 loaded (filter + monitor + sweep).");
            }
            catch (Exception ex)
            {
                Logger.LogError("Companion Toolkit failed to initialise: " + ex);
            }
        }

        private void PatchScan(Harmony harmony, string typeName, string methodName, Type[] args)
        {
            var type = FindType(typeName);
            if (type == null)
            {
                Logger.LogWarning($"{typeName} not found - scan layer off, veto still works.");
                return;
            }

            var method = args == null ? AccessTools.Method(type, methodName) : AccessTools.Method(type, methodName, args);
            if (method == null)
            {
                Logger.LogWarning($"{typeName}.{methodName} not found - scan layer off, veto still works.");
                return;
            }

            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(FilterPatches).GetMethod(nameof(FilterPatches.ScanPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(FilterPatches).GetMethod(nameof(FilterPatches.ScanFinalizer), BindingFlags.Static | BindingFlags.NonPublic)));
            Logger.LogInfo($"scan layer armed: {typeName}.{methodName}");
        }

        internal static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch (Exception) { continue; }
                if (types == null) continue;
                foreach (var t in types)
                    if (t != null && t.Name == name) return t;
            }
            return null;
        }
    }

    internal static class FilterPatches
    {
        private static readonly Dictionary<int, bool> Companion = new Dictionary<int, bool>();
        private static readonly List<string> Positives = new List<string>();
        private static readonly List<string> Exceptions = new List<string>();
        private static string _raw;
        private static bool _reportedError;
        private static int _depth;

        internal static bool AllowListMode =>
            CompanionToolkitPlugin.FilterMode == null ||
            !string.Equals(CompanionToolkitPlugin.FilterMode.Value, "DenyList", StringComparison.OrdinalIgnoreCase);

        internal static bool Allowed(string name)
        {
            Sync();

            for (int i = 0; i < Exceptions.Count; i++)
                if (Match(Exceptions[i], name)) return !AllowListMode;

            bool hit = false;
            for (int i = 0; i < Positives.Count; i++)
                if (Match(Positives[i], name)) { hit = true; break; }

            return AllowListMode ? hit : !hit;
        }

        internal static bool Match(string pattern, string name)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(name)) return false;
            int p = 0, n = 0, star = -1, mark = 0;
            while (n < name.Length)
            {
                if (p < pattern.Length &&
                    (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(name[n])))
                { p++; n++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = n; }
                else if (star >= 0) { p = star + 1; n = ++mark; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static void Sync()
        {
            string raw = CompanionToolkitPlugin.FilterItems?.Value ?? string.Empty;
            if (string.Equals(raw, _raw, StringComparison.Ordinal)) return;
            _raw = raw;

            Positives.Clear();
            Exceptions.Clear();
            foreach (var part in raw.Split(','))
            {
                var token = part.Trim();
                if (token.Length == 0) continue;
                if (token[0] == '-')
                {
                    var bare = token.Substring(1).Trim();
                    if (bare.Length > 0) Exceptions.Add(bare);
                }
                else Positives.Add(token);
            }
        }

        private static string NameOf(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            try
            {
                var prefab = item.m_dropPrefab;
                if (prefab != null) return prefab.name;
            }
            catch (Exception) { }
            return item.m_shared != null ? item.m_shared.m_name : null;
        }

        internal static bool PickupPrefix(Humanoid __instance, GameObject go)
        {
            try
            {
                if (!CompanionToolkitPlugin.FilterEnabled.Value) return true;
                if (__instance == null || go == null) return true;
                if (!IsCompanion(__instance)) return true;

                var drop = go.GetComponent<ItemDrop>();
                string name = drop != null ? NameOf(drop.m_itemData) : null;
                if (string.IsNullOrEmpty(name))
                    name = go.name.Replace("(Clone)", string.Empty).Trim();
                if (string.IsNullOrEmpty(name)) return true;

                bool allowed = Allowed(name);
                if (!allowed && CompanionToolkitPlugin.Verbose.Value)
                    CompanionToolkitPlugin.Log.LogInfo("[filter] blocked pickup " + name);
                return allowed;
            }
            catch (Exception ex) { WarnOnce(ex); return true; }
        }

        internal static bool CanAddItemPrefix(ItemDrop.ItemData item, ref bool __result)
        {
            try
            {
                if (_depth <= 0) return true;
                if (!CompanionToolkitPlugin.FilterEnabled.Value) return true;

                string name = NameOf(item);
                if (string.IsNullOrEmpty(name)) return true;
                if (Allowed(name)) return true;

                __result = false;
                if (CompanionToolkitPlugin.Verbose.Value)
                    CompanionToolkitPlugin.Log.LogInfo("[filter] skip " + name);
                return false;
            }
            catch (Exception ex) { WarnOnce(ex); return true; }
        }

        internal static void ScanPrefix() { _depth++; }

        internal static Exception ScanFinalizer(Exception __exception)
        {
            if (_depth > 0) _depth--;
            return __exception;
        }

        internal static bool IsCompanion(Humanoid humanoid)
        {
            int id = humanoid.GetInstanceID();
            if (Companion.TryGetValue(id, out var known)) return known;

            bool result = false;
            var all = humanoid.GetComponents<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;
                var type = c.GetType().Name;
                if (type == "CompanionAI" || type == "CompanionSetup") { result = true; break; }
            }

            if (Companion.Count > 4096) Companion.Clear();
            Companion[id] = result;
            return result;
        }

        private static void WarnOnce(Exception ex)
        {
            if (_reportedError) return;
            _reportedError = true;
            CompanionToolkitPlugin.Log.LogWarning("[filter] skipped one item: " + ex.Message);
        }
    }
}
