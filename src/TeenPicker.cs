using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Make your own teens (face, icon, money, energy, distance, age and the other card details) and
    // choose which of them show up on the three teen cards at the start of a trip.
    internal static class TeenPicker
    {
        static bool _patched;

        // hook the moment the player really picks a card, so backing out of the screen changes nothing
        public static void Patch(Harmony h)
        {
            try
            {
                h.Patch(AccessTools.Method(typeof(CharacterSelectionMenu), nameof(CharacterSelectionMenu.OnCharaSelected)),
                    postfix: new HarmonyMethod(typeof(TeenPicker), nameof(SelectedPostfix)));
                _patched = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Teen picker hook failed, using the fallback: " + e.Message); }
        }

        static void SelectedPostfix(CharacterSelectionMenu __instance)
        {
            try
            {
                if (!D.Enabled) return;
                int sel = __instance._currentSelection;
                IntPtr cardCtx = IntPtr.Zero;
                try { var ui = __instance._uis[sel]; if (ui?._associatedContext != null) cardCtx = ui._associatedContext.Pointer; } catch { }
                OnPicked(sel, cardCtx);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Teen picker pick: " + e.Message); }
        }

        public class Preset
        {
            public string Name { get; set; } = "New teen";
            public int Face { get; set; } = -1;          // -1 the game's pick
            public int Money { get; set; } = -1;
            public float Energy { get; set; } = -1;
            public int Distance { get; set; } = -1;
            public int Age { get; set; } = -1;
            public int MissingDays { get; set; } = -1;
            public string LastSeen { get; set; } = "";
            public string Icon { get; set; } = "";       // png or jpg in the icons folder, empty for the teen's own
        }

        public class Data
        {
            public bool Enabled { get; set; } = true;
            public List<Preset> Presets { get; set; } = new List<Preset>();
            public List<string> Cards { get; set; } = new List<string> { "", "", "" };
        }

        static Data _data;
        static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "teens.json");
        static bool _dirty;
        static float _saveAt;

        static Data D
        {
            get
            {
                if (_data != null) return _data;
                try { if (File.Exists(FilePath)) _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)); } catch (Exception e) { Plugin.Log.LogWarning("teens.json: " + e.Message); }
                _data ??= new Data();
                while (_data.Cards.Count < 3) _data.Cards.Add("");
                return _data;
            }
        }

        static void Changed() { _dirty = true; _saveAt = Time.unscaledTime + 1f; }

        static void SaveNow()
        {
            _dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(D, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Saving teens: " + e.Message); }
        }

        static Preset ByName(string name) => string.IsNullOrEmpty(name) ? null : D.Presets.FirstOrDefault(p => p.Name == name);

        // ---------- game side ----------

        static NB.NarrativeSettings Settings()
        {
            var s = Game.Settings;
            if (s != null) return s;
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<NB.NarrativeSettings>()))
                {
                    var x = o.TryCast<NB.NarrativeSettings>();
                    if (x != null) return x;
                }
            }
            catch { }
            return null;
        }

        static CharacterSelectionMenu _menu;
        static bool _wasVisible, _appliedThisShow;
        static float _visibleSince, _nextPoll;
        static int _lastSel = -1;
        static Preset _pending;
        static float _pendingUntil;
        static IntPtr _pendingCard, _pendingOld;
        static bool _pendingOldStarted;
        static Vector2Int? _ageOriginal;
        static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        public static bool MenuVisible { get { try { return _menu != null && _menu._visible; } catch { return false; } } }

        static Sprite IconSprite(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_icons.TryGetValue(file, out var sp) && sp != null) return sp;
            sp = null;
            try
            {
                var path = Path.Combine(IconSwapper.Folder, file);
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return null;
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.name = Path.GetFileNameWithoutExtension(file);
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sp.hideFlags = HideFlags.HideAndDontSave;
                sp.name = tex.name;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Teen icon: " + e.Message); }
            _icons[file] = sp;
            return sp;
        }

        static void SetFace(NB.NarrativeContext ctx, int id)
        {
            ctx._currentPlayerFace = id;
            var g = TeenTab.GenderOf(id);
            var run = ctx.CurrentRun;
            if (run != null)
            {
                run._playerFaceId = id;
                if (g != null) run._playerGender = g.Value;
            }
        }

        // put a preset's numbers into a trip
        static void ApplyStats(NB.NarrativeContext ctx, Preset p, bool started)
        {
            var settings = Settings();
            if (p.Face >= 0) SetFace(ctx, p.Face);
            if (p.Money >= 0) { ctx._currentMoney = p.Money; ctx._previousMoney = p.Money; }
            if (p.Energy >= 0)
            {
                float e = p.Energy;
                if (settings != null) { try { e = Mathf.Min(e, ctx.MaxEnergy(settings)); } catch { } }
                ctx._currentEnergy = e; ctx._previousEnergy = e;
            }
            if (p.Distance >= 0)
            {
                ctx._distanceRemaining = p.Distance;
                ctx._previousDistanceRemaining = p.Distance;
                var run = ctx.CurrentRun;
                if (run != null && (started || run._distanceStart < p.Distance)) run._distanceStart = p.Distance;
            }
            if (started)
            {
                // tell the HUD about the new numbers
                try { ctx._onMoneyAmountChanged?.Invoke(ctx._currentMoney); } catch { }
                try { ctx._onDistanceRemainingChanged?.Invoke(ctx._distanceRemaining); } catch { }
            }
        }

        static string ReplaceNumber(string text, int value)
        {
            if (string.IsNullOrEmpty(text)) return value.ToString();
            var rx = new Regex(@"\d+");
            return rx.IsMatch(text) ? rx.Replace(text, value.ToString(), 1) : text + " " + value;
        }

        static void ApplyCards()
        {
            var uis = _menu._uis;
            if (uis == null) return;
            int changed = 0;
            for (int i = 0; i < uis.Length && i < D.Cards.Count; i++)
            {
                var p = ByName(D.Cards[i]);
                var ui = uis[i];
                if (p == null || ui == null) continue;
                var ctx = ui._associatedContext;
                if (ctx == null) continue;
                Game.Try("Teen card " + (i + 1), () =>
                {
                    ApplyStats(ctx, p, false);
                    ui.SetContext(ctx);
                    if (p.Age >= 0 && ui._age != null) ui._age.text = ReplaceNumber(ui._age.text, p.Age);
                    if (p.MissingDays >= 0 && ui._missingDays != null) ui._missingDays.text = ReplaceNumber(ui._missingDays.text, p.MissingDays);
                    if (!string.IsNullOrWhiteSpace(p.LastSeen) && ui._lastSeen != null)
                    {
                        var t = ui._lastSeen.text ?? "";
                        int colon = t.IndexOf(':');
                        ui._lastSeen.text = colon >= 0 ? t.Substring(0, colon + 1) + " " + p.LastSeen.Trim() : p.LastSeen.Trim();
                    }
                    var icon = IconSprite(p.Icon);
                    if (icon != null && ui._portrait != null) { ui._portrait.sprite = icon; ui._portrait.preserveAspect = true; }
                    changed++;
                });
            }
            if (changed > 0) ToolkitBehaviour.Toast($"Teen picker: {changed} card(s) are your teens");
        }

        // the card art can be redrawn by the game's hover animation, so keep custom icons on
        static void KeepIcons()
        {
            var uis = _menu._uis;
            if (uis == null) return;
            for (int i = 0; i < uis.Length && i < D.Cards.Count; i++)
            {
                var p = ByName(D.Cards[i]);
                var icon = p != null ? IconSprite(p.Icon) : null;
                var ui = uis[i];
                if (icon == null || ui?._portrait == null) continue;
                if (ui._portrait.sprite == null || ui._portrait.sprite.Pointer != icon.Pointer) ui._portrait.sprite = icon;
            }
        }

        static CharacterSelectionUISettings CardSettings()
        {
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<CharacterSelectionUISettings>()))
                {
                    var s = o.TryCast<CharacterSelectionUISettings>();
                    if (s != null) return s;
                }
            }
            catch { }
            return null;
        }

        static void OnPicked(int index, IntPtr cardCtx)
        {
            var p = index >= 0 && index < D.Cards.Count ? ByName(D.Cards[index]) : null;
            // the age only lives on the cards and loading screens, so pin it for this trip
            var cs = CardSettings();
            if (cs != null)
            {
                Game.Try("Teen age", () =>
                {
                    if (p != null && p.Age >= 0) { _ageOriginal ??= cs._age; cs._age = new Vector2Int(p.Age, p.Age); }
                    else if (_ageOriginal != null) { cs._age = _ageOriginal.Value; _ageOriginal = null; }
                });
            }
            if (p == null) { IconSwapper.ClearPending(); return; }
            _pending = p;
            _pendingUntil = Time.unscaledTime + 600f;
            _pendingCard = cardCtx;
            var now = Game.Context;
            _pendingOld = now != null ? now.Pointer : IntPtr.Zero;
            _pendingOldStarted = false;
            try { _pendingOldStarted = now != null && now._runStarted; } catch { }
            var icon = IconSprite(p.Icon);
            if (icon != null) IconSwapper.UseSprite(icon, p.Icon);
            else IconSwapper.ClearPending();
            Plugin.Log.LogInfo("Teen picker: card " + (index + 1) + " is " + p.Name);
        }

        public static void Update()
        {
            if (_dirty && Time.unscaledTime > _saveAt) SaveNow();

            // after picking, put the stats on the new trip once it has started (the game may reset some of them)
            if (_pending != null)
            {
                if (Time.unscaledTime > _pendingUntil) _pending = null;
                else
                {
                    var ctx = Game.Context;
                    bool started = false;
                    try { started = ctx != null && ctx._runStarted && ctx.CurrentRun != null; } catch { }
                    // only the trip that came from the card: the card's own data, a fresh one, or one that started after the pick
                    bool ours = ctx != null && (ctx.Pointer == _pendingCard || ctx.Pointer != _pendingOld || !_pendingOldStarted);
                    if (started && ours)
                    {
                        var p = _pending;
                        _pending = null;
                        Game.Try("Teen picker stats", () => ApplyStats(ctx, p, true));
                        ToolkitBehaviour.Toast("Playing as " + p.Name);
                    }
                }
            }

            if (!D.Enabled || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.2f;
            try
            {
                if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<CharacterSelectionMenu>();
                bool vis = MenuVisible;
                if (vis && !_wasVisible) { _visibleSince = Time.unscaledTime; _appliedThisShow = false; }
                if (vis)
                {
                    try { _lastSel = _menu._currentSelection; } catch { }
                    // the game fills the cards as the menu opens, so wait a moment before changing them
                    if (!_appliedThisShow && Time.unscaledTime - _visibleSince > 0.4f) { _appliedThisShow = true; ApplyCards(); }
                    else if (_appliedThisShow) KeepIcons();
                }
                // without the hook, guess the pick from the screen closing
                if (!_patched && !vis && _wasVisible && _appliedThisShow)
                {
                    IntPtr card = IntPtr.Zero;
                    try { card = _menu._uis[_lastSel]._associatedContext.Pointer; } catch { }
                    OnPicked(_lastSel, card);
                }
                _wasVisible = vis;
            }
            catch { _menu = null; }
        }

        // ---------- menu ----------

        static int _page;
        static Preset _sel;
        static List<string> _iconFiles;

        static List<string> IconFiles()
        {
            if (_iconFiles != null) return _iconFiles;
            _iconFiles = new List<string>();
            try
            {
                Directory.CreateDirectory(IconSwapper.Folder);
                foreach (var f in Directory.GetFiles(IconSwapper.Folder).OrderBy(x => x))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") _iconFiles.Add(Path.GetFileName(f));
                }
            }
            catch { }
            return _iconFiles;
        }

        static int Step(int v, int delta, int min, int start)
        {
            if (v < 0) return delta > 0 ? start : -1;
            v += delta;
            return v < min ? -1 : v;
        }

        static void NumberRow(string label, int value, string unit, int small, int big, int min, int start, Action<int> set)
        {
            var r = Gui.Row(2.2f, 0.7f, 0.7f, 0.7f, 0.7f, 1f);
            GUI.Label(r[0], label + ": " + (value < 0 ? "game's pick" : value + unit));
            if (GUI.Button(r[1], "-" + big)) set(Step(value, -big, min, start));
            if (GUI.Button(r[2], "-" + small)) set(Step(value, -small, min, start));
            if (GUI.Button(r[3], "+" + small)) set(Step(value, small, min, start));
            if (GUI.Button(r[4], "+" + big)) set(Step(value, big, min, start));
            if (GUI.Button(r[5], "Game")) set(-1);
        }

        public static void Draw()
        {
            var d = D;
            if (Gui.Toggle("Put my teens on the teen cards when a new trip starts", d.Enabled)) { d.Enabled = !d.Enabled; Changed(); }
            Gui.Label(MenuVisible ? "The teen cards are on screen now." : "Cards change when the teen select screen opens (after Sonya's talk).");

            Gui.Header("Who is on the cards");
            var names = new List<string> { "" };
            names.AddRange(d.Presets.Select(p => p.Name));
            for (int i = 0; i < 3; i++)
            {
                int at = Math.Max(0, names.IndexOf(d.Cards[i]));
                if (at == 0) d.Cards[i] = "";
                var r = Gui.Row(1.2f, 0.5f, 3f, 0.5f);
                GUI.Label(r[0], "Card " + (i + 1));
                if (GUI.Button(r[1], "<")) { d.Cards[i] = names[(at - 1 + names.Count) % names.Count]; Changed(); }
                GUI.Label(r[2], at == 0 ? "  the game's random teen" : "  " + names[at]);
                if (GUI.Button(r[3], ">")) { d.Cards[i] = names[(at + 1) % names.Count]; Changed(); }
            }
            if (MenuVisible && Gui.Button("Put them on the cards now")) Game.Try("Teen cards", ApplyCards);

            var h = Gui.Row(2f, 1f, 1f);
            GUI.Label(h[0], $"My teens ({d.Presets.Count})");
            if (GUI.Button(h[1], "New teen"))
            {
                var p = new Preset { Name = "Teen " + (d.Presets.Count + 1) };
                while (d.Presets.Any(x => x.Name == p.Name)) p.Name += "+";
                d.Presets.Add(p); _sel = p; Changed();
            }
            if (GUI.Button(h[2], "Delete") && _sel != null)
            {
                for (int i = 0; i < d.Cards.Count; i++) if (d.Cards[i] == _sel.Name) d.Cards[i] = "";
                d.Presets.Remove(_sel); _sel = null; Changed();
            }
            int pick = Gui.List(d.Presets, ref _page, p => p.Name + (d.Cards.Contains(p.Name) ? "   (on card " + (d.Cards.IndexOf(p.Name) + 1) + ")" : ""), 3, p => p == _sel);
            if (pick >= 0) _sel = d.Presets[pick];
            if (_sel == null) { Gui.Label("Make a teen, then pick it for a card above."); return; }

            var s = _sel;
            Gui.Header("Edit " + s.Name);
            var nr = Gui.Row(1f, 3f);
            GUI.Label(nr[0], "Name");
            var newName = Gui.TextField(nr[1], s.Name);
            if (newName != s.Name && newName.Trim().Length > 0 && !d.Presets.Any(x => x != s && x.Name == newName))
            {
                for (int i = 0; i < d.Cards.Count; i++) if (d.Cards[i] == s.Name) d.Cards[i] = newName;
                s.Name = newName; Changed();
            }

            // face with a small portrait
            int faces = TeenTab.Faces(Settings());
            var fr = Gui.Row(0.8f, 2.2f, 0.7f, 0.7f, 1f);
            if (s.Face >= 0) TeenTab.DrawPortrait(new Rect(fr[0].x, fr[0].y - 2, fr[0].width, Gui.LineH + 4), s.Face);
            var g = s.Face >= 0 ? TeenTab.GenderOf(s.Face) : null;
            GUI.Label(fr[1], "Teen: " + (s.Face < 0 ? "game's pick" : "#" + s.Face + (g != null ? (g == NB.Gender.Male ? "  male" : "  female") : "")));
            if (GUI.Button(fr[2], "<") && faces > 0) { s.Face = s.Face <= 0 ? faces - 1 : s.Face - 1; Changed(); }
            if (GUI.Button(fr[3], ">") && faces > 0) { s.Face = (s.Face + 1) % faces; Changed(); }
            if (GUI.Button(fr[4], "Game")) { s.Face = -1; Changed(); }
            if (faces == 0) Gui.Label("   (the teen list loads with a trip or the main menu)");

            NumberRow("Money", s.Money, "$", 10, 100, 0, 50, v => { s.Money = v; Changed(); });
            var er = Gui.Row(2.2f, 0.7f, 0.7f, 1.4f, 1f);
            GUI.Label(er[0], "Energy: " + (s.Energy < 0 ? "game's pick" : s.Energy.ToString("0")));
            if (GUI.Button(er[1], "-")) { s.Energy = s.Energy <= 0 ? -1 : s.Energy - 1; Changed(); }
            if (GUI.Button(er[2], "+")) { s.Energy = s.Energy < 0 ? 3 : Mathf.Min(10, s.Energy + 1); Changed(); }
            if (GUI.Button(er[3], "Full")) { s.Energy = 10; Changed(); }
            if (GUI.Button(er[4], "Game")) { s.Energy = -1; Changed(); }
            NumberRow("Distance", s.Distance, " km", 100, 1000, 0, 4000, v => { s.Distance = v; Changed(); });
            NumberRow("Age", s.Age, "", 1, 5, 1, 16, v => { s.Age = v; Changed(); });
            NumberRow("Missing for", s.MissingDays, " days", 1, 10, 0, 7, v => { s.MissingDays = v; Changed(); });

            var lr = Gui.Row(1f, 3f);
            GUI.Label(lr[0], "Last seen");
            var ls = Gui.TextField(lr[1], s.LastSeen ?? "");
            if (ls != s.LastSeen) { s.LastSeen = ls; Changed(); }

            var files = IconFiles();
            int ii = string.IsNullOrEmpty(s.Icon) ? -1 : files.IndexOf(s.Icon);
            var ir = Gui.Row(0.8f, 2.2f, 0.7f, 0.7f, 1f);
            var isp = IconSprite(s.Icon);
            if (isp != null) GUI.DrawTexture(new Rect(ir[0].x, ir[0].y - 2, ir[0].width, Gui.LineH + 4), isp.texture, ScaleMode.ScaleToFit);
            GUI.Label(ir[1], "Icon: " + (ii < 0 ? "the teen's own" : s.Icon));
            if (GUI.Button(ir[2], "<") && files.Count > 0) { s.Icon = ii <= 0 ? files[files.Count - 1] : files[ii - 1]; Changed(); }
            if (GUI.Button(ir[3], ">") && files.Count > 0) { s.Icon = files[(ii + 1) % files.Count]; Changed(); }
            if (GUI.Button(ir[4], "Own")) { s.Icon = ""; Changed(); }
            var br = Gui.Row(2);
            if (GUI.Button(br[0], "Open icons folder")) { Directory.CreateDirectory(IconSwapper.Folder); Application.OpenURL("file:///" + IconSwapper.Folder.Replace('\\', '/')); }
            if (GUI.Button(br[1], "Reload icons")) { _iconFiles = null; _icons.Clear(); }
        }
    }
}
