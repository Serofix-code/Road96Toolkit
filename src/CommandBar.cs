using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlueEyes.Entities;
using UnityEngine;

namespace Road96Toolkit
{
    // Type a command instead of clicking through tabs. Opens with F9 (or whatever the config says).
    internal static class CommandBar
    {
        public static bool Open { get; private set; }
        static string _text = "";
        static readonly List<string> _history = new List<string>();
        static int _histPos = -1;
        static readonly List<string> _output = new List<string>();
        static bool _focus, _frozePlayer;

        static readonly (string cmd, string help)[] Help =
        {
            ("help", "this list"),
            ("money <n>", "give money (money -50 takes it)"),
            ("setmoney <n>", "set money to exactly n"),
            ("karma <-1..1>", "set karma"),
            ("energy full", "fill energy"),
            ("tp <name>", "go to a waypoint or a character"),
            ("wp <name>", "save a waypoint here"),
            ("mission <id>", "start a story mission, like SONYA_6"),
            ("queue <id>", "play this scenario next"),
            ("level <name>", "load a level on its own"),
            ("spawn <name>", "spawn a character from this level or the copied ones"),
            ("tapes", "give every cassette tape"),
            ("teen <number>", "play as another teen"),
            ("speed <x>", "game speed, 1 is normal"),
            ("snapshot [name]", "make a save snapshot"),
            ("freecam / noclip / photo / devview", "toggle those modes"),
            ("hints / autopick / esp", "toggle choice hints, auto pick or the item ESP"),
            ("odds win|fail|normal|<percent>", "force every dice roll"),
            ("fact <name>", "add or remove a story fact (fact add / fact remove to be sure)"),
            ("facts [search]", "list the facts you have, or every known one matching"),
            ("runfacts", "list the facts this teen got or lost on this trip"),
            ("clear", "clear this box"),
        };

        public static void Toggle()
        {
            Open = !Open;
            _text = "";
            _histPos = -1;
            _focus = Open;
            var p = Game.Player;
            if (Open && p != null && !Freecam.Active)
            {
                Game.Try("Freeze for typing", () => p.Freeze(true, PlayerBlueEyes.FreezeOrigin.Debug));
                _frozePlayer = true;
            }
            else if (!Open && _frozePlayer)
            {
                if (p != null) Game.Try("Unfreeze", () => p.Freeze(false, PlayerBlueEyes.FreezeOrigin.Debug));
                _frozePlayer = false;
            }
            if (Open && _output.Count == 0) Print("Type help for the list of commands. Enter runs, Up for history, " + Plugin.CommandKey + " closes.");
        }

        static void Print(string s)
        {
            _output.Add(s);
            while (_output.Count > 8) _output.RemoveAt(0);
        }

        public static void Draw(float scale)
        {
            if (!Open) return;
            float sw = Screen.width / scale;
            var box = new Rect(sw / 2f - 380, 30, 760, 34 + _output.Count * 20);
            GUI.Box(box, "");
            GUI.Box(box, "");
            float y = box.y + 4;
            foreach (var line in _output) { GUI.Label(new Rect(box.x + 10, y, box.width - 20, 20), line); y += 20; }

            var ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { Run(_text); _text = ""; ev.Use(); }
                else if (ev.keyCode == KeyCode.UpArrow && _history.Count > 0)
                {
                    _histPos = _histPos < 0 ? _history.Count - 1 : Math.Max(0, _histPos - 1);
                    _text = _history[_histPos]; ev.Use();
                }
                else if (ev.keyCode == KeyCode.DownArrow && _histPos >= 0)
                {
                    _histPos++;
                    if (_histPos >= _history.Count) { _histPos = -1; _text = ""; } else _text = _history[_histPos];
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.Escape) { Toggle(); ev.Use(); return; }
            }

            GUI.SetNextControlName("r96cmd");
            _text = Gui.TextField(new Rect(box.x + 8, box.yMax - 26, box.width - 16, 22), _text);
            if (_focus) { GUI.FocusControl("r96cmd"); _focus = false; }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Run(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0) return;
            _history.Add(line);
            _histPos = -1;
            Print("> " + line);
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1].Trim() : "";
            var ctx = Game.Context;
            try
            {
                switch (cmd)
                {
                    case "help": case "?":
                        Print(string.Join("   ", Help.Take(9).Select(h => h.cmd)));
                        Print(string.Join("   ", Help.Skip(9).Select(h => h.cmd)));
                        break;
                    case "clear": _output.Clear(); break;
                    case "money":
                        if (ctx == null) { Print("Load a trip first"); break; }
                        int m = (int)F(arg);
                        if (m >= 0) ctx.AddMoney(m); else ctx.RemoveMoney(-m);
                        Print($"Money is now ${ctx._currentMoney}");
                        break;
                    case "setmoney":
                        if (ctx == null) { Print("Load a trip first"); break; }
                        int target = (int)F(arg), diff = target - ctx._currentMoney;
                        if (diff > 0) ctx.AddMoney(diff); else if (diff < 0) ctx.RemoveMoney(-diff);
                        Print($"Money is now ${ctx._currentMoney}");
                        break;
                    case "karma":
                        ctx?.SetKarma(Mathf.Clamp(F(arg), -1f, 1f)); Print("Karma set"); break;
                    case "energy":
                        if (ctx != null && Game.Settings != null) { ctx.ImpactEnergy(10f, Game.Settings, true); Print("Energy filled"); }
                        break;
                    case "tp": case "goto":
                        if (TravelTab.GoToWaypoint(arg)) { Print("Went to waypoint " + arg); break; }
                        var npc = TravelTab.Characters().FirstOrDefault(n => n.gameObject.name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (npc != null) { TravelTab.GoToCharacter(npc); Print("Went to " + npc.gameObject.name); }
                        else Print("No waypoint or character called " + arg);
                        break;
                    case "wp": TravelTab.SaveHere(arg); Print("Waypoint saved"); break;
                    case "mission": case "queue":
                    {
                        var ctrl = Game.Controller;
                        var el = Game.Brain?._narrativeInput?.GetAllPossibleInputs()?.FirstOrDefault(e => e != null && e.ID.Equals(arg, StringComparison.OrdinalIgnoreCase));
                        if (ctrl == null || el == null) { Print("No scenario called " + arg); break; }
                        if (ctx != null) ctx._overrideNextBubble = el.ID;
                        ctrl._nextBubble = el;
                        if (cmd == "mission") { ctrl.GoToNextBubble(false, true); Print("Starting " + el.ID); }
                        else Print(el.ID + " plays next");
                        break;
                    }
                    case "level":
                    {
                        var lv = LevelsTab.Levels.FirstOrDefault(l => l.Name.Equals(arg, StringComparison.OrdinalIgnoreCase))
                                 ?? LevelsTab.Levels.FirstOrDefault(l => l.Name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (lv == null) { Print("No level called " + arg); break; }
                        LevelsTab.LoadLevel(lv.Data); Print("Loading " + lv.Name);
                        break;
                    }
                    case "spawn":
                    {
                        var src = Spawner.Gather(0).Concat(Spawner.Gather(1)).FirstOrDefault(s => s.Name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (src == null) { Print("Nobody called " + arg + " is loaded, copy them from their level first"); break; }
                        Spawner.SpawnObject(src.Go, src.Name); Print("Spawned " + src.Name);
                        break;
                    }
                    case "tapes":
                    {
                        if (ctx == null) { Print("Load a trip first"); break; }
                        int n = 0;
                        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Collectible>()))
                        {
                            var c = o.TryCast<Collectible>();
                            if (c != null && !ctx.HasItem(c)) { ctx.AddItem(c); n++; }
                        }
                        Print($"Gave {n} tapes");
                        break;
                    }
                    case "teen":
                        if (ctx == null) { Print("Load a trip first"); break; }
                        TeenTab.PlayAs(ctx, (int)F(arg)); Print("Teen set to #" + arg);
                        break;
                    case "speed":
                        Time.timeScale = Mathf.Clamp(F(arg), 0f, 10f);
                        if (Time.timeScale > 0f) WorldTab.FixSound(false);
                        Print("Speed " + Time.timeScale);
                        break;
                    case "snapshot": case "save":
                        SavesTab.SnapshotNow(arg); Print("Snapshot coming up"); break;
                    case "freecam": Freecam.Toggle(); break;
                    case "noclip": Noclip.Toggle(); break;
                    case "photo": Toggle(); PhotoMode.Toggle(); return;
                    case "devview": case "dev": Toggle(); DevView.Toggle(); return;
                    case "hints": Plugin.ChoiceHints.Value = !Plugin.ChoiceHints.Value; Print("Choice hints " + (Plugin.ChoiceHints.Value ? "on" : "off")); break;
                    case "odds": case "dice":
                    {
                        var a = arg.ToLowerInvariant();
                        if (a == "win") Plugin.OddsMode.Value = 1;
                        else if (a == "fail" || a == "lose") Plugin.OddsMode.Value = 2;
                        else if (a == "normal" || a == "off" || a == "") Plugin.OddsMode.Value = 0;
                        else { Plugin.OddsPercent.Value = Mathf.Clamp(F(a.TrimEnd('%')), 0f, 100f); Plugin.OddsMode.Value = 3; }
                        ChoiceHints.Reread();
                        Print("Dice rolls: " + ChoiceEditor.OddsModeText());
                        break;
                    }
                    case "fact":
                    {
                        if (ctx == null) { Print("Load a trip first"); break; }
                        var bits = arg.Split(new[] { ' ' }, 2);
                        var mode = bits[0].ToLowerInvariant();
                        string name = arg; bool? want = null;
                        if (bits.Length > 1 && (mode == "add" || mode == "on")) { want = true; name = bits[1]; }
                        else if (bits.Length > 1 && (mode == "remove" || mode == "off" || mode == "del")) { want = false; name = bits[1]; }
                        if (name.Length == 0) { Print("fact <name>"); break; }
                        var tag = FactsEditor.Find(name) ?? name.Trim();
                        bool on = want ?? !FactsEditor.Has(tag);
                        FactsEditor.Set(tag, on);
                        Print((on ? "Added " : "Removed ") + tag);
                        break;
                    }
                    case "facts":
                    {
                        if (ctx == null) { Print("Load a trip first"); break; }
                        var list = FactsEditor.Matching(arg).Where(t => arg.Length > 0 || FactsEditor.Has(t)).ToList();
                        foreach (var t in list.Take(25)) Print((FactsEditor.Has(t) ? "[x] " : "[  ] ") + t);
                        Print(list.Count > 25 ? $"{list.Count} facts, showing 25" : $"{list.Count} facts");
                        break;
                    }
                    case "runfacts":
                    {
                        if (ctx == null) { Print("Load a trip first"); break; }
                        var v = RunFacts.Read(ctx);
                        if (!v.Started) { Print("No trip is going on"); break; }
                        foreach (var t in v.Gained.Take(20)) Print("+ " + t);
                        foreach (var t in v.Lost.Take(10)) Print("- " + t);
                        Print($"{v.Gained.Count} gained, {v.Lost.Count} lost this trip" + (v.FromStart ? "" : " (watching since midway)"));
                        break;
                    }
                    case "esp": case "items": ItemEsp.Toggle(); Print("Item ESP " + (Plugin.ItemEsp.Value ? "on" : "off")); break;
                    case "autopick": Plugin.AutoPickBest.Value = !Plugin.AutoPickBest.Value; Print("Auto pick " + (Plugin.AutoPickBest.Value ? "on" : "off")); break;
                    default: Print("Unknown command, type help"); break;
                }
            }
            catch (FormatException) { Print("That needs a number"); }
            catch (Exception e) { Print("Failed: " + e.Message); }
        }
    }
}
