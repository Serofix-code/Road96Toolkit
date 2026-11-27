using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using UnityEngine;

namespace Road96Toolkit
{
    // Save manager. Road 96 keeps one trip in Saves/context.sav (plain JSON). This keeps as many
    // named copies of it as you like, makes automatic backups every time the game saves, and puts
    // any copy back, either right away from the main menu or the next time the game starts.
    internal static class SavesTab
    {
        class Snap
        {
            public string Dir, Name, Summary;
            public DateTime When;
            public bool Auto;
        }

        const string SaveFile = "context.sav";
        static readonly List<Snap> _snaps = new List<Snap>();
        static int _page;
        static Snap _sel;
        static string _newName = "";
        static bool _confirmDelete;
        static float _nextPoll;
        static DateTime _lastSeenWrite;
        static string _pendingSnapshotName;
        static float _pendingSnapshotAt;

        public static string Root => Path.Combine(Paths.ConfigPath, "Road96Toolkit", "saves");
        static string AutoRoot => Path.Combine(Root, "auto");
        static string PendingFile => Path.Combine(Root, "restore-on-start.txt");

        // where the game keeps its save
        public static string GameSaveDir
        {
            get
            {
                try
                {
                    var p = Application.persistentDataPath;
                    if (!string.IsNullOrEmpty(p)) return Path.Combine(p, "Saves");
                }
                catch { }
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow", "DigixArt", "Road 96", "Saves");
            }
        }
        static string GameSave => Path.Combine(GameSaveDir, SaveFile);

        // called from Plugin.Load, before the game reads its save
        public static void ApplyPendingRestore()
        {
            try
            {
                if (!File.Exists(PendingFile)) return;
                var dir = File.ReadAllText(PendingFile).Trim();
                File.Delete(PendingFile);
                var src = Path.Combine(dir, SaveFile);
                if (!File.Exists(src)) { Plugin.Log.LogWarning("Pending restore missing: " + src); return; }
                BackupCurrent("before restore");
                Directory.CreateDirectory(GameSaveDir);
                File.Copy(src, GameSave, true);
                Plugin.Log.LogInfo("Restored save from " + dir);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pending restore failed: " + e.Message); }
        }

        static byte[] ReadShared(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }

        static string MakeSnapshot(string name, bool auto)
        {
            if (!File.Exists(GameSave)) { ToolkitBehaviour.Toast("No save file yet"); return null; }
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var safe = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            var dir = Path.Combine(auto ? AutoRoot : Root, stamp + (safe.Length > 0 ? " " + safe : ""));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, SaveFile), ReadShared(GameSave));
            File.WriteAllText(Path.Combine(dir, "name.txt"), string.IsNullOrEmpty(safe) ? (auto ? "Auto backup" : "Snapshot") : safe);
            if (auto) TrimAuto();
            return dir;
        }

        static void BackupCurrent(string why)
        {
            try { if (File.Exists(GameSave)) MakeSnapshot(why, true); } catch { }
        }

        static void TrimAuto()
        {
            try
            {
                var keep = Math.Max(1, Plugin.AutoBackupCount.Value);
                var dirs = Directory.GetDirectories(AutoRoot).OrderByDescending(d => d).ToList();
                foreach (var d in dirs.Skip(keep)) Directory.Delete(d, true);
            }
            catch { }
        }

        static string Summarize(string file)
        {
            try
            {
                var bytes = ReadShared(file);
                int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, skip, bytes.Length - skip));
                var r = doc.RootElement;
                int money = r.TryGetProperty("_currentMoney", out var m) ? m.GetInt32() : 0;
                int days = r.TryGetProperty("_daysRemaining", out var d) ? d.GetInt32() : 0;
                int teen = r.TryGetProperty("_currentPlayerFace", out var f) ? f.GetInt32() : -1;
                int runs = 0, tapes = 0;
                string episode = "", last = "";
                if (r.TryGetProperty("_runs", out var rs) && rs.ValueKind == JsonValueKind.Array)
                {
                    runs = rs.GetArrayLength();
                    if (runs > 0 && rs[runs - 1].TryGetProperty("_episodeName", out var ep)) episode = ep.GetString();
                }
                if (r.TryGetProperty("_previousValidatedNarrativeElementsID", out var pv) && pv.ValueKind == JsonValueKind.Array && pv.GetArrayLength() > 0)
                    last = pv[pv.GetArrayLength() - 1].GetString();
                if (r.TryGetProperty("_itemsID", out var it) && it.ValueKind == JsonValueKind.Array)
                    foreach (var x in it.EnumerateArray()) if ((x.GetString() ?? "").StartsWith("COLLECTIBLE")) tapes++;
                return $"trip {runs}  {episode}  last: {last}  ${money}  {days} days  teen #{teen}  {tapes} tapes";
            }
            catch { return "(could not read)"; }
        }

        static void Scan()
        {
            _snaps.Clear();
            void Add(string root, bool auto)
            {
                if (!Directory.Exists(root)) return;
                foreach (var d in Directory.GetDirectories(root))
                {
                    if (!auto && Path.GetFileName(d) == "auto") continue;
                    var f = Path.Combine(d, SaveFile);
                    if (!File.Exists(f)) continue;
                    var nameFile = Path.Combine(d, "name.txt");
                    _snaps.Add(new Snap
                    {
                        Dir = d,
                        Auto = auto,
                        Name = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : Path.GetFileName(d),
                        When = File.GetLastWriteTime(f),
                        Summary = Summarize(f),
                    });
                }
            }
            Add(Root, false);
            Add(AutoRoot, true);
            _snaps.Sort((a, b) => b.When.CompareTo(a.When));
        }

        static bool AtMainMenu => GameStates.Has("MainMenuFallbackState") && !GameStates.Has("GameplayState") && !GameStates.Has("LoadSceneDataState");

        // ask the game to write its save right now, so the snapshot has the latest state
        static void ForceGameSave()
        {
            Game.Try("Game save", () =>
            {
                var sm = UnityEngine.Object.FindObjectOfType<SaveManager>();
                var ctx = Game.Context;
                if (sm != null && ctx != null && !AtMainMenu) sm.SaveContext(ctx, false);
            });
        }

        public static void SnapshotNow(string name)
        {
            ForceGameSave();
            _pendingSnapshotName = name ?? "";
            _pendingSnapshotAt = Time.unscaledTime + 1.5f;
        }

        public static void Update()
        {
            if (_pendingSnapshotName != null && Time.unscaledTime >= _pendingSnapshotAt)
            {
                var name = _pendingSnapshotName;
                _pendingSnapshotName = null;
                Game.Try("Snapshot", () =>
                {
                    if (MakeSnapshot(name, false) != null) ToolkitBehaviour.Toast("Snapshot saved: " + (name.Length > 0 ? name : "Snapshot"));
                    Scan();
                });
            }

            if (!Plugin.AutoBackup.Value || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 5f;
            try
            {
                if (!File.Exists(GameSave)) return;
                var w = File.GetLastWriteTimeUtc(GameSave);
                if (_lastSeenWrite == default) { _lastSeenWrite = w; return; }
                if (w == _lastSeenWrite) return;
                _lastSeenWrite = w;
                MakeSnapshot("Auto backup", true);
                Plugin.Log.LogInfo("Auto backup of the save made");
            }
            catch { }
        }

        public static void Draw()
        {
            if (_snaps.Count == 0 && Event.current?.type == EventType.Layout) Scan();

            Gui.Label("Current save: " + (File.Exists(GameSave) ? Summarize(GameSave) : "none yet"));
            Gui.Space(4);

            Gui.Header("Make a snapshot");
            var r = Gui.Row(1f, 2.4f);
            GUI.Label(r[0], "Name (optional)");
            _newName = Gui.TextField(r[1], _newName);
            r = Gui.Row(2);
            if (GUI.Button(r[0], "Snapshot my trip now"))
            {
                ForceGameSave();
                // give the game a moment to finish writing
                _pendingSnapshotName = _newName ?? "";
                _pendingSnapshotAt = Time.unscaledTime + 1.5f;
                _newName = "";
            }
            if (GUI.Button(r[1], "Open the snapshots folder"))
            {
                Directory.CreateDirectory(Root);
                Application.OpenURL("file:///" + Root.Replace('\\', '/'));
            }
            if (Gui.Toggle($"Automatic backup every time the game saves (keeps the last {Plugin.AutoBackupCount.Value})", Plugin.AutoBackup.Value))
                Plugin.AutoBackup.Value = !Plugin.AutoBackup.Value;

            Gui.Space(4);
            var h = Gui.Row(2f, 1f);
            GUI.Label(h[0], $"Snapshots ({_snaps.Count(s => !s.Auto)} yours, {_snaps.Count(s => s.Auto)} automatic)");
            if (GUI.Button(h[1], "Refresh")) Scan();
            int pick = Gui.List(_snaps, ref _page, s => $"{s.When:dd MMM HH:mm}  {(s.Auto ? "[auto] " : "")}{s.Name}", 9, s => s == _sel);
            if (pick >= 0) { _sel = _snaps[pick]; _confirmDelete = false; }

            Gui.Space(4);
            if (_sel == null) { Gui.Label("Pick a snapshot to put it back or delete it."); return; }
            Gui.Label(_sel.Name + "   " + _sel.When.ToString("dd MMM yyyy HH:mm"));
            Gui.Label(_sel.Summary);

            bool menu = AtMainMenu;
            r = Gui.Row(2);
            if (GUI.Button(r[0], menu ? "Put it back now" : "Put it back (go to the main menu)") && menu) RestoreNow(_sel);
            if (GUI.Button(r[1], "Put it back next time I start")) RestoreOnStart(_sel);
            Gui.Label(menu
                ? "At the main menu. If the menu still shows the old trip, restart the game."
                : "In a trip the game would save over it, so quit to the main menu first, or use the second button.");
            r = Gui.Row(2);
            if (GUI.Button(r[0], _confirmDelete ? "Click again to delete" : "Delete this snapshot"))
            {
                if (!_confirmDelete) _confirmDelete = true;
                else
                {
                    Game.Try("Delete snapshot", () => Directory.Delete(_sel.Dir, true));
                    _sel = null;
                    _confirmDelete = false;
                    Scan();
                }
            }
            if (GUI.Button(r[1], "Keep it (turn auto into mine)") && _sel != null && _sel.Auto)
            {
                Game.Try("Keep snapshot", () =>
                {
                    var dest = Path.Combine(Root, Path.GetFileName(_sel.Dir));
                    Directory.Move(_sel.Dir, dest);
                    File.WriteAllText(Path.Combine(dest, "name.txt"), "Kept backup");
                });
                _sel = null;
                Scan();
            }
        }

        static void RestoreNow(Snap s)
        {
            Game.Try("Restore", () =>
            {
                BackupCurrent("before restore");
                Directory.CreateDirectory(GameSaveDir);
                File.Copy(Path.Combine(s.Dir, SaveFile), GameSave, true);
                _lastSeenWrite = File.GetLastWriteTimeUtc(GameSave);
                var sm = UnityEngine.Object.FindObjectOfType<SaveManager>();
                sm?.LoadContext();
                ToolkitBehaviour.Toast("Save put back. Press Continue");
                Plugin.Log.LogInfo("Restored save from " + s.Dir);
                Scan();
            });
        }

        static void RestoreOnStart(Snap s)
        {
            Game.Try("Restore on start", () =>
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(PendingFile, s.Dir);
                ToolkitBehaviour.Toast("It goes back the next time you start the game");
            });
        }
    }
}
