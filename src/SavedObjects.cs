using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // Objects you placed or copied that come back every time their level loads, until you remove them.
    // One file per scene in BepInEx/config/Road96Toolkit/saved.
    //
    // A copy of something that was already in the level (say the dust particles at the border) is made
    // again from that same object, found by its path in the scene. Characters and props from the palette
    // are made again from their template, like a shared layout does it.
    internal static class SavedObjects
    {
        public class Entry
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
            public string Name { get; set; } = "";
            public string Scene { get; set; } = "";
            public string Template { get; set; } = "";
            public string From { get; set; } = "here";
            public string SourceScene { get; set; }
            public string SourcePath { get; set; }
            public bool Parented { get; set; }
            public float[] Pos { get; set; }
            public float[] Rot { get; set; }
            public float[] Scale { get; set; }
            public bool Active { get; set; } = true;
        }

        static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "saved");
        static readonly Dictionary<string, List<Entry>> _byScene = new Dictionary<string, List<Entry>>();
        static readonly Dictionary<IntPtr, Entry> _live = new Dictionary<IntPtr, Entry>();
        static readonly Dictionary<string, GameObject> _liveById = new Dictionary<string, GameObject>();
        // where a copy came from, so it can be made again from the same thing
        static readonly Dictionary<IntPtr, (string scene, string path, bool parented)> _source = new Dictionary<IntPtr, (string, string, bool)>();
        static readonly HashSet<string> _dirty = new HashSet<string>();
        static float _nextSave, _nextScan;
        static ConfigEntry<bool> _autoKeep, _autoLayout;

        public static bool AutoKeep => _autoKeep != null && _autoKeep.Value;

        public static void Init(ConfigFile cfg)
        {
            _autoKeep = cfg.Bind("DevView", "KeepEverythingPlaced", false, "Everything you place or copy in the dev view stays in the level until you remove it");
            _autoLayout = cfg.Bind("DevView", "LoadLayoutsAutomatically", false, "Apply your saved scene editor layout every time a level loads");
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var f in Directory.GetFiles(Folder, "*.json"))
                {
                    var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(f));
                    if (list == null || list.Count == 0) continue;
                    _byScene[list[0].Scene] = list;
                }
                Plugin.Log.LogInfo($"Saved objects: {_byScene.Values.Sum(l => l.Count)} in {_byScene.Count} scenes");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Saved objects read: " + e.Message); }
        }

        public static int Count => _byScene.Values.Sum(l => l.Count);
        public static bool IsKept(GameObject go) => go != null && _live.ContainsKey(go.Pointer);

        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static Vector3 V(float[] a, Vector3 def) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : def;

        // a copy made from something in the level
        public static void NoteSource(GameObject clone, GameObject original)
        {
            if (clone == null || original == null) return;
            if (_source.TryGetValue(original.Pointer, out var s)) { _source[clone.Pointer] = s; return; }
            if (!original.scene.IsValid()) return;
            _source[clone.Pointer] = (original.scene.name, SceneEditor.PathOf(original.transform), original.transform.parent != null);
        }

        public static void Keep(GameObject go)
        {
            if (go == null || IsKept(go)) return;
            var t = go.transform;
            var e = new Entry
            {
                Name = go.name,
                Scene = go.scene.IsValid() ? go.scene.name : TravelTab.SceneName,
                Template = go.name.Replace(" (toolkit)", "").Trim(),
                From = Spawner.OriginOf(go),
            };
            if (_source.TryGetValue(go.Pointer, out var s)) { e.SourceScene = s.scene; e.SourcePath = s.path; e.Parented = s.parented; }
            Fill(e, t);
            if (!_byScene.TryGetValue(e.Scene, out var list)) _byScene[e.Scene] = list = new List<Entry>();
            list.Add(e);
            _live[go.Pointer] = e;
            _liveById[e.Id] = go;
            _dirty.Add(e.Scene);
            ToolkitBehaviour.Toast($"{e.Template} stays in {e.Scene} now");
        }

        public static void Forget(GameObject go)
        {
            if (go == null || !_live.TryGetValue(go.Pointer, out var e)) return;
            _live.Remove(go.Pointer);
            Remove(e);
        }

        static void Remove(Entry e)
        {
            _liveById.Remove(e.Id);
            if (_byScene.TryGetValue(e.Scene, out var list)) list.Remove(e);
            _dirty.Add(e.Scene);
        }

        static void Fill(Entry e, Transform t)
        {
            e.Pos = V(t.position); e.Rot = V(t.eulerAngles); e.Scale = V(t.localScale);
            e.Active = t.gameObject.activeSelf;
        }

        // called whenever the toolkit moves something
        public static void Moved(Transform t)
        {
            if (t == null || !_live.TryGetValue(t.gameObject.Pointer, out var e)) return;
            Fill(e, t);
            _dirty.Add(e.Scene);
        }

        static void Save()
        {
            foreach (var scene in _dirty.ToList())
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    var file = Path.Combine(Folder, SceneEditor.Safe(scene) + ".json");
                    var list = _byScene.TryGetValue(scene, out var l) ? l : null;
                    if (list == null || list.Count == 0) { if (File.Exists(file)) File.Delete(file); }
                    else File.WriteAllText(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception e) { Plugin.Log.LogWarning("Saved objects write: " + e.Message); }
            }
            _dirty.Clear();
        }

        // ---------- bringing them back ----------

        class Seen { public float At; public int Tries; public bool LayoutDone; }
        static readonly Dictionary<int, Seen> _scenes = new Dictionary<int, Seen>();
        static readonly float[] TryAt = { 3f, 10f, 30f };

        static bool _warned;

        public static void Update()
        {
            try { Tick(); }
            catch (Exception e) { if (!_warned) { _warned = true; Plugin.Log.LogWarning("Saved objects: " + e); } }
        }

        static void Tick()
        {
            if (_dirty.Count > 0 && Time.unscaledTime > _nextSave) { _nextSave = Time.unscaledTime + 2f; Save(); }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;

            // drop objects that are gone (level unloaded or deleted by the game)
            foreach (var k in _live.Where(kv => !_liveById.TryGetValue(kv.Value.Id, out var g) || g == null).Select(kv => kv.Key).ToList())
            {
                var e = _live[k];
                _live.Remove(k);
                _liveById.Remove(e.Id);
            }

            var loaded = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                int h = sc.handle;
                loaded.Add(h);
                if (!_scenes.TryGetValue(h, out var seen)) _scenes[h] = seen = new Seen { At = Time.unscaledTime };
                float age = Time.unscaledTime - seen.At;
                if (_autoLayout.Value && !seen.LayoutDone && age > 3f) { seen.LayoutDone = true; SceneEditor.ApplyLayoutFor(sc); }
                if (seen.Tries >= TryAt.Length || age < TryAt[seen.Tries]) continue;
                seen.Tries++;
                if (_byScene.TryGetValue(sc.name, out var list)) Restore(list);
            }
            foreach (var h in _scenes.Keys.Where(h => !loaded.Contains(h)).ToList()) _scenes.Remove(h);
        }

        static Scene? Loaded(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded && sc.name == name) return sc;
            }
            return null;
        }

        static int Restore(List<Entry> list, bool loud = false)
        {
            int made = 0, missing = 0;
            foreach (var e in list.ToList())
            {
                if (_liveById.TryGetValue(e.Id, out var g) && g != null) continue;
                var go = Make(e);
                if (go == null) { missing++; continue; }
                _live[go.Pointer] = e;
                _liveById[e.Id] = go;
                made++;
            }
            if (made > 0 || loud)
            {
                ToolkitBehaviour.Toast($"Brought back {made} saved objects" + (missing > 0 ? $", {missing} not found yet" : ""));
                Plugin.Log.LogInfo($"Saved objects: {made} brought back, {missing} not found");
            }
            return made;
        }

        static GameObject Make(Entry e)
        {
            GameObject res = null;
            Game.Try("Bring back " + e.Template, () =>
            {
                var pos = V(e.Pos, Vector3.zero);
                var rot = Quaternion.Euler(V(e.Rot, Vector3.zero));
                if (!string.IsNullOrEmpty(e.SourcePath))
                {
                    var sc = Loaded(e.SourceScene ?? e.Scene);
                    var src = sc.HasValue ? SceneEditor.FindByPath(sc.Value, e.SourcePath) : null;
                    if (src == null) return;
                    var go = src.gameObject;
                    var clone = UnityEngine.Object.Instantiate(go.Cast<UnityEngine.Object>(), pos, rot).Cast<GameObject>();
                    clone.name = e.Template + " (toolkit)";
                    if (e.Parented && src.parent != null) clone.transform.SetParent(src.parent, true);
                    else if (clone.transform.parent != null) clone.transform.SetParent(null, true);
                    clone.transform.SetPositionAndRotation(pos, rot);
                    clone.SetActive(e.Active);
                    Spawner.Track(clone, "here");
                    _source[clone.Pointer] = (e.SourceScene ?? e.Scene, e.SourcePath, e.Parented);
                    res = clone;
                }
                else
                {
                    var tpl = LayoutShare.FindTemplate(new LayoutShare.Spawn { Template = e.Template, From = e.From });
                    if (tpl == null) return;
                    var t = Spawner.SpawnAt(tpl, e.Template, pos, rot, e.From);
                    if (t == null) return;
                    t.gameObject.SetActive(e.Active);
                    res = t.gameObject;
                }
                res.transform.localScale = V(e.Scale, Vector3.one);
            });
            return res;
        }

        // ---------- the list ----------

        static int _page;

        public static void Draw()
        {
            string here = TravelTab.SceneName;
            int all = Count;
            var list = new List<Entry>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded && _byScene.TryGetValue(sc.name, out var l)) list.AddRange(l);
            }
            Gui.Label($"{list.Count} saved objects in the loaded levels, {all} in all levels.");
            if (Gui.Toggle("Keep everything I place or copy", _autoKeep.Value)) _autoKeep.Value = !_autoKeep.Value;
            if (Gui.Toggle("Apply my saved layout when a level loads", _autoLayout.Value)) _autoLayout.Value = !_autoLayout.Value;
            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Bring back missing ones")) foreach (var kv in _byScene) if (Loaded(kv.Key).HasValue) Restore(kv.Value, true);
            if (GUI.Button(r[1], "Open the folder"))
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            int rows = Math.Max(3, (int)((Gui.Bottom - Gui.Y) / (Gui.LineH + Gui.Gap)) - 1);
            int per = rows;
            int pages = Math.Max(1, (list.Count + per - 1) / per);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            foreach (var e in list.Skip(_page * per).Take(per))
            {
                var row = Gui.Row(3f, 1f, 1f);
                bool live = _liveById.TryGetValue(e.Id, out var g) && g != null;
                GUI.Label(row[0], (live ? "" : "[missing] ") + e.Template + (e.SourcePath != null ? "  (copy)" : "") + "   " + e.Scene);
                if (live && GUI.Button(row[1], "Select")) DevView.Select(g.transform);
                if (GUI.Button(row[2], "Remove"))
                {
                    if (live) { _live.Remove(g.Pointer); UnityEngine.Object.Destroy(g); }
                    Remove(e);
                    ToolkitBehaviour.Toast(e.Template + " removed for good");
                    break;
                }
            }
            if (pages > 1)
            {
                r = Gui.Row(1f, 1f, 1f);
                if (GUI.Button(r[0], "<")) _page--;
                GUI.Label(r[1], $"  {_page + 1} / {pages}");
                if (GUI.Button(r[2], ">")) _page++;
            }
        }
    }
}
