using System;
using System.Collections.Generic;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Pick which teen you play as (face and gender).
    internal static class TeenTab
    {
        static int _genderFilter = -1; // -1 all, 0 male, 1 female
        static int _page;

        public static void Draw()
        {
            var ctx = Game.Context;
            var settings = Game.Settings;
            if (ctx == null || settings == null)
            {
                Gui.Label("Load into the game first.");
                return;
            }

            var faces = settings._playerFaces;
            if (faces == null || faces.Length == 0)
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

            Gui.Label($"You are teen #{current}   gender: {gender}   ({faces.Length} teens in the game)");
            Gui.Label("The change shows on the trip map, wanted posters and loading screens.");
            Gui.Space();

            Gui.Header("Gender");
            var g = Gui.Row(3);
            if (GUI.Button(g[0], "Make me male")) SetGender(ctx, NB.Gender.Male, faces);
            if (GUI.Button(g[1], "Make me female")) SetGender(ctx, NB.Gender.Female, faces);
            if (GUI.Button(g[2], "Random teen")) Game.Try("Random teen", () =>
            {
                var exclude = new Il2CppSystem.Collections.Generic.List<int>();
                int id = ctx.RandomizeCurrentPlayerFace(exclude, settings);
                Apply(ctx, id, faces);
            });

            Gui.Space();
            Gui.Header("All teens");
            var f = Gui.Row(3);
            string[] filters = { "Show all", "Only male", "Only female" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(f[i], (_genderFilter == i - 1 ? "> " : "") + filters[i])) { _genderFilter = i - 1; _page = 0; }

            var list = new List<int>();
            for (int i = 0; i < faces.Length; i++)
            {
                if (_genderFilter < 0) { list.Add(i); continue; }
                bool male = false;
                Game.Try("Read face", () => male = faces[i]._gender == NB.Gender.Male);
                if (male == (_genderFilter == 0)) list.Add(i);
            }

            // grid of portraits, 4 per row
            const int perRow = 4, rows = 3, perPage = perRow * rows;
            int pages = Math.Max(1, (list.Count + perPage - 1) / perPage);
            if (_page >= pages) _page = pages - 1;
            float cell = (Gui.W - Gui.Gap * (perRow - 1)) / perRow;
            float imgH = 90;
            for (int rIdx = 0; rIdx < rows; rIdx++)
            {
                for (int c = 0; c < perRow; c++)
                {
                    int li = _page * perPage + rIdx * perRow + c;
                    if (li >= list.Count) break;
                    int id = list[li];
                    var rect = new Rect(Gui.X + c * (cell + Gui.Gap), Gui.Y, cell, imgH + Gui.LineH);
                    DrawFace(rect, faces[id], id, id == current);
                    if (GUI.Button(new Rect(rect.x, rect.y + imgH, rect.width, Gui.LineH), id == current ? "Playing" : "Play as #" + id))
                        Apply(ctx, id, faces);
                }
                Gui.Y += imgH + Gui.LineH + Gui.Gap;
            }

            var nav = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && _page > 0) _page--;
            GUI.Label(nav[1], $"  page {_page + 1} / {pages}");
            if (GUI.Button(nav[2], "Next >") && _page < pages - 1) _page++;
        }

        static void DrawFace(Rect rect, NB.PlayerFace face, int id, bool current)
        {
            GUI.Box(new Rect(rect.x, rect.y, rect.width, 90), current ? "YOU" : "");
            Game.Try("Draw face", () =>
            {
                var sp = face._icon;
                if (sp == null) return;
                var tex = sp.texture;
                if (tex == null) return;
                var tr = sp.textureRect;
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                float size = Math.Min(rect.width, 86);
                var img = new Rect(rect.x + (rect.width - size) / 2f, rect.y + 2, size, 86);
                GUI.DrawTextureWithTexCoords(img, tex, uv);
            });
            string gender = "";
            Game.Try("Face gender", () => gender = face._gender == NB.Gender.Male ? "M" : "F");
            GUI.Label(new Rect(rect.x + 3, rect.y + 2, 40, 20), gender);
        }

        static void SetGender(NB.NarrativeContext ctx, NB.Gender gender, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NB.PlayerFace> faces)
        {
            // pick a random face that matches, so the portrait agrees with the gender
            var pool = new List<int>();
            for (int i = 0; i < faces.Length; i++)
                if (faces[i] != null && faces[i]._gender == gender) pool.Add(i);
            if (pool.Count == 0)
            {
                Game.Try("Set gender", () => { var run = ctx.CurrentRun; if (run != null) run._playerGender = gender; });
                return;
            }
            Apply(ctx, pool[UnityEngine.Random.Range(0, pool.Count)], faces);
        }

        static void Apply(NB.NarrativeContext ctx, int id, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NB.PlayerFace> faces)
        {
            if (id < 0 || id >= faces.Length) return;
            if (Game.Try("Set teen", () =>
            {
                ctx._currentPlayerFace = id;
                var run = ctx.CurrentRun;
                if (run != null)
                {
                    run._playerFaceId = id;
                    run._playerGender = faces[id]._gender;
                }
            }))
                ToolkitBehaviour.Toast("Now playing as teen #" + id);
        }
    }
}
