using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // Clones characters (or any prefab that is loaded in memory) in front of the camera.
    internal static class Spawner
    {
        class Source
        {
            public GameObject Go;
            public string Name;
            public string Kind;
        }

        static List<Source> _sources = new List<Source>();
        static List<Source> _filtered = new List<Source>();
        static readonly List<GameObject> _spawned = new List<GameObject>();
        static int _mode; // 0 characters here, 1 characters from other levels, 2 any prefab
        static int _page, _spawnPage, _levelPage;
        static string _search = "", _lastSearch = null;
        static string _levelSearch = "", _lastLevelSearch = null;
        static List<LevelsTab.Level> _levels = new List<LevelsTab.Level>();
        static LevelsTab.Level _level;
        static Source _sel;
        static bool _stayStill = true;
        static float _distance = 3f;
        static int _catalogSeen = -1;

        public static IReadOnlyList<GameObject> Spawned => _spawned;

        static void Scan()
        {
            _sources.Clear();
            _sel = null;
            Game.Try("Scan characters", () =>
            {
                if (_mode == 0)
                {
                    var seen = new HashSet<string>();
                    var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<NPC>());
                    foreach (var o in all)
                    {
                        var npc = o.TryCast<NPC>();
                        if (npc == null) continue;
                        var go = npc.gameObject;
                        if (go == null || IsOurs(go)) continue;
                        bool inScene = go.scene.IsValid();
                        string key = go.name + (inScene ? "#s" : "#p");
                        if (!seen.Add(key)) continue;
                        _sources.Add(new Source { Go = go, Name = go.name, Kind = inScene ? "in scene" : "prefab" });
                    }
                }
                else if (_mode == 1)
                {
                    foreach (var e in CharacterCatalog.Entries)
                        if (e.Template != null) _sources.Add(new Source { Go = e.Template, Name = e.Name, Kind = "from " + e.From });
                    _catalogSeen = CharacterCatalog.Entries.Count;
                }
                else
                {
                    var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>());
                    var seen = new HashSet<string>();
                    foreach (var o in all)
                    {
                        var go = o.TryCast<GameObject>();
                        if (go == null) continue;
                        if (go.scene.IsValid()) continue;           // only assets, not scene objects
                        if (go.transform.parent != null) continue;  // only roots
                        if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                        if (go.GetComponentInChildren<Renderer>(true) == null) continue;
                        if (!seen.Add(go.name)) continue;
                        _sources.Add(new Source { Go = go, Name = go.name, Kind = "prefab" });
                    }
                }
            });
            _sources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _lastSearch = null;
            Plugin.Log.LogInfo($"Spawner found {_sources.Count} entries in mode {_mode}");
        }

        static bool IsOurs(GameObject go)
        {
            if (go.name.EndsWith("(toolkit)") || go.name.StartsWith("Road96Toolkit")) return true;
            var root = go.transform.root;
            return root != null && root.name.StartsWith("Road96Toolkit");
        }

        public static void Draw()
        {
            var tabs = Gui.Row(3);
            string[] names = { "Characters here", "Other levels", "Any prefab" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(tabs[i], _mode == i ? "[ " + names[i] + " ]" : names[i])) { _mode = i; Scan(); }

            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Rescan")) Scan();
            if (GUI.Button(r[1], (_stayStill ? "[ON] " : "[off] ") + "Clones stand still")) _stayStill = !_stayStill;

            r = Gui.Row(2f, 1f, 1f);
            GUI.Label(r[0], $"Spawn distance: {_distance:0.0} m");
            if (GUI.Button(r[1], "-")) _distance = Mathf.Max(0.5f, _distance - 0.5f);
            if (GUI.Button(r[2], "+")) _distance = Mathf.Min(50f, _distance + 0.5f);

            if (_mode == 1) DrawOtherLevels();
            else Gui.Label(_mode == 0 ? "Characters the current area has loaded." : "Anything with a model that is loaded in memory.");

            if (_mode == 1 && _catalogSeen != CharacterCatalog.Entries.Count) Scan();

            _search = Gui.Search(_search);
            if (_search != _lastSearch)
            {
                _lastSearch = _search;
                _filtered = _sources.Where(s => Gui.Contains(s.Name, _search) || Gui.Contains(s.Kind, _search)).ToList();
                _page = 0;
            }

            int pick = Gui.List(_filtered, ref _page, s => $"{s.Name}   ({s.Kind})", _mode == 1 ? 6 : 10, s => s == _sel);
            if (pick >= 0) _sel = _filtered[pick];

            var a = Gui.Row(2);
            if (GUI.Button(a[0], _sel != null ? "Spawn " + _sel.Name : "Spawn (pick one)") && _sel != null) Spawn(_sel);
            if (GUI.Button(a[1], "3D preview") && _sel != null) NpcPreview.Show(_sel.Go, _sel.Name);

            Gui.Space();
            Gui.Header($"Spawned by you ({_spawned.Count})");
            _spawned.RemoveAll(g => g == null);
            var r2 = Gui.Row(2);
            if (GUI.Button(r2[0], "Delete all spawned"))
            {
                foreach (var g in _spawned) Game.Try("Delete", () => UnityEngine.Object.Destroy(g));
                _spawned.Clear();
            }
            if (GUI.Button(r2[1], "Edit last one") && _spawned.Count > 0)
                SceneEditor.Select(_spawned[_spawned.Count - 1].transform);

            int sp = Gui.List(_spawned, ref _spawnPage, g => g.name + "   (click to edit)", 3);
            if (sp >= 0) SceneEditor.Select(_spawned[sp].transform);
        }

        static void DrawOtherLevels()
        {
            Gui.Label("Pick a level, then copy its characters. The level loads out of sight for a moment.");
            _levelSearch = Gui.Search(_levelSearch);
            if (_levelSearch != _lastLevelSearch)
            {
                _lastLevelSearch = _levelSearch;
                _levels = LevelsTab.Levels.Where(l => !l.Core && (Gui.Contains(l.Name, _levelSearch) || Gui.Contains(l.Bubble, _levelSearch))).ToList();
                _levelPage = 0;
            }
            int lp = Gui.List(_levels, ref _levelPage, l => l.Name + (l.Bubble != null ? "   (" + l.Bubble + ")" : ""), 5, l => l == _level);
            if (lp >= 0) _level = _levels[lp];

            var r = Gui.Row(2f, 1f);
            if (CharacterCatalog.Busy) GUI.Label(r[0], CharacterCatalog.Status);
            else if (GUI.Button(r[0], _level != null ? "Copy characters from " + _level.Name : "Copy characters (pick a level)") && _level != null)
            {
                var paths = LevelsTab.ScenePaths(_level);
                if (paths.Count == 0) ToolkitBehaviour.Toast("Couldn't find the scenes of that level");
                else CharacterCatalog.Grab(_level.Name, paths);
            }
            if (GUI.Button(r[1], "Clear copies")) { CharacterCatalog.Clear(); Scan(); }
            if (!CharacterCatalog.Busy && CharacterCatalog.Status.Length > 0) Gui.Label(CharacterCatalog.Status);
        }

        static void Spawn(Source s) => SpawnObject(s.Go, s.Name);

        public static void SpawnObject(GameObject template, string name)
        {
            if (template == null)
            {
                ToolkitBehaviour.Toast("That one was unloaded, rescan");
                return;
            }

            Vector3 camPos;
            Quaternion camRot;
            if (Freecam.Active) { camPos = Freecam.Position; camRot = Freecam.Rotation; }
            else
            {
                var cam = Game.MainCamera;
                if (cam == null) { ToolkitBehaviour.Toast("No camera"); return; }
                camPos = cam.transform.position;
                camRot = cam.transform.rotation;
            }

            var flatFwd = camRot * Vector3.forward;
            flatFwd.y = 0;
            if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
            flatFwd.Normalize();
            var pos = camPos + flatFwd * _distance;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 50f)) pos = hit.point;
            var rot = Quaternion.LookRotation(-flatFwd, Vector3.up);

            Game.Try("Spawn", () =>
            {
                var obj = UnityEngine.Object.Instantiate(template.Cast<UnityEngine.Object>(), pos, rot);
                var clone = obj.Cast<GameObject>();
                clone.name = name + " (toolkit)";
                // copies from the catalog live under a hidden holder, so put them into the world
                if (clone.transform.parent != null) clone.transform.SetParent(null, true);
                clone.hideFlags = HideFlags.None;
                clone.layer = template.layer;
                clone.SetActive(true);
                if (_stayStill) MakeStill(clone);
                _spawned.Add(clone);
                ToolkitBehaviour.Toast("Spawned " + name);
            });
        }

        // Stop clones from walking off to their original routine spots.
        public static void MakeStill(GameObject go)
        {
            Game.Try("Still routine", () =>
            {
                foreach (var c in go.GetComponentsInChildren<RoutineController>(true)) c.enabled = false;
                foreach (var c in go.GetComponentsInChildren<Routine>(true)) c.enabled = false;
            });
            Game.Try("Still agent", () =>
            {
                foreach (var a in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) a.enabled = false;
            });
        }
    }
}
