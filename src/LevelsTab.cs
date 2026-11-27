using System;
using System.Collections.Generic;
using System.Linq;
using BlueEyes.ApplicationStates;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using NB = BlueEyes.NarrativeBrain;
using SceneData = Tool7.Core.SceneManagement.SceneData;

namespace Road96Toolkit
{
    // Start any level (mission) the game has, including developer test levels.
    internal static class LevelsTab
    {
        internal class Level
        {
            public SceneData Data;
            public string Name;
            public string Bubble;       // story id when it is a mission
            public NB.NarrativeElement Element;
            public bool Test;
            public bool Core;
        }

        internal class RawScene
        {
            public string Path, Name;
            public int Index;
        }

        static readonly List<Level> _levels = new List<Level>();
        static readonly List<RawScene> _raw = new List<RawScene>();
        static List<Level> _shownLevels = new List<Level>();
        static List<RawScene> _shownRaw = new List<RawScene>();
        static int _mode; // 0 missions, 1 all levels, 2 raw scenes
        static int _page;
        static string _search = "", _lastSearch = null;
        static int _lastMode = -1;
        static Level _selLevel;
        static RawScene _selRaw;

        public static IReadOnlyList<Level> Levels { get { if (_levels.Count == 0) Scan(); return _levels; } }
        public static IReadOnlyList<RawScene> RawScenes { get { if (_raw.Count == 0) Scan(); return _raw; } }

        static bool LooksLikeTest(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("test") || n.Contains("proto") || n.Contains("debug") || n.Contains("sandbox") || n.Contains("demo") || n.Contains("playground") || n.Contains("dev");
        }

        public static void Scan()
        {
            _levels.Clear();
            _raw.Clear();
            var seen = new HashSet<IntPtr>();
            var core = new HashSet<IntPtr>();

            void Add(SceneData sd, bool isCore)
            {
                if (sd == null || !seen.Add(sd.Pointer)) return;
                var lv = new Level { Data = sd, Name = sd.name, Core = isCore };
                Game.Try("Read level", () =>
                {
                    var b = sd.TryCast<NB.BubbleSceneData>();
                    if (b != null && b._correspondingBubble != null)
                    {
                        lv.Element = b._correspondingBubble;
                        lv.Bubble = b._correspondingBubble.ID;
                    }
                });
                lv.Test = LooksLikeTest(lv.Name);
                _levels.Add(lv);
            }

            // the build's own scene list, the same one the developers' level select uses
            Game.Try("Build scene list", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Tool7.BuildPipeline.BuildInfos>()))
                {
                    var bi = o.TryCast<Tool7.BuildPipeline.BuildInfos>();
                    var list = bi?._sceneList;
                    if (list == null) continue;
                    if (list._coreScenes != null) foreach (var sd in list._coreScenes) { if (sd != null) core.Add(sd.Pointer); Add(sd, true); }
                    if (list._scenes != null) foreach (var sd in list._scenes) Add(sd, false);
                }
            });
            Game.Try("Loaded scene data", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SceneData>()))
                {
                    var sd = o.TryCast<SceneData>();
                    if (sd != null) Add(sd, core.Contains(sd.Pointer));
                }
            });
            _levels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            Game.Try("Raw scenes", () =>
            {
                int n = SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < n; i++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(i);
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    _raw.Add(new RawScene { Path = path, Name = name, Index = i });
                }
            });
            _lastSearch = null;
            Plugin.Log.LogInfo($"Levels: {_levels.Count} level definitions ({_levels.Count(l => l.Bubble != null)} missions, {_levels.Count(l => l.Test)} test or dev), {_raw.Count} raw scenes");
        }

        public static void Draw()
        {
            if (_levels.Count == 0 && _raw.Count == 0) Scan();

            var m = Gui.Row(1f, 1f, 1f, 0.7f);
            string[] modes = { "Missions", "All levels", "Raw scenes" };
            for (int i = 0; i < 3; i++)
                if (Gui.Pick(m[i], modes[i], _mode == i, Gui.TabKind.Choice)) { _mode = i; _page = 0; }
            if (GUI.Button(m[3], "Rescan")) Scan();

            _search = Gui.Search(_search);
            if (_search != _lastSearch || _mode != _lastMode)
            {
                _lastSearch = _search;
                _lastMode = _mode;
                _shownLevels = _levels.Where(l =>
                        (_mode == 0 ? l.Bubble != null : !l.Core) &&
                        (Gui.Contains(l.Name, _search) || Gui.Contains(l.Bubble, _search))).ToList();
                // test levels first in "All levels" so they are easy to find
                if (_mode == 1) _shownLevels = _shownLevels.OrderByDescending(l => l.Test).ThenBy(l => l.Name).ToList();
                _shownRaw = _raw.Where(r => Gui.Contains(r.Path, _search)).ToList();
                _page = 0;
            }

            if (_mode < 2)
            {
                int pick = Gui.List(_shownLevels, ref _page,
                    l => l.Name + (l.Bubble != null ? "   (" + l.Bubble + ")" : "") + (l.Test ? "   [test]" : ""), 15, l => l == _selLevel);
                if (pick >= 0) _selLevel = _shownLevels[pick];
                Gui.Space();
                DrawLevelActions();
            }
            else
            {
                int pick = Gui.List(_shownRaw, ref _page, r => $"#{r.Index}  {r.Name}" + (LooksLikeTest(r.Name) ? "   [test]" : ""), 15, r => r == _selRaw);
                if (pick >= 0) _selRaw = _shownRaw[pick];
                Gui.Space();
                DrawRawActions();
            }
        }

        static void DrawLevelActions()
        {
            if (_selLevel == null) { Gui.Label("Pick a level above."); return; }
            Gui.Label("Selected: " + _selLevel.Name + (_selLevel.Test ? "   (developer test level)" : ""));
            var r = Gui.Row(2);
            if (_selLevel.Element != null)
            {
                if (GUI.Button(r[0], "Start as a mission"))
                {
                    var ctrl = Game.Controller;
                    if (ctrl == null) ToolkitBehaviour.Toast("Start or continue a trip first");
                    else Game.Try("Start mission", () =>
                    {
                        var ctx = ctrl.CurrentContext;
                        if (ctx != null) ctx._overrideNextBubble = _selLevel.Bubble;
                        ctrl._nextBubble = _selLevel.Element;
                        ctrl.GoToNextBubble(false, true);
                        ToolkitBehaviour.Toast("Starting " + _selLevel.Bubble);
                    });
                }
            }
            else GUI.Label(r[0], "  (not a story mission)");
            if (GUI.Button(r[1], "Load the level only")) LoadLevel(_selLevel.Data);
            Gui.Label("\"Load the level only\" skips the story setup. Fine for looking around and test levels.");
        }

        static void DrawRawActions()
        {
            if (_selRaw == null) { Gui.Label("Pick a scene above."); return; }
            Gui.Label(_selRaw.Path);
            var r = Gui.Row(2);
            bool loaded = SceneManager.GetSceneByPath(_selRaw.Path).isLoaded;
            if (GUI.Button(r[0], loaded ? "(already loaded)" : "Add it to the world") && !loaded)
                Game.Try("Load scene", () =>
                {
                    SceneManager.LoadSceneAsync(_selRaw.Path, LoadSceneMode.Additive);
                    ToolkitBehaviour.Toast("Loading " + _selRaw.Name);
                });
            if (GUI.Button(r[1], "Unload it") && loaded)
                Game.Try("Unload scene", () => SceneManager.UnloadSceneAsync(_selRaw.Path));
            Gui.Label("Raw scenes are single pieces of a level. They load on top of what is there.");
        }

        // Every unity scene file a level is made of (used to copy characters out of it).
        public static List<string> ScenePaths(Level lv)
        {
            var list = new List<string>();
            void Take(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Tool7.Core.SceneManagement.SceneField> arr)
            {
                if (arr == null) return;
                foreach (var f in arr)
                {
                    var path = f?.ScenePath;
                    if (!string.IsNullOrEmpty(path) && !list.Contains(path)) list.Add(path);
                }
            }
            Game.Try("Level scenes", () => Take(lv.Data.ScenesToLoad));
            Game.Try("Level scenes (story)", () =>
            {
                var b = lv.Data.TryCast<NB.BubbleSceneData>();
                if (b != null) Take(b.GetSceneToLoad(true, false, true));
            });
            if (list.Count == 0) Game.Try("Level scenes (raw)", () => Take(lv.Data._scenes));
            return list;
        }

        // Same path the game uses: push a LoadSceneDataState that swaps the current level for this one.
        public static void LoadLevel(SceneData sd)
        {
            var stack = GameStates.Stack;
            if (stack == null || sd == null) { ToolkitBehaviour.Toast("Can't load right now"); return; }
            Game.Try("Load level", () =>
            {
                var settings = new LoadSceneStateSettings(sd, true, true, true, null, null, false, false);
                var st = ScriptableObject.CreateInstance(Il2CppType.Of<LoadSceneDataState>()).Cast<LoadSceneDataState>();
                st.Setup(settings);
                stack.AddStateOnStack(st);
                ToolkitBehaviour.Toast("Loading " + sd.name);
                Plugin.Log.LogInfo("Loading level " + sd.name);
            });
        }
    }
}
