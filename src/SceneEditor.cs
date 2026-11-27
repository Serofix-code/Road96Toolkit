using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // Browse, pick and move objects in the loaded scenes, and save the changes per scene.
    internal static class SceneEditor
    {
        static Transform _sel;
        static readonly List<Transform> _list = new List<Transform>();
        static Transform _listParent;   // null means scene roots
        static int _page;
        static string _search = "", _lastSearch = null;
        static List<Transform> _filtered = new List<Transform>();
        static readonly float[] Steps = { 0.01f, 0.1f, 0.5f, 1f, 5f };
        static int _step = 2;
        static bool _carry;
        static float _carryDist = 3f;

        // edits remembered for saving, keyed by scene then path
        public class Edit
        {
            public string Scene { get; set; }
            public string Path { get; set; }
            public float[] Pos { get; set; }
            public float[] Rot { get; set; }
            public float[] Scale { get; set; }
            public bool Active { get; set; } = true;
            public bool Deleted { get; set; }
        }
        static readonly Dictionary<string, Edit> _edits = new Dictionary<string, Edit>();

        static string SaveDir => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "layouts");
        static int _section;

        // everything changed in the loaded scenes: saved layout files plus this session
        internal static List<Edit> AllEdits()
        {
            var merged = new Dictionary<string, Edit>();
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var sc = SceneManager.GetSceneAt(i);
                    if (!sc.isLoaded) continue;
                    var file = Path.Combine(SaveDir, Safe(sc.name) + ".json");
                    if (!File.Exists(file)) continue;
                    var old = JsonSerializer.Deserialize<List<Edit>>(File.ReadAllText(file));
                    if (old != null) foreach (var o in old) merged[o.Scene + "|" + o.Path] = o;
                }
            }
            catch { }
            foreach (var kv in _edits) merged[kv.Key] = kv.Value;
            return merged.Values.ToList();
        }

        internal static (int applied, int missing) ApplyEdits(List<Edit> edits)
        {
            int applied = 0, missing = 0;
            if (edits == null) return (0, 0);
            foreach (var e in edits)
            {
                Transform t = null;
                for (int i = 0; i < SceneManager.sceneCount && t == null; i++)
                {
                    var sc = SceneManager.GetSceneAt(i);
                    if (!sc.isLoaded || (e.Scene != null && sc.name != e.Scene)) continue;
                    t = FindByPath(sc, e.Path);
                }
                if (t == null) { missing++; continue; }
                if (e.Deleted) { UnityEngine.Object.Destroy(t.gameObject); applied++; continue; }
                if (e.Pos != null) t.position = new Vector3(e.Pos[0], e.Pos[1], e.Pos[2]);
                if (e.Rot != null) t.eulerAngles = new Vector3(e.Rot[0], e.Rot[1], e.Rot[2]);
                if (e.Scale != null) t.localScale = new Vector3(e.Scale[0], e.Scale[1], e.Scale[2]);
                t.gameObject.SetActive(e.Active);
                if (e.Scene != null) _edits[e.Scene + "|" + e.Path] = e;
                applied++;
            }
            return (applied, missing);
        }

        public static void Select(Transform t)
        {
            _sel = t;
            if (t != null) ToolkitBehaviour.Toast("Selected " + t.name);
        }

        public static void PickUnderCrosshair()
        {
            var cam = Game.MainCamera;
            if (cam == null) return;
            Game.Try("Pick", () =>
            {
                Vector3 origin = Freecam.Active ? Freecam.Position : cam.transform.position;
                Vector3 dir = Freecam.Active ? Freecam.Rotation * Vector3.forward : cam.transform.forward;
                if (Physics.Raycast(origin, dir, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var t = hit.collider.transform;
                    // prefer the renderer owner so we grab the visible thing, not a tiny collider child
                    var rb = hit.collider.attachedRigidbody;
                    if (rb != null) t = rb.transform;
                    Select(t);
                }
                else ToolkitBehaviour.Toast("Nothing under the crosshair");
            });
        }

        public static void Update()
        {
            if (_sel == null) { _carry = false; return; }
            if (!_carry) return;
            var cam = Game.MainCamera;
            if (cam == null) return;
            Vector3 origin = Freecam.Active ? Freecam.Position : cam.transform.position;
            Quaternion rot = Freecam.Active ? Freecam.Rotation : cam.transform.rotation;
            if (InputHelper.Held(KeyCode.RightBracket)) _carryDist += Time.unscaledDeltaTime * 3f;
            if (InputHelper.Held(KeyCode.LeftBracket)) _carryDist = Mathf.Max(0.5f, _carryDist - Time.unscaledDeltaTime * 3f);
            _sel.position = origin + rot * Vector3.forward * _carryDist;
            Remember(_sel);
        }

        static void BuildList()
        {
            _list.Clear();
            Game.Try("List objects", () =>
            {
                if (_listParent == null)
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var sc = SceneManager.GetSceneAt(i);
                        if (!sc.isLoaded) continue;
                        foreach (var go in sc.GetRootGameObjects())
                            if (go != null) _list.Add(go.transform);
                    }
                }
                else
                {
                    for (int i = 0; i < _listParent.childCount; i++)
                        _list.Add(_listParent.GetChild(i));
                }
            });
            _lastSearch = null;
        }

        public static void Draw()
        {
            var sec = Gui.Row(2);
            if (GUI.Button(sec[0], _section == 0 ? "[ Objects ]" : "Objects")) _section = 0;
            if (GUI.Button(sec[1], _section == 1 ? "[ Share layouts ]" : "Share layouts")) _section = 1;
            Gui.Space(4);
            if (_section == 1) { LayoutShare.Draw(); return; }
            if (_sel == null) DrawBrowser();
            else DrawInspector();
        }

        static void DrawBrowser()
        {
            Gui.Label($"Press {Plugin.PickKey} to grab what is in the middle of the screen, or browse below.");

            var r = Gui.Row(1f, 1f, 1f);
            if (GUI.Button(r[0], "Scene roots")) { _listParent = null; BuildList(); _page = 0; }
            if (GUI.Button(r[1], "Up one level") && _listParent != null) { _listParent = _listParent.parent; BuildList(); _page = 0; }
            if (GUI.Button(r[2], "Refresh")) BuildList();

            if (_list.Count == 0 || (_listParent != null && _listParent == null)) BuildList();

            string where = _listParent == null ? $"{SceneManager.sceneCount} loaded scenes" : PathOf(_listParent);
            Gui.Label("Looking at: " + where);

            _search = Gui.Search(_search);
            _list.RemoveAll(t => t == null);
            if (_search != _lastSearch)
            {
                _lastSearch = _search;
                _filtered = _list.Where(t => Gui.Contains(t.name, _search)).ToList();
                _page = 0;
            }

            var rows = _filtered;
            int start = _page * 15;
            int pages = Math.Max(1, (rows.Count + 14) / 15);
            if (_page >= pages) _page = pages - 1;
            for (int i = start; i < Math.Min(rows.Count, start + 15); i++)
            {
                var t = rows[i];
                if (t == null) continue;
                var c = Gui.Row(5f, 1.2f, 1f);
                string label = (t.gameObject.activeSelf ? "" : "(off) ") + t.name + (t.childCount > 0 ? $"  [{t.childCount}]" : "");
                GUI.Label(c[0], label);
                if (GUI.Button(c[1], "Open") && t.childCount > 0) { _listParent = t; BuildList(); _page = 0; return; }
                if (GUI.Button(c[2], "Edit")) { Select(t); return; }
            }
            var nav = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && _page > 0) _page--;
            GUI.Label(nav[1], $"  page {_page + 1} / {pages}   ({rows.Count})");
            if (GUI.Button(nav[2], "Next >") && _page < pages - 1) _page++;

            Gui.Space();
            DrawSaveLoad();
        }

        static void DrawInspector()
        {
            var t = _sel;
            var go = t.gameObject;

            var top = Gui.Row(1f, 1f, 1f);
            if (GUI.Button(top[0], "< Back to list")) { _sel = null; _carry = false; return; }
            if (GUI.Button(top[1], "Select parent") && t.parent != null) { Select(t.parent); return; }
            if (GUI.Button(top[2], "Select root")) { Select(t.root); return; }

            Gui.Label("Object: " + t.name);
            Gui.Label("Path: " + PathOf(t));
            Gui.Label($"Scene: {go.scene.name}   children: {t.childCount}");

            var r = Gui.Row(2f, 1f);
            GUI.Label(r[0], $"Step: {Steps[_step]}  (move in meters, rotate x10 degrees)");
            if (GUI.Button(r[1], "Change step")) _step = (_step + 1) % Steps.Length;

            float st = Steps[_step];
            Gui.Header("Position");
            var p = t.position;
            p = Axis("X", p, 0, st);
            p = Axis("Y", p, 1, st);
            p = Axis("Z", p, 2, st);
            if (p != t.position) { t.position = p; Remember(t); }

            Gui.Header("Rotation");
            var e = t.eulerAngles;
            var e2 = Axis("Pitch", e, 0, st * 10f);
            e2 = Axis("Yaw", e2, 1, st * 10f);
            e2 = Axis("Roll", e2, 2, st * 10f);
            if (e2 != e) { t.eulerAngles = e2; Remember(t); }

            Gui.Header("Scale");
            var s = t.localScale;
            var sr = Gui.Row(3f, 1f, 1f, 1f);
            GUI.Label(sr[0], $"{s.x:0.###}, {s.y:0.###}, {s.z:0.###}");
            if (GUI.Button(sr[1], "-")) { t.localScale = s * (1f / (1f + st)); Remember(t); }
            if (GUI.Button(sr[2], "+")) { t.localScale = s * (1f + st); Remember(t); }
            if (GUI.Button(sr[3], "1x")) { t.localScale = Vector3.one; Remember(t); }

            Gui.Space();
            var a = Gui.Row(2);
            if (GUI.Button(a[0], (go.activeSelf ? "[ON] " : "[off] ") + "Visible")) { go.SetActive(!go.activeSelf); Remember(t); }
            if (GUI.Button(a[1], (_carry ? "[ON] " : "[off] ") + "Carry with camera  [ and ]")) _carry = !_carry;

            a = Gui.Row(3);
            if (GUI.Button(a[0], "Bring here"))
            {
                var cam = Game.MainCamera;
                if (cam != null)
                {
                    var o = Freecam.Active ? Freecam.Position : cam.transform.position;
                    var f = (Freecam.Active ? Freecam.Rotation : cam.transform.rotation) * Vector3.forward;
                    t.position = o + f * 3f;
                    Remember(t);
                }
            }
            if (GUI.Button(a[1], "Drop to ground"))
            {
                if (Physics.Raycast(t.position + Vector3.up * 0.5f, Vector3.down, out var hit, 200f))
                {
                    t.position = hit.point;
                    Remember(t);
                }
            }
            if (GUI.Button(a[2], "Face camera"))
            {
                var cam = Game.MainCamera;
                if (cam != null)
                {
                    var o = Freecam.Active ? Freecam.Position : cam.transform.position;
                    var d = o - t.position;
                    d.y = 0;
                    if (d.sqrMagnitude > 0.001f) { t.rotation = Quaternion.LookRotation(d); Remember(t); }
                }
            }

            a = Gui.Row(2);
            if (GUI.Button(a[0], "Duplicate"))
            {
                Game.Try("Duplicate", () =>
                {
                    var clone = UnityEngine.Object.Instantiate(go.Cast<UnityEngine.Object>(), t.position + t.right * 1f, t.rotation).Cast<GameObject>();
                    clone.name = go.name + " (toolkit)";
                    if (t.parent != null) clone.transform.SetParent(t.parent, true);
                    Select(clone.transform);
                });
            }
            if (GUI.Button(a[1], "Delete"))
            {
                var path = PathOf(t);
                var scene = go.scene.name;
                if (!go.name.EndsWith("(toolkit)"))
                    _edits[scene + "|" + path] = new Edit { Scene = scene, Path = path, Deleted = true };
                UnityEngine.Object.Destroy(go);
                _sel = null;
                _carry = false;
                ToolkitBehaviour.Toast("Deleted " + path);
                return;
            }

            Gui.Space();
            Gui.Header("Children");
            int shown = 0;
            for (int i = 0; i < t.childCount && shown < 5; i++, shown++)
            {
                var ch = t.GetChild(i);
                if (GUI.Button(new Rect(Gui.X, Gui.Y, Gui.W, Gui.LineH), ch.name)) { Select(ch); return; }
                Gui.Y += Gui.LineH + Gui.Gap;
            }
            if (t.childCount > 5) Gui.Label($"... and {t.childCount - 5} more, use the list view");

            Gui.Space();
            DrawSaveLoad();
        }

        static Vector3 Axis(string name, Vector3 v, int i, float step)
        {
            var r = Gui.Row(2f, 1f, 1f, 1f, 1f);
            GUI.Label(r[0], $"{name}: {v[i]:0.###}");
            if (GUI.Button(r[1], "--")) v[i] -= step * 10f;
            if (GUI.Button(r[2], "-")) v[i] -= step;
            if (GUI.Button(r[3], "+")) v[i] += step;
            if (GUI.Button(r[4], "++")) v[i] += step * 10f;
            return v;
        }

        static void DrawSaveLoad()
        {
            Gui.Header($"Layout ({_edits.Count} changes this session)");
            var r = Gui.Row(3);
            if (GUI.Button(r[0], "Save layout")) Save();
            if (GUI.Button(r[1], "Load layout")) Load();
            if (GUI.Button(r[2], "Forget changes")) _edits.Clear();
            Gui.Label("Layouts go to BepInEx/config/Road96Toolkit/layouts, one file per scene.");
        }

        internal static void Remember(Transform t)
        {
            if (t == null) return;
            var go = t.gameObject;
            if (go.name.EndsWith("(toolkit)")) return; // clones are not saved, they do not exist on reload
            var scene = go.scene.name;
            var path = PathOf(t);
            var p = t.position;
            var q = t.eulerAngles;
            var s = t.localScale;
            _edits[scene + "|" + path] = new Edit
            {
                Scene = scene,
                Path = path,
                Pos = new[] { p.x, p.y, p.z },
                Rot = new[] { q.x, q.y, q.z },
                Scale = new[] { s.x, s.y, s.z },
                Active = go.activeSelf,
            };
        }

        internal static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Transform FindByPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null || root.name != parts[0]) continue;
                if (parts.Length == 1) return root.transform;
                var found = root.transform.Find(string.Join("/", parts.Skip(1)));
                if (found != null) return found;
            }
            return null;
        }

        static void Save()
        {
            Game.Try("Save layout", () =>
            {
                Directory.CreateDirectory(SaveDir);
                int files = 0;
                foreach (var grp in _edits.Values.GroupBy(e => e.Scene))
                {
                    var file = Path.Combine(SaveDir, Safe(grp.Key) + ".json");
                    var merged = new Dictionary<string, Edit>();
                    if (File.Exists(file))
                    {
                        var old = JsonSerializer.Deserialize<List<Edit>>(File.ReadAllText(file));
                        if (old != null) foreach (var o in old) merged[o.Path] = o;
                    }
                    foreach (var e in grp) merged[e.Path] = e;
                    File.WriteAllText(file, JsonSerializer.Serialize(merged.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
                    files++;
                }
                ToolkitBehaviour.Toast($"Saved {_edits.Count} changes to {files} file(s)");
            });
        }

        static void Load()
        {
            Game.Try("Load layout", () =>
            {
                if (!Directory.Exists(SaveDir)) { ToolkitBehaviour.Toast("No layouts saved yet"); return; }
                int applied = 0, missing = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var sc = SceneManager.GetSceneAt(i);
                    if (!sc.isLoaded) continue;
                    var file = Path.Combine(SaveDir, Safe(sc.name) + ".json");
                    if (!File.Exists(file)) continue;
                    var edits = JsonSerializer.Deserialize<List<Edit>>(File.ReadAllText(file));
                    if (edits == null) continue;
                    foreach (var e in edits) e.Scene ??= sc.name;
                    var (a, m) = ApplyEdits(edits);
                    applied += a; missing += m;
                }
                ToolkitBehaviour.Toast($"Applied {applied} changes" + (missing > 0 ? $", {missing} objects not found" : ""));
            });
        }

        static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "unnamed" : s;
        }
    }
}
