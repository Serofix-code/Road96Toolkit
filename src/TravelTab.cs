using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // Waypoints you can save and jump back to, and teleporting straight to any character.
    internal static class TravelTab
    {
        public class Waypoint
        {
            public string Scene { get; set; } = "";
            public string Name { get; set; } = "";
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float Yaw { get; set; }
        }

        static List<Waypoint> _points;
        static string _name = "";
        static bool _allLevels;
        static int _page, _npcPage;
        static Waypoint _sel;
        static readonly List<NPC> _npcs = new List<NPC>();
        static float _nextNpcScan;

        static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "waypoints.json");

        static List<Waypoint> Points
        {
            get
            {
                if (_points != null) return _points;
                try { if (File.Exists(FilePath)) _points = JsonSerializer.Deserialize<List<Waypoint>>(File.ReadAllText(FilePath)); } catch { }
                return _points ??= new List<Waypoint>();
            }
        }

        static void SavePoints()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Points, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Waypoints: " + e.Message); }
        }

        public static string SceneName { get { try { return SceneManager.GetActiveScene().name; } catch { return "?"; } } }

        // where "I" am: the free camera if it is flying, otherwise the teen
        static bool Here(out Vector3 pos, out float yaw)
        {
            if (Freecam.Active) { pos = Freecam.Position; yaw = Freecam.Rotation.eulerAngles.y; return true; }
            var p = Game.Player;
            if (p != null) { pos = p.transform.position; yaw = Game.MainCamera != null ? Game.MainCamera.transform.eulerAngles.y : p.transform.eulerAngles.y; return true; }
            pos = default; yaw = 0; return false;
        }

        public static void Go(Vector3 pos, float yaw)
        {
            if (Freecam.Active) { Freecam.MoveTo(pos + Vector3.up * 1.6f, yaw); return; }
            var p = Game.Player;
            if (p == null) { ToolkitBehaviour.Toast("No player here"); return; }
            Game.Try("Teleport", () => p.Teleport(pos, Quaternion.Euler(0, yaw, 0)));
        }

        public static bool GoToWaypoint(string name)
        {
            var w = Points.FirstOrDefault(x => x.Scene == SceneName && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? Points.FirstOrDefault(x => x.Scene == SceneName && x.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (w == null) return false;
            Go(new Vector3(w.X, w.Y, w.Z), w.Yaw);
            return true;
        }

        public static void SaveHere(string name)
        {
            if (!Here(out var pos, out var yaw)) { ToolkitBehaviour.Toast("Nowhere to save"); return; }
            if (Freecam.Active && Physics.Raycast(pos, Vector3.down, out var hit, 200f)) pos = hit.point + Vector3.up * 0.05f;
            name = string.IsNullOrWhiteSpace(name) ? "Spot " + (Points.Count(w => w.Scene == SceneName) + 1) : name.Trim();
            Points.RemoveAll(w => w.Scene == SceneName && w.Name == name);
            Points.Add(new Waypoint { Scene = SceneName, Name = name, X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw });
            SavePoints();
            ToolkitBehaviour.Toast("Saved " + name);
        }

        public static List<NPC> Characters()
        {
            if (Time.unscaledTime > _nextNpcScan)
            {
                _nextNpcScan = Time.unscaledTime + 2f;
                _npcs.Clear();
                Game.Try("Find characters", () =>
                {
                    foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
                        if (n != null && !n.gameObject.name.EndsWith("(toolkit)")) _npcs.Add(n);
                });
                if (Here(out var me, out _)) _npcs.Sort((a, b) => Vector3.Distance(a.transform.position, me).CompareTo(Vector3.Distance(b.transform.position, me)));
            }
            _npcs.RemoveAll(n => n == null);
            return _npcs;
        }

        public static void GoToCharacter(NPC n)
        {
            if (n == null) return;
            var t = n.transform;
            var spot = t.position + t.forward * 1.6f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 10f)) spot = hit.point;
            Go(spot, Quaternion.LookRotation(t.position - spot).eulerAngles.y);
            ToolkitBehaviour.Toast("Went to " + n.gameObject.name);
        }

        public static void Draw()
        {
            Gui.Header("Waypoints in " + SceneName);
            var r = Gui.Row(1f, 2.4f);
            GUI.Label(r[0], "Name");
            _name = Gui.TextField(r[1], _name);
            r = Gui.Row(2);
            if (GUI.Button(r[0], Freecam.Active ? "Save the camera spot" : "Save where I stand")) { SaveHere(_name); _name = ""; }
            if (GUI.Button(r[1], (_allLevels ? "[ON] " : "[off] ") + "Show other levels")) _allLevels = !_allLevels;

            var list = Points.Where(w => _allLevels || w.Scene == SceneName).ToList();
            int pick = Gui.List(list, ref _page, w => (w.Scene == SceneName ? "" : "[" + w.Scene + "]  ") + w.Name, 6, w => w == _sel);
            if (pick >= 0)
            {
                _sel = list[pick];
                if (_sel.Scene == SceneName) Go(new Vector3(_sel.X, _sel.Y, _sel.Z), _sel.Yaw);
                else ToolkitBehaviour.Toast("That waypoint is in " + _sel.Scene);
            }
            if (Gui.Button("Delete the selected waypoint") && _sel != null) { Points.Remove(_sel); _sel = null; SavePoints(); }

            Gui.Space();
            Gui.Header("Go to a character (closest first)");
            var npcs = Characters();
            Vector3 me = Vector3.zero;
            Here(out me, out _);
            int np = Gui.List(npcs, ref _npcPage, n => $"{n.gameObject.name}   {Vector3.Distance(n.transform.position, me):0} m", 12);
            if (np >= 0) GoToCharacter(npcs[np]);
            Gui.Label(Freecam.Active ? "The freecam is on, so the camera moves instead of your teen." : "Your teen is moved. Turn the freecam on to move only the camera.");
        }
    }
}
