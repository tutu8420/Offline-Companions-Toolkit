using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace ChineseCanFly.OfflineCompanionsToolkit
{
    /// <summary>
    /// Coloured companion list: green = running, yellow = loaded but frozen, red = its part of
    /// the world is unloaded (nothing it does counts until you come back).
    ///
    /// Cost model: one full pass over the ZDO table when the world loads (and every Rescan
    /// Minutes after that), then only lookups by the ids we already know plus a cheap scan of the
    /// live character list. Two earlier versions walked the whole table every second - never again.
    /// </summary>
    internal static class Monitor
    {
        internal static bool Visible = true;

        private sealed class Entry
        {
            internal ZDOID Id;
            internal string Name = "?";
            internal Vector3 Pos;
            internal int Mode;
            internal float Distance;
            internal int State;     // 0 green, 1 yellow, 2 red
            internal int Shown;
            internal int Pending;
            internal float Since;
            internal float LastSeen;
            internal float LastMove;
        }

        private static readonly Dictionary<ZDOID, Entry> Entries = new Dictionary<ZDOID, Entry>();
        private static readonly List<Entry> Rows = new List<Entry>();

        private static string _prefab;
        private static int _prefabHash;
        private static bool _scanned;
        private static float _nextRescan;
        private static float _nextLearn;
        private static float _nextTick;
        private static bool _loggedDraw;
        private static bool _loggedLearn;
        private static bool _loggedEmpty;
        private static bool _loggedOwner;
        private static FieldInfo _objectsField;
        private static bool _objectsFieldFailed;

        private static GUIStyle _style;
        private static int _styleSize = -1;
        private static Texture2D _plate;

        internal static void Tick()
        {
            if (!CompanionToolkitPlugin.MonitorEnabled.Value) return;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + Mathf.Max(0.25f, CompanionToolkitPlugin.Refresh.Value);

            var player = Player.m_localPlayer;
            if (player == null) { Rows.Clear(); return; }

            LearnPrefab();

            if (!_scanned)
            {
                _scanned = true;
                FullScan(player);
                _nextRescan = Time.time + Mathf.Max(1f, CompanionToolkitPlugin.MonitorRescan.Value) * 60f;
            }
            else if (Time.time >= _nextRescan)
            {
                _nextRescan = Time.time + Mathf.Max(1f, CompanionToolkitPlugin.MonitorRescan.Value) * 60f;
                FullScan(player);
            }

            LiveAdd(player);
            RefreshRows(player);
        }

        // ------------------------------------------------------------ prefab

        private static void LearnPrefab()
        {
            if (!string.IsNullOrEmpty(_prefab)) return;
            if (Time.time < _nextLearn) return;
            _nextLearn = Time.time + 10f;

            var chars = Character.GetAllCharacters();
            if (chars == null) return;
            for (int i = 0; i < chars.Count; i++)
            {
                var hum = chars[i] as Humanoid;
                if (hum == null || hum.IsDead() || !FilterPatches.IsCompanion(hum)) continue;

                var view = hum.GetComponent<ZNetView>();
                var zdo = view != null ? view.GetZDO() : null;
                if (zdo == null) continue;

                _prefabHash = zdo.GetPrefab();
                var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(_prefabHash) : null;
                _prefab = go != null ? go.name : "?";

                if (!_loggedLearn)
                {
                    _loggedLearn = true;
                    CompanionToolkitPlugin.Log.LogInfo($"[monitor] companion prefab: {_prefab} (hash {_prefabHash})");
                }
                return;
            }
        }

        // ------------------------------------------------------------ scanning

        private static void FullScan(Player player)
        {
            if (_prefabHash == 0) return;

            var watch = Stopwatch.StartNew();
            var found = new List<ZDO>(8);

            try
            {
                var man = ZDOMan.instance;
                if (man == null) return;

                if (_objectsField == null)
                {
                    var type = CompanionToolkitPlugin.FindType("ZDOMan");
                    _objectsField = type?.GetField("m_objectsByID",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (_objectsField == null)
                    {
                        _objectsFieldFailed = true;
                        CompanionToolkitPlugin.Log.LogWarning("[monitor] ZDOMan.m_objectsByID not found - live companions only.");
                    }
                }

                if (!_objectsFieldFailed)
                {
                    var dict = _objectsField.GetValue(man) as Dictionary<ZDOID, ZDO>;
                    if (dict != null)
                    {
                        foreach (var zdo in dict.Values)
                            if (zdo != null && zdo.GetPrefab() == _prefabHash) found.Add(zdo);
                    }
                }
            }
            catch (Exception ex)
            {
                CompanionToolkitPlugin.Log.LogWarning("[monitor] scan failed: " + ex.Message);
            }

            watch.Stop();
            if (watch.Elapsed.TotalMilliseconds > 50.0)
                CompanionToolkitPlugin.Log.LogWarning(
                    $"[monitor] full scan took {watch.Elapsed.TotalMilliseconds:0} ms ({found.Count} matched)");

            Rebuild(player, found);
        }

        private static void LiveAdd(Player player)
        {
            var chars = Character.GetAllCharacters();
            if (chars == null) return;

            for (int i = 0; i < chars.Count; i++)
            {
                var hum = chars[i] as Humanoid;
                if (hum == null || hum.IsDead() || !FilterPatches.IsCompanion(hum)) continue;

                var view = hum.GetComponent<ZNetView>();
                var zdo = view != null ? view.GetZDO() : null;
                if (zdo == null) continue;
                if (!Entries.ContainsKey(zdo.m_uid)) Apply(player, zdo, null);
            }
        }

        private static void Rebuild(Player player, List<ZDO> found)
        {
            if (found.Count == 0)
            {
                var live = new List<ZDO>();
                var chars = Character.GetAllCharacters();
                if (chars != null)
                    for (int i = 0; i < chars.Count; i++)
                    {
                        var hum = chars[i] as Humanoid;
                        if (hum == null || hum.IsDead() || !FilterPatches.IsCompanion(hum)) continue;
                        var view = hum.GetComponent<ZNetView>();
                        var zdo = view != null ? view.GetZDO() : null;
                        if (zdo != null) live.Add(zdo);
                    }
                found = live;
            }

            int kept = 0;
            for (int i = 0; i < found.Count; i++)
                if (Apply(player, found[i], null)) kept++;

            if (!_loggedEmpty && kept == 0)
            {
                _loggedEmpty = true;
                CompanionToolkitPlugin.Log.LogWarning(
                    $"[monitor] {found.Count} companion ZDOs found, none kept (prefab {_prefab}, " +
                    $"only mine = {CompanionToolkitPlugin.MonitorOnlyMine.Value}).");
            }
            else if (kept > 0)
            {
                CompanionToolkitPlugin.Log.LogInfo($"[monitor] companions: {kept} tracked");
            }
        }

        /// <summary>Turn one ZDO into an entry. Returns false when it is not one of our companions.</summary>
        private static bool Apply(Player player, ZDO zdo, HashSet<ZDOID> seen)
        {
            if (zdo == null || !zdo.IsValid()) return false;

            string owner = zdo.GetString("HC_Owner", string.Empty);
            if (!OwnerMatches(player, owner)) return false;

            string name = zdo.GetString("HC_Name", string.Empty);
            if (string.IsNullOrEmpty(name)) name = zdo.GetString("tamedName", string.Empty);
            if (string.IsNullOrEmpty(name))
            {
                var inst = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                var ch = inst != null ? inst.GetComponent<Character>() : null;
                if (ch != null) name = ch.m_name;
            }
            if (string.IsNullOrEmpty(name)) name = "伙伴" + (zdo.m_uid.GetHashCode() & 0xFFFF).ToString("X4");

            var id = zdo.m_uid;
            if (seen != null) seen.Add(id);

            if (!Entries.TryGetValue(id, out var entry))
            {
                int state = StateOf(zdo);
                entry = new Entry
                {
                    Id = id, Name = name, Shown = state, Pending = state,
                    LastSeen = Time.time, LastMove = Time.time
                };
                Entries[id] = entry;
            }

            entry.Name = name;
            entry.Pos = zdo.GetPosition();
            entry.Mode = zdo.GetInt("HC_ActionMode", 0);
            entry.LastSeen = Time.time;
            return true;
        }

        private static bool OwnerMatches(Player player, string owner)
        {
            if (string.IsNullOrEmpty(owner)) return true;   // no owner recorded - show it

            string name = player.GetPlayerName();
            if (string.Equals(owner, name, StringComparison.OrdinalIgnoreCase)) return true;

            try
            {
                string id = player.GetPlayerID().ToString();
                if (string.Equals(owner, id, StringComparison.OrdinalIgnoreCase)) return true;
                if (owner.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch (Exception) { }

            if (!_loggedOwner)
            {
                _loggedOwner = true;
                CompanionToolkitPlugin.Log.LogInfo(
                    $"[monitor] a companion is owned by '{owner}' (you are '{name}'). " +
                    "Set Only My Companions = false to list companions that belong to someone else.");
            }
            return !CompanionToolkitPlugin.MonitorOnlyMine.Value;
        }

        // ------------------------------------------------------------ refresh

        private static void RefreshRows(Player player)
        {
            Rows.Clear();
            if (Entries.Count == 0) return;

            var man = ZDOMan.instance;
            Vector3 me = player.transform.position;
            var watch = WatchPatterns();

            foreach (var pair in Entries)
            {
                var entry = pair.Value;
                if (!Watched(watch, entry.Name)) continue;

                var zdo = man != null ? man.GetZDO(entry.Id) : null;
                int state;
                if (zdo == null) state = 2;
                else
                {
                    var pos = zdo.GetPosition();
                    if ((pos - entry.Pos).sqrMagnitude > 0.0625f) entry.LastMove = Time.time;   // moved > 0.25 m
                    entry.Pos = pos;
                    entry.Mode = zdo.GetInt("HC_ActionMode", 0);
                    state = StateOf(zdo);
                }

                if (entry.Pending != state) { entry.Pending = state; entry.Since = Time.time; }
                else if (entry.Shown != state && Time.time - entry.Since >= 2f) entry.Shown = state;

                entry.State = entry.Shown;
                entry.Distance = Vector3.Distance(me, entry.Pos);
                Rows.Add(entry);
            }

            Rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static int StateOf(ZDO zdo)
        {
            var view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (view == null) return 2;
            if (!view.gameObject.activeInHierarchy) return CompanionToolkitPlugin.FrozenAsYellow.Value ? 1 : 2;
            return 0;
        }

        private static List<string> WatchPatterns()
        {
            var list = new List<string>();
            string raw = CompanionToolkitPlugin.WatchList?.Value ?? string.Empty;
            foreach (var part in raw.Split(','))
            {
                var token = part.Trim();
                if (token.Length > 0) list.Add(token);
            }
            return list;
        }

        private static bool Watched(List<string> patterns, string name)
        {
            if (patterns.Count == 0) return true;
            for (int i = 0; i < patterns.Count; i++)
                if (FilterPatches.Match(patterns[i], name)) return true;
            return false;
        }

        private static string JobName(int mode)
        {
            switch (mode)
            {
                case 0: return "跟随";
                case 1: return "伐木";
                case 2: return "采石";
                case 3: return "挖矿";
                case 4: return "留守";
                case 5: return "采集";
                case 6: return "冶炼";
                case 7: return "打猎";
                case 8: return "耕种";
                case 9: return "钓鱼";
                case 15: return "修缮";
                case 16: return "补给";
                case 17: return "烹饪";
                case 18: return "农田";
                default: return "工作" + mode;
            }
        }

        // ------------------------------------------------------------ drawing

        internal static void Draw()
        {
            if (!CompanionToolkitPlugin.MonitorEnabled.Value || !Visible) return;

            if (!_loggedDraw)
            {
                _loggedDraw = true;
                CompanionToolkitPlugin.Log.LogInfo("[monitor] OnGUI is running.");
            }

            var style = Style();
            if (Rows.Count == 0)
            {
                if (!CompanionToolkitPlugin.MonitorDebug.Value) return;
                float px = Screen.width * (CompanionToolkitPlugin.PosX.Value / 100f);
                float py = Screen.height * (CompanionToolkitPlugin.PosY.Value / 100f);
                GUI.Label(new Rect(px, py, 400f, style.fontSize + 6f), "monitor: no companions found", style);
                return;
            }

            float line = Mathf.Max(style.lineHeight, style.fontSize * 1.35f)
                         + CompanionToolkitPlugin.LineExtra.Value;
            float width = 300f * Mathf.Max(0.8f, style.fontSize / 15f);
            float x = Screen.width * (CompanionToolkitPlugin.PosX.Value / 100f);
            float y = Screen.height * (CompanionToolkitPlugin.PosY.Value / 100f);

            if (CompanionToolkitPlugin.ShowBackground.Value && _plate != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(x - 8f, y - 6f, width + 16f, line * Rows.Count + 12f), _plate);
                GUI.color = Color.white;
            }

            for (int i = 0; i < Rows.Count; i++)
                GUI.Label(new Rect(x, y + i * line, width, line), TextFor(Rows[i]), style);
        }

        private static string TextFor(Entry row)
        {
            switch (row.State)
            {
                case 2:
                    return "<color=#FF4D4D>\u25CF</color> " + row.Name;
                case 1:
                    return "<color=#FFD21A>\u25CF</color> " + row.Name + Suffix(row);
                default:
                    return "<color=#4CD964>\u25CF</color> " + row.Name + Suffix(row);
            }
        }

        private static string Suffix(Entry row)
        {
            string text = string.Empty;
            if (CompanionToolkitPlugin.ShowJob.Value) text += "  " + JobName(row.Mode);
            if (CompanionToolkitPlugin.ShowDistance.Value) text += "  " + row.Distance.ToString("0") + " 米";
            if (CompanionToolkitPlugin.ShowIdle.Value) text += "  " + Idle(row);
            return text;
        }

        /// <summary>Moving / how long it has stood still. Reading a position needs no game internals.</summary>
        private static string Idle(Entry row)
        {
            float idle = Time.time - row.LastMove;
            if (idle < 2f) return "移动中";
            if (idle < 60f) return "静止 " + idle.ToString("0") + " 秒";
            if (idle < 3600f) return "静止 " + (idle / 60f).ToString("0") + " 分";
            return "静止 " + (idle / 3600f).ToString("0.#") + " 小时";
        }

        private static GUIStyle Style()
        {
            int size = CompanionToolkitPlugin.FontSize.Value;
            if (_style != null && _styleSize == size) return _style;

            _styleSize = size;
            _style = new GUIStyle(GUI.skin != null ? GUI.skin.label : new GUIStyle())
            {
                fontSize = size,
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft
            };
            _style.normal.textColor = Color.white;

            try
            {
                var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, size);
                if (font != null) _style.font = font;
            }
            catch (Exception) { }

            if (_plate == null)
            {
                _plate = new Texture2D(1, 1);
                _plate.SetPixel(0, 0, Color.white);
                _plate.Apply();
            }

            return _style;
        }
    }
}
