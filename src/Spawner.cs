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
            public bool InScene;
            public string Kind;
        }

        static List<Source> _sources = new List<Source>();
        static List<Source> _filtered = new List<Source>();
        static readonly List<GameObject> _spawned = new List<GameObject>();
        static int _mode; // 0 characters, 1 prefabs
        static int _page, _spawnPage;
        static string _search = "", _lastSearch = null;
        static bool _stayStill = true;
        static float _distance = 3f;

        public static IReadOnlyList<GameObject> Spawned => _spawned;

        static void Scan()
        {
            _sources.Clear();
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
                        if (go == null || IsOurClone(go)) continue;
                        bool inScene = go.scene.IsValid();
                        string key = go.name + (inScene ? "#s" : "#p");
                        if (!seen.Add(key)) continue;
                        _sources.Add(new Source { Go = go, Name = go.name, InScene = inScene, Kind = inScene ? "in scene" : "prefab" });
                    }
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
                        _sources.Add(new Source { Go = go, Name = go.name, InScene = false, Kind = "prefab" });
                    }
                }
            });
            _sources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _lastSearch = null;
            Plugin.Log.LogInfo($"Spawner found {_sources.Count} {(_mode == 0 ? "characters" : "prefabs")}");
        }

        static bool IsOurClone(GameObject go) => go.name.EndsWith("(toolkit)");

        public static void Draw()
        {
            var tabs = Gui.Row(2);
            if (GUI.Button(tabs[0], _mode == 0 ? "[ Characters ]" : "Characters")) { _mode = 0; Scan(); }
            if (GUI.Button(tabs[1], _mode == 1 ? "[ Any prefab ]" : "Any prefab")) { _mode = 1; Scan(); }

            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Rescan")) Scan();
            if (GUI.Button(r[1], (_stayStill ? "[ON] " : "[off] ") + "Clones stand still")) _stayStill = !_stayStill;

            r = Gui.Row(2f, 1f, 1f);
            GUI.Label(r[0], $"Spawn distance: {_distance:0.0} m");
            if (GUI.Button(r[1], "-")) _distance = Mathf.Max(0.5f, _distance - 0.5f);
            if (GUI.Button(r[2], "+")) _distance = Mathf.Min(50f, _distance + 0.5f);

            Gui.Label("Characters that the current area has loaded show up here.");
            _search = Gui.Search(_search);
            if (_search != _lastSearch)
            {
                _lastSearch = _search;
                _filtered = _sources.Where(s => Gui.Contains(s.Name, _search)).ToList();
                _page = 0;
            }

            int pick = Gui.List(_filtered, ref _page, s => $"{s.Name}   ({s.Kind})", 11);
            if (pick >= 0) Spawn(_filtered[pick]);

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

            int sp = Gui.List(_spawned, ref _spawnPage, g => g.name + "   (click to edit)", 4);
            if (sp >= 0) SceneEditor.Select(_spawned[sp].transform);
        }

        static void Spawn(Source s)
        {
            if (s.Go == null)
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
                var obj = UnityEngine.Object.Instantiate(s.Go.Cast<UnityEngine.Object>(), pos, rot);
                var clone = obj.Cast<GameObject>();
                clone.name = s.Name + " (toolkit)";
                clone.SetActive(true);
                if (_stayStill) MakeStill(clone);
                _spawned.Add(clone);
                ToolkitBehaviour.Toast("Spawned " + s.Name);
            });
        }

        // Stop clones from walking off to their original routine spots.
        static void MakeStill(GameObject go)
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
