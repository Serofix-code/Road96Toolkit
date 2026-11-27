using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Pick which teen you play as (face, gender and age).
    internal static class TeenTab
    {
        static int _section;
        static readonly string[] Sections = { "Teen and gender", "Age" };

        static int _genderFilter = -1; // -1 all, 0 male, 1 female
        static int _page;

        // portraits are cached because the face structs get collected between frames
        class FaceInfo
        {
            public Texture2D Tex;
            public Rect Uv;
            public NB.Gender Gender;
        }
        static readonly Dictionary<int, FaceInfo> _faces = new Dictionary<int, FaceInfo>();
        static IntPtr _facesFrom = IntPtr.Zero;

        public static void Draw()
        {
            var row = Gui.Row(2);
            for (int i = 0; i < Sections.Length; i++)
                if (GUI.Button(row[i], i == _section ? "[ " + Sections[i] + " ]" : Sections[i])) _section = i;
            Gui.Space(4);

            if (_section == 1) { AgeSection.Draw(); return; }

            var ctx = Game.Context;
            var settings = Game.Settings;
            if (ctx == null || settings == null)
            {
                Gui.Label("Load into the game first.");
                return;
            }

            int count = LoadFaces(settings);
            if (count == 0)
            {
                Gui.Label("No teen list found.");
                return;
            }

            int current = -1;
            string gender = "?";
            Game.Try("Read teen", () =>
            {
                current = ctx._currentPlayerFace;
                var run = ctx.CurrentRun;
                if (run != null) gender = run._playerGender.ToString();
            });

            Gui.Label($"You are teen #{current}   gender: {gender}   ({count} teens in the game)");
            Gui.Label("The change shows on the trip map, wanted posters and loading screens.");
            Gui.Space();

            Gui.Header("Gender");
            var g = Gui.Row(3);
            if (GUI.Button(g[0], "Make me male")) SetGender(ctx, NB.Gender.Male, count);
            if (GUI.Button(g[1], "Make me female")) SetGender(ctx, NB.Gender.Female, count);
            if (GUI.Button(g[2], "Random teen")) Apply(ctx, UnityEngine.Random.Range(0, count));

            Gui.Space();
            Gui.Header("All teens");
            var f = Gui.Row(3);
            string[] filters = { "Show all", "Only male", "Only female" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(f[i], (_genderFilter == i - 1 ? "> " : "") + filters[i])) { _genderFilter = i - 1; _page = 0; }

            var list = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (_genderFilter < 0) { list.Add(i); continue; }
                if (_faces.TryGetValue(i, out var fi) && (fi.Gender == NB.Gender.Male) == (_genderFilter == 0)) list.Add(i);
            }

            const int perRow = 4, rows = 3, perPage = perRow * rows;
            int pages = Math.Max(1, (list.Count + perPage - 1) / perPage);
            if (_page >= pages) _page = pages - 1;
            float cell = (Gui.W - Gui.Gap * (perRow - 1)) / perRow;
            const float imgH = 90;
            for (int rIdx = 0; rIdx < rows; rIdx++)
            {
                for (int c = 0; c < perRow; c++)
                {
                    int li = _page * perPage + rIdx * perRow + c;
                    if (li >= list.Count) break;
                    int id = list[li];
                    var rect = new Rect(Gui.X + c * (cell + Gui.Gap), Gui.Y, cell, imgH + Gui.LineH);
                    DrawFace(rect, id, id == current);
                    if (GUI.Button(new Rect(rect.x, rect.y + imgH, rect.width, Gui.LineH), id == current ? "Playing" : "Play as #" + id))
                        Apply(ctx, id);
                }
                Gui.Y += imgH + Gui.LineH + Gui.Gap;
            }

            var nav = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && _page > 0) _page--;
            GUI.Label(nav[1], $"  page {_page + 1} / {pages}");
            if (GUI.Button(nav[2], "Next >") && _page < pages - 1) _page++;
        }

        static int LoadFaces(NB.NarrativeSettings settings)
        {
            int count = 0;
            Game.Try("Read faces", () =>
            {
                var arr = settings._playerFaces;
                count = arr == null ? 0 : arr.Length;
                if (settings.Pointer == _facesFrom && _faces.Count == count) return;
                _faces.Clear();
                _facesFrom = settings.Pointer;
                for (int i = 0; i < count; i++)
                {
                    var face = settings.GetPlayerFaceById(i);
                    var info = new FaceInfo { Gender = face._gender };
                    var sp = face._icon;
                    if (sp != null && sp.texture != null)
                    {
                        var tex = sp.texture;
                        var tr = sp.textureRect;
                        info.Tex = tex;
                        info.Uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                    }
                    _faces[i] = info;
                }
            });
            return count;
        }

        static void DrawFace(Rect rect, int id, bool current)
        {
            GUI.Box(new Rect(rect.x, rect.y, rect.width, 90), current ? "YOU" : "");
            if (!_faces.TryGetValue(id, out var fi)) return;
            if (fi.Tex != null)
            {
                float size = Math.Min(rect.width, 86);
                var img = new Rect(rect.x + (rect.width - size) / 2f, rect.y + 2, size, 86);
                try
                {
                    if (fi.Tex == null) throw new NullReferenceException();
                    GUI.DrawTextureWithTexCoords(img, fi.Tex, fi.Uv);
                }
                catch
                {
                    // the game unloaded the portrait atlas on a scene change, grab them again next frame
                    _facesFrom = IntPtr.Zero;
                    fi.Tex = null;
                }
            }
            GUI.Label(new Rect(rect.x + 3, rect.y + 2, 40, 20), fi.Gender == NB.Gender.Male ? "M" : "F");
        }

        static void SetGender(NB.NarrativeContext ctx, NB.Gender gender, int count)
        {
            // pick a face that matches, so the portrait agrees with the gender
            var pool = new List<int>();
            for (int i = 0; i < count; i++)
                if (_faces.TryGetValue(i, out var fi) && fi.Gender == gender) pool.Add(i);
            if (pool.Count == 0)
            {
                Game.Try("Set gender", () => { var run = ctx.CurrentRun; if (run != null) run._playerGender = gender; });
                return;
            }
            Apply(ctx, pool[UnityEngine.Random.Range(0, pool.Count)]);
        }

        static void Apply(NB.NarrativeContext ctx, int id)
        {
            if (!_faces.TryGetValue(id, out var fi)) return;
            if (Game.Try("Set teen", () =>
            {
                ctx._currentPlayerFace = id;
                var run = ctx.CurrentRun;
                if (run != null)
                {
                    run._playerFaceId = id;
                    run._playerGender = fi.Gender;
                }
            }))
                ToolkitBehaviour.Toast("Now playing as teen #" + id);
        }
    }

    // The teen's age only lives on the teen cards (main menu and loading screens). The game rolls it
    // from a min/max range, so pinning the range picks the age.
    internal static class AgeSection
    {
        static Vector2Int? _original;
        static int _min = 15, _max = 15;
        static bool _synced;

        static CharacterSelectionUISettings FindSettings()
        {
            CharacterSelectionUISettings found = null;
            Game.Try("Find age settings", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<CharacterSelectionUISettings>()))
                {
                    var s = o.TryCast<CharacterSelectionUISettings>();
                    if (s != null) { found = s; break; }
                }
            });
            return found;
        }

        public static void Draw()
        {
            var s = FindSettings();
            if (s == null)
            {
                Gui.Label("The teen cards are not loaded yet.");
                Gui.Label("They load with the main menu, open this from there or after a trip loads.");
                return;
            }

            Vector2Int range = default;
            Game.Try("Read age", () => range = s._age);
            if (_original == null) _original = range;
            if (!_synced) { _min = range.x; _max = range.y; _synced = true; }

            Gui.Label(range.x == range.y ? $"Teen age is fixed at {range.x}" : $"Teen age is random from {range.x} to {range.y}");
            Gui.Label("Shows on the teen cards in the main menu and on the trip loading screens.");
            Gui.Space();

            Gui.Header("Age chooser");
            for (int start = 12; start <= 20; start += 5)
            {
                var r = Gui.Row(5);
                for (int i = 0; i < 5; i++)
                {
                    int age = start + i;
                    if (age > 21) break;
                    bool on = range.x == age && range.y == age;
                    if (GUI.Button(r[i], (on ? "> " : "") + age))
                        Set(s, age, age);
                }
            }

            Gui.Space();
            Gui.Header("Age setter");
            var a = Gui.Row(2f, 1f, 1f);
            GUI.Label(a[0], $"Exact age: {_min}");
            if (GUI.Button(a[1], "-")) { _min = Math.Max(1, _min - 1); _max = _min; }
            if (GUI.Button(a[2], "+")) { _min = Math.Min(99, _min + 1); _max = _min; }
            if (Gui.Button($"Set age to {_min}")) Set(s, _min, _min);

            Gui.Space();
            Gui.Label("Or let the game roll it between two ages:");
            var m = Gui.Row(2f, 1f, 1f);
            GUI.Label(m[0], $"Youngest: {_min}");
            if (GUI.Button(m[1], "-")) _min = Math.Max(1, _min - 1);
            if (GUI.Button(m[2], "+")) _min = Math.Min(_max, _min + 1);
            m = Gui.Row(2f, 1f, 1f);
            GUI.Label(m[0], $"Oldest: {_max}");
            if (GUI.Button(m[1], "-")) _max = Math.Max(_min, _max - 1);
            if (GUI.Button(m[2], "+")) _max = Math.Min(99, _max + 1);
            var b = Gui.Row(2);
            if (GUI.Button(b[0], $"Use range {_min} to {_max}")) Set(s, _min, _max);
            if (GUI.Button(b[1], "Back to the game's default") && _original != null)
            {
                var o = _original.Value;
                Set(s, o.x, o.y);
                _min = o.x; _max = o.y;
            }
        }

        static void Set(CharacterSelectionUISettings s, int min, int max)
        {
            if (Game.Try("Set age", () => s._age = new Vector2Int(min, max)))
                ToolkitBehaviour.Toast(min == max ? $"Age set to {min}" : $"Age range {min} to {max}");
            _min = min; _max = max;
            if (min == max) RefreshCards();
        }

        // If teen cards are on screen right now, redraw them so the new age shows straight away.
        static void RefreshCards()
        {
            Game.Try("Refresh cards", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMPro.TextMeshProUGUI>()))
                {
                    var t = o.TryCast<TMPro.TextMeshProUGUI>();
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    var n = t.gameObject.name;
                    if (n.IndexOf("age", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (n.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    // only rewrite labels that are just a number, so we never touch real text
                    var txt = t.text;
                    if (int.TryParse(txt?.Trim(), out _)) t.text = _min.ToString();
                }
            });
        }
    }
}
