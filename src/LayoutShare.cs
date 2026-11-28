using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace Road96Toolkit
{
    // Export everything you built (moved, hidden or deleted objects plus every character and prop
    // you placed) as one file, and load files made by other people.
    internal static class LayoutShare
    {
        public class Spawn
        {
            public string Template { get; set; } = "";
            public string From { get; set; } = "here";
            public float[] Pos { get; set; }
            public float[] Rot { get; set; }
            public float[] Scale { get; set; }
        }

        public class Shared
        {
            public string Name { get; set; } = "";
            public string Author { get; set; } = "";
            public string Level { get; set; } = "";
            public string Made { get; set; } = "";
            public string ToolkitVersion { get; set; } = Plugin.Version;
            public List<SceneEditor.Edit> Edits { get; set; } = new List<SceneEditor.Edit>();
            public List<Spawn> Spawns { get; set; } = new List<Spawn>();
        }

        static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "shared");
        static string _name = "", _author = "";
        static List<string> _files = new List<string>();
        static int _page;
        static string _sel;
        static Shared _selData;
        static readonly List<string> _missingLevels = new List<string>();
        static readonly Queue<string> _toCopy = new Queue<string>();

        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static Vector3 V(float[] a, Vector3 def) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : def;

        static void Export()
        {
            var data = new Shared
            {
                Name = string.IsNullOrWhiteSpace(_name) ? "My layout" : _name.Trim(),
                Author = _author?.Trim() ?? "",
                Level = TravelTab.SceneName,
                Made = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Edits = SceneEditor.AllEdits(),
            };
            foreach (var g in Spawner.Spawned)
            {
                if (g == null) continue;
                var t = g.transform;
                data.Spawns.Add(new Spawn
                {
                    Template = g.name.Replace(" (toolkit)", "").Trim(),
                    From = Spawner.OriginOf(g),
                    Pos = V(t.position), Rot = V(t.eulerAngles), Scale = V(t.localScale),
                });
            }
            Directory.CreateDirectory(Folder);
            var safe = new string(data.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var file = Path.Combine(Folder, safe + ".r96layout");
            File.WriteAllText(file, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            ToolkitBehaviour.Toast($"Exported {data.Edits.Count} changes and {data.Spawns.Count} placed things");
            Plugin.Log.LogInfo("Layout exported to " + file);
            Scan();
        }

        static void Scan()
        {
            Directory.CreateDirectory(Folder);
            _files = Directory.GetFiles(Folder, "*.r96layout").Concat(Directory.GetFiles(Folder, "*.json")).OrderBy(f => f).ToList();
        }

        static Shared Read(string file)
        {
            try { return JsonSerializer.Deserialize<Shared>(File.ReadAllText(file)); }
            catch (Exception e) { ToolkitBehaviour.Toast("Can't read that file"); Plugin.Log.LogWarning("Layout read: " + e.Message); return null; }
        }

        internal static GameObject FindTemplate(Spawn s)
        {
            var e = CharacterCatalog.Entries.FirstOrDefault(x => x.Template != null && x.Name == s.Template && (s.From == "here" || x.From == s.From));
            if (e != null) return e.Template;
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var src = Spawner.Gather(mode).FirstOrDefault(x => x.Name == s.Template);
                if (src?.Go != null) return src.Go;
            }
            return null;
        }

        static void Import(Shared data)
        {
            var (applied, missingObjects) = SceneEditor.ApplyEdits(data.Edits);
            int placed = 0;
            _missingLevels.Clear();
            foreach (var s in data.Spawns)
            {
                var tpl = FindTemplate(s);
                if (tpl == null)
                {
                    if (s.From != "here" && !_missingLevels.Contains(s.From)) _missingLevels.Add(s.From);
                    continue;
                }
                var t = Spawner.SpawnAt(tpl, s.Template, V(s.Pos, Vector3.zero), Quaternion.Euler(V(s.Rot, Vector3.zero)), s.From);
                if (t != null) { t.localScale = V(s.Scale, Vector3.one); placed++; }
            }
            int missing = data.Spawns.Count - placed;
            ToolkitBehaviour.Toast($"Loaded {applied} changes and {placed} placed things" + (missing > 0 ? $", {missing} need other levels" : ""));
        }

        public static void Update()
        {
            if (_toCopy.Count == 0 || CharacterCatalog.Busy) return;
            var label = _toCopy.Dequeue();
            var lv = LevelsTab.Levels.FirstOrDefault(l => l.Name == label);
            if (lv != null) CharacterCatalog.Grab(label, LevelsTab.ScenePaths(lv));
        }

        public static void Draw()
        {
            if (_files.Count == 0 && Event.current?.type == EventType.Layout) Scan();
            Gui.Header("Share what you built");
            var r = Gui.Row(1f, 2.4f);
            GUI.Label(r[0], "Name");
            _name = Gui.TextField(r[1], _name);
            r = Gui.Row(1f, 2.4f);
            GUI.Label(r[0], "Made by");
            _author = Gui.TextField(r[1], _author);
            r = Gui.Row(2);
            if (GUI.Button(r[0], "Export to a file")) Game.Try("Export layout", Export);
            if (GUI.Button(r[1], "Open the shared folder"))
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            Gui.Label($"Exports {SceneEditor.AllEdits().Count} changes and {Spawner.Spawned.Count(g => g != null)} placed characters and props.");

            Gui.Space(4);
            var h = Gui.Row(2f, 1f);
            GUI.Label(h[0], $"Layout files ({_files.Count})");
            if (GUI.Button(h[1], "Refresh")) Scan();
            int pick = Gui.List(_files, ref _page, f => Path.GetFileNameWithoutExtension(f), 7, f => f == _sel);
            if (pick >= 0) { _sel = _files[pick]; _selData = Read(_sel); }

            if (_selData == null) { Gui.Label("Put .r96layout files from friends in the shared folder."); return; }
            Gui.Label($"{_selData.Name}" + (_selData.Author.Length > 0 ? "  by " + _selData.Author : "") + $"   made in {_selData.Level}");
            Gui.Label($"{_selData.Edits.Count} changes, {_selData.Spawns.Count} placed things" + (_selData.Level != TravelTab.SceneName ? "   (you are in " + TravelTab.SceneName + ")" : ""));
            r = Gui.Row(2);
            if (GUI.Button(r[0], "Load it here")) Game.Try("Import layout", () => Import(_selData));
            if (GUI.Button(r[1], "Copy characters it needs"))
            {
                var levels = _selData.Spawns.Select(s => s.From).Where(f => f != "here").Distinct()
                    .Where(f => !CharacterCatalog.Entries.Any(e => e.From == f)).ToList();
                foreach (var l in levels) _toCopy.Enqueue(l);
                ToolkitBehaviour.Toast(levels.Count == 0 ? "Nothing to copy" : $"Copying from {levels.Count} level(s), load the layout when it is done");
            }
            if (_missingLevels.Count > 0) Gui.Label("Missing characters from: " + string.Join(", ", _missingLevels));
            if (CharacterCatalog.Busy) Gui.Label(CharacterCatalog.Status);
        }
    }
}
