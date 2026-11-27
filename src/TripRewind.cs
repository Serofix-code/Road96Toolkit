using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Keeps a copy of the save every time the game saves during a trip, labelled with the scenario,
    // so you can go back to any earlier point of the trip and try something else.
    internal static class TripRewind
    {
        public class Info
        {
            public string Scenario { get; set; } = "";
            public string Npc { get; set; } = "";
            public int Money { get; set; }
            public int Days { get; set; }
            public int Step { get; set; }
            public DateTime When { get; set; }
        }

        class Point { public string Dir; public Info I; }
        class Trip { public string Dir, Name; public List<Point> Points = new List<Point>(); }

        static string Root => Path.Combine(SavesTab.Root, "rewind");
        static string CurrentFile => Path.Combine(Root, "current.txt");

        static string _key, _tripDir;
        static DateTime _lastWrite;
        static byte[] _lastBytes;
        static float _nextPoll, _menuSince = -1f;
        static string _pending;          // point to put back once the main menu shows
        static List<Trip> _trips = new List<Trip>();
        static int _trip, _page;
        static Point _sel;
        static bool _scanned;

        public static void Update()
        {
            if (_pending != null)
            {
                if (SavesTab.AtMenu)
                {
                    if (_menuSince < 0) _menuSince = Time.unscaledTime;
                    // give the game a moment to finish its own save on the way out
                    if (Time.unscaledTime - _menuSince > 1.5f)
                    {
                        var p = _pending;
                        _pending = null;
                        _menuSince = -1f;
                        SavesTab.RestoreDir(p, "Back to that point. Press Continue");
                    }
                }
                else _menuSince = -1f;
            }

            if (!Plugin.TripRewind.Value || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 3f;
            var ctx = Game.Context;
            if (ctx == null || SavesTab.AtMenu) return;
            bool started = false;
            try { started = ctx._runStarted; } catch { }
            if (!started) return;
            try
            {
                var save = SavesTab.GameSavePath;
                if (!File.Exists(save)) return;
                var w = File.GetLastWriteTimeUtc(save);
                if (_lastWrite == default) { _lastWrite = w; }
                else if (w == _lastWrite) return;
                _lastWrite = w;
                var bytes = SavesTab.ReadShared(save);
                if (_lastBytes != null && bytes.AsSpan().SequenceEqual(_lastBytes)) return;
                _lastBytes = bytes;
                Keep(ctx, bytes);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Trip rewind: " + e.Message); }
        }

        static string TripDir(NB.NarrativeContext ctx)
        {
            var key = RunFacts.KeyOf(ctx);
            if (key == _key && _tripDir != null && Directory.Exists(_tripDir)) return _tripDir;
            try
            {
                if (File.Exists(CurrentFile))
                {
                    var l = File.ReadAllLines(CurrentFile);
                    if (l.Length >= 2 && l[0] == key && Directory.Exists(l[1])) { _key = key; _tripDir = l[1]; return _tripDir; }
                }
            }
            catch { }
            int trip = 0;
            try { trip = ctx.CurrentRuns; } catch { }
            _key = key;
            _tripDir = Path.Combine(Root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + " trip " + trip);
            Directory.CreateDirectory(_tripDir);
            File.WriteAllLines(CurrentFile, new[] { key, _tripDir });
            TrimTrips();
            return _tripDir;
        }

        static void Keep(NB.NarrativeContext ctx, byte[] bytes)
        {
            var dir = TripDir(ctx);
            var info = new Info { When = DateTime.Now };
            Game.Try("Rewind info", () =>
            {
                info.Money = ctx._currentMoney;
                info.Days = ctx._daysRemaining;
                var b = ctx.CurrentRun?._bubbles;
                if (b != null && b.Count > 0)
                {
                    info.Step = b.Count;
                    info.Scenario = b[b.Count - 1]?._bubbleId ?? "";
                }
                var el = Game.Brain?._narrativeInput?.GetAllPossibleInputs()?.FirstOrDefault(e => e != null && e.ID == info.Scenario);
                if (el != null) info.Npc = el.NPC ?? "";
            });
            var point = Path.Combine(dir, DateTime.Now.ToString("HH-mm-ss") + " " + Safe(info.Scenario));
            Directory.CreateDirectory(point);
            File.WriteAllBytes(Path.Combine(point, "context.sav"), bytes);
            File.WriteAllText(Path.Combine(point, "info.json"), JsonSerializer.Serialize(info));
            // keep the trip from growing forever
            var all = Directory.GetDirectories(dir).OrderBy(d => d).ToList();
            foreach (var d in all.Take(Math.Max(0, all.Count - 80))) try { Directory.Delete(d, true); } catch { }
            Plugin.Log.LogInfo("Rewind point: " + info.Scenario);
            _scanned = false;
        }

        static string Safe(string s) => new string((s ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

        static void TrimTrips()
        {
            try
            {
                var keep = Math.Max(1, Plugin.RewindTrips.Value);
                foreach (var d in Directory.GetDirectories(Root).OrderByDescending(d => d).Skip(keep))
                    Directory.Delete(d, true);
            }
            catch { }
        }

        static void Scan()
        {
            _scanned = true;
            _trips = new List<Trip>();
            if (!Directory.Exists(Root)) return;
            foreach (var d in Directory.GetDirectories(Root).OrderByDescending(d => d))
            {
                var t = new Trip { Dir = d, Name = Path.GetFileName(d).Replace('_', ' ') };
                foreach (var p in Directory.GetDirectories(d).OrderByDescending(x => x))
                {
                    if (!File.Exists(Path.Combine(p, "context.sav"))) continue;
                    Info i = null;
                    try { i = JsonSerializer.Deserialize<Info>(File.ReadAllText(Path.Combine(p, "info.json"))); } catch { }
                    t.Points.Add(new Point { Dir = p, I = i ?? new Info { When = Directory.GetLastWriteTime(p) } });
                }
                if (t.Points.Count > 0) _trips.Add(t);
            }
            if (_trip >= _trips.Count) _trip = 0;
        }

        static string Line(Point p) =>
            $"{p.I.When:HH:mm}   step {p.I.Step}   {(p.I.Scenario.Length > 0 ? p.I.Scenario : "?")}{(p.I.Npc.Length > 0 ? "  (" + p.I.Npc + ")" : "")}   ${p.I.Money}   {p.I.Days} days left";

        public static void Draw()
        {
            if (!_scanned) Scan();
            Gui.Label("Every time the game saves during a trip, a rewind point is kept. Pick one to go back there.");
            if (Gui.Toggle($"Make rewind points (keeps the last {Plugin.RewindTrips.Value} trips)", Plugin.TripRewind.Value))
                Plugin.TripRewind.Value = !Plugin.TripRewind.Value;

            if (_trips.Count == 0) { Gui.Label("No rewind points yet. They show up as you play a trip."); return; }
            var t = _trips[_trip];
            var r = Gui.Row(3f, 1f);
            if (GUI.Button(r[0], $"Trip: {t.Name}   ({_trip + 1} of {_trips.Count}, click for the next)")) { _trip = (_trip + 1) % _trips.Count; _page = 0; _sel = null; }
            if (GUI.Button(r[1], "Refresh")) Scan();

            int pick = Gui.List(t.Points, ref _page, Line, 11, p => p == _sel);
            if (pick >= 0) _sel = t.Points[pick];

            if (_pending != null)
            {
                Gui.Label("Waiting for the main menu: open the pause menu and go back to the menu.");
                if (Gui.Button("Cancel")) _pending = null;
                return;
            }
            if (_sel == null) { Gui.Label("Pick a point to go back to it."); return; }
            Gui.Space(4);
            Gui.Label(Line(_sel));
            r = Gui.Row(2);
            bool menu = SavesTab.AtMenu;
            if (GUI.Button(r[0], menu ? "Go back here now" : "Go back here"))
            {
                if (menu) SavesTab.RestoreDir(_sel.Dir, "Back to that point. Press Continue");
                else
                {
                    _pending = _sel.Dir;
                    ToolkitBehaviour.Toast("Now go back to the main menu from the pause menu");
                }
            }
            if (GUI.Button(r[1], "Keep it as a snapshot"))
                Game.Try("Keep point", () =>
                {
                    var dest = Path.Combine(SavesTab.Root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + " rewind " + Safe(_sel.I.Scenario));
                    Directory.CreateDirectory(dest);
                    File.Copy(Path.Combine(_sel.Dir, "context.sav"), Path.Combine(dest, "context.sav"));
                    File.WriteAllText(Path.Combine(dest, "name.txt"), "Rewind " + _sel.I.Scenario);
                    SavesTab.Rescan();
                    ToolkitBehaviour.Toast("Kept in your snapshots");
                });
            Gui.Label(menu
                ? "At the main menu, it goes back right away."
                : "In a trip the game would save over it, so the toolkit waits until you are at the main menu, then puts it back.");
        }
    }
}
