using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Remembers which facts the current teen got or lost on this trip. The game keeps no list of that,
    // so we take a copy of every fact when the trip starts and watch AddTag / RemoveTag after that.
    internal static class RunFacts
    {
        public class Data
        {
            public string Key { get; set; } = "";
            public List<string> AtStart { get; set; }      // null when we only started watching midway
            public List<string> Gained { get; set; } = new List<string>();
            public List<string> Lost { get; set; } = new List<string>();
        }

        static Data _d;
        static bool _loaded, _wasStarted;
        static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "runfacts.json");

        public static void Patch(Harmony h)
        {
            try
            {
                var t = typeof(NB.NarrativeContext);
                h.Patch(AccessTools.Method(t, nameof(NB.NarrativeContext.OnRunStarted)),
                    prefix: new HarmonyMethod(typeof(RunFacts), nameof(StartPrefix)),
                    postfix: new HarmonyMethod(typeof(RunFacts), nameof(StartPostfix)));
                h.Patch(AccessTools.Method(t, nameof(NB.NarrativeContext.AddTag)), postfix: new HarmonyMethod(typeof(RunFacts), nameof(AddPostfix)));
                h.Patch(AccessTools.Method(t, nameof(NB.NarrativeContext.RemoveTag)), postfix: new HarmonyMethod(typeof(RunFacts), nameof(RemovePostfix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Run facts hook failed: " + e.Message); }
        }

        static Data D
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    try { if (File.Exists(FilePath)) _d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)); }
                    catch (Exception e) { Plugin.Log.LogWarning("runfacts.json: " + e.Message); }
                }
                return _d;
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_d));
            }
            catch (Exception e) { Plugin.Log.LogWarning("runfacts.json: " + e.Message); }
        }

        static string KeyOf(NB.NarrativeContext ctx)
        {
            try { return $"{ctx.CurrentRuns}|{ctx._allTimeRuns?.Count ?? 0}|{ctx._isNewGamePlus}"; }
            catch { return ""; }
        }

        static List<string> TagsOf(NB.NarrativeContext ctx)
        {
            var res = new List<string>();
            var l = ctx._currentTags;
            if (l != null) for (int i = 0; i < l.Count; i++) if (!string.IsNullOrEmpty(l[i])) res.Add(l[i]);
            return res;
        }

        static bool IsMain(NB.NarrativeContext ctx) => ctx != null && Game.Context != null && ctx.Pointer == Game.Context.Pointer;

        // the data for this trip, made fresh (without a start copy) when we have nothing for it yet
        static Data Current(NB.NarrativeContext ctx)
        {
            var key = KeyOf(ctx);
            var d = D;
            if (d == null || d.Key != key) { _d = new Data { Key = key }; Save(); }
            return _d;
        }

        static void StartPrefix(NB.NarrativeContext __instance)
        {
            try { _wasStarted = __instance._runStarted; } catch { _wasStarted = false; }
        }

        static void StartPostfix(NB.NarrativeContext __instance)
        {
            try
            {
                if (Game.Context != null && !IsMain(__instance)) return;
                var key = KeyOf(__instance);
                var d = D;
                // loading a save in the middle of a trip keeps what we had
                if (_wasStarted && d != null && d.Key == key) return;
                _d = new Data { Key = key, AtStart = TagsOf(__instance) };
                Save();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Run facts start: " + e.Message); }
        }

        static void AddPostfix(NB.NarrativeContext __instance, string __0)
        {
            try
            {
                if (string.IsNullOrEmpty(__0) || !IsMain(__instance) || !__instance._runStarted) return;
                var d = Current(__instance);
                bool changed = d.Lost.Remove(__0);
                if ((d.AtStart == null || !d.AtStart.Contains(__0)) && !d.Gained.Contains(__0)) { d.Gained.Add(__0); changed = true; }
                if (changed) Save();
            }
            catch { }
        }

        static void RemovePostfix(NB.NarrativeContext __instance, string __0)
        {
            try
            {
                if (string.IsNullOrEmpty(__0) || !IsMain(__instance) || !__instance._runStarted || __instance.HasTag(__0)) return;
                var d = Current(__instance);
                bool changed = d.Gained.Remove(__0);
                if ((d.AtStart == null || d.AtStart.Contains(__0)) && !d.Lost.Contains(__0)) { d.Lost.Add(__0); changed = true; }
                if (changed) Save();
            }
            catch { }
        }

        public class View
        {
            public List<string> Gained = new List<string>(), Lost = new List<string>(), Played = new List<string>();
            public bool FromStart, Started;
            public int Teen = -1, Trip;
        }

        // what this teen got and lost on this trip, plus the scenarios they finished
        public static View Read(NB.NarrativeContext ctx)
        {
            var v = new View();
            if (ctx == null) return v;
            Game.Try("Run facts read", () =>
            {
                v.Started = ctx._runStarted;
                v.Trip = ctx.CurrentRuns;
                var run = ctx.CurrentRun;
                if (run != null)
                {
                    v.Teen = run._playerFaceId;
                    if (run._bubbles != null)
                        for (int i = 0; i < run._bubbles.Count; i++)
                        {
                            var id = run._bubbles[i]?._bubbleId;
                            if (!string.IsNullOrEmpty(id) && !v.Played.Contains(id)) v.Played.Add(id);
                        }
                }
                if (!v.Started) return;
                var d = Current(ctx);
                v.FromStart = d.AtStart != null;
                var now = new HashSet<string>(TagsOf(ctx));
                if (d.AtStart != null)
                {
                    var start = new HashSet<string>(d.AtStart);
                    v.Gained = now.Where(t => !start.Contains(t)).ToList();
                    v.Lost = d.AtStart.Where(t => !now.Contains(t)).Distinct().ToList();
                }
                else
                {
                    v.Gained = d.Gained.Where(now.Contains).ToList();
                    v.Lost = d.Lost.Where(t => !now.Contains(t)).ToList();
                }
                // a finished scenario leaves its id as a fact, count those even if we missed them
                foreach (var p in v.Played) if (now.Contains(p) && !v.Gained.Contains(p)) v.Gained.Add(p);
            });
            return v;
        }
    }
}
