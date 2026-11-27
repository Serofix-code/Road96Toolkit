using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // Characters only exist inside their own level, so to spawn someone from another level we load
    // that level's scenes on the side, copy its characters into a hidden holder, and unload it again.
    internal static class CharacterCatalog
    {
        public class Entry
        {
            public GameObject Template;
            public string Name;
            public string From;
        }

        public static readonly List<Entry> Entries = new List<Entry>();

        // which level a copied character came from, null for things that were already here
        public static string FromOf(GameObject template)
        {
            if (template == null) return null;
            foreach (var e in Entries) if (e.Template != null && e.Template.Pointer == template.Pointer) return e.From;
            return null;
        }
        static GameObject _holder;

        static readonly Queue<string> _todo = new Queue<string>();
        static AsyncOperation _op;
        static string _current;
        static string _label;
        static int _framesSinceLoad;
        public static bool Busy => _op != null || _todo.Count > 0 || _current != null;
        public static string Status { get; private set; } = "";

        static GameObject Holder
        {
            get
            {
                if (_holder == null)
                {
                    _holder = new GameObject("Road96Toolkit_CharacterCatalog");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                    _holder.hideFlags = HideFlags.HideAndDontSave;
                }
                return _holder;
            }
        }

        public static void Grab(string label, IEnumerable<string> scenePaths)
        {
            if (Busy) { ToolkitBehaviour.Toast("Still busy with the last level"); return; }
            _label = label;
            foreach (var p in scenePaths)
                if (!string.IsNullOrEmpty(p) && !SceneManager.GetSceneByPath(p).isLoaded) _todo.Enqueue(p);
            if (_todo.Count == 0) { ToolkitBehaviour.Toast("Those scenes are already loaded, use the normal list"); return; }
            Status = $"Loading {label} ({_todo.Count} scene(s))";
            Plugin.Log.LogInfo("Catalog: " + Status);
        }

        public static void Update()
        {
            if (_current == null)
            {
                if (_todo.Count == 0) return;
                _current = _todo.Dequeue();
                try
                {
                    _op = SceneManager.LoadSceneAsync(_current, LoadSceneMode.Additive);
                    _framesSinceLoad = 0;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Catalog load failed: " + e.Message);
                    _current = null;
                    _op = null;
                }
                return;
            }

            if (_op != null && !_op.isDone) { Status = $"Loading {_label}  {(int)(_op.progress * 100)}%"; return; }
            _op = null;

            // give the scene one frame to finish waking up before we copy from it
            if (_framesSinceLoad++ < 1) return;

            int got = 0;
            var scene = SceneManager.GetSceneByPath(_current);
            Game.Try("Catalog copy", () =>
            {
                if (!scene.isLoaded) return;
                var seen = new HashSet<string>();
                foreach (var e in Entries) seen.Add(e.Name);
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var npc in root.GetComponentsInChildren<NPC>(true))
                    {
                        if (npc == null) continue;
                        var go = npc.gameObject;
                        if (!seen.Add(go.name)) continue;
                        // copy while the source is switched off so the copy does not start running
                        bool was = go.activeSelf;
                        go.SetActive(false);
                        var copy = UnityEngine.Object.Instantiate(go.Cast<UnityEngine.Object>(), Holder.transform).Cast<GameObject>();
                        copy.name = go.name;
                        go.SetActive(was);
                        Entries.Add(new Entry { Template = copy, Name = go.name, From = _label });
                        got++;
                    }
                }
            });
            Plugin.Log.LogInfo($"Catalog: {got} character(s) from {_current}");

            Game.Try("Catalog unload", () => SceneManager.UnloadSceneAsync(scene));
            _current = null;
            if (_todo.Count == 0)
            {
                Status = $"Done with {_label}. {Entries.Count} characters in the catalog.";
                ToolkitBehaviour.Toast(Status);
            }
        }

        public static void Clear()
        {
            foreach (var e in Entries)
                if (e.Template != null) UnityEngine.Object.Destroy(e.Template);
            Entries.Clear();
            Status = "";
        }
    }
}
