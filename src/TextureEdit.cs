using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // Paint the game's textures and put them back on characters and objects while the game runs.
    //
    // Save a texture of the selected object as a PNG, paint it in any image program, and load it back.
    // It can go on this one object only (it gets its own copy of the material, nobody else changes)
    // or everywhere the material is used. While a PNG is loaded the toolkit watches the file, so every
    // save in your paint program shows up in the game a second later. Every swap is remembered and put
    // back when the level loads again, until you reset it.
    internal static class TextureEdit
    {
        public class Swap
        {
            public bool Everywhere { get; set; }
            public string Scene { get; set; }
            public string Path { get; set; }
            public string Material { get; set; } = "";
            public string Slot { get; set; } = "";
            public string File { get; set; }
            public string Color { get; set; }
        }

        public static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "textures");
        static string SaveFile => Path.Combine(Folder, "swaps.json");

        static List<Swap> _swaps = new List<Swap>();
        static bool _loadedList;
        static bool _everywhere;

        // ---------- files ----------

        class Pic { public Texture2D Tex; public DateTime Time; public bool Linear; }
        static readonly Dictionary<string, Pic> _pics = new Dictionary<string, Pic>(StringComparer.OrdinalIgnoreCase);

        static bool IsLinear(string slot)
        {
            foreach (var k in new[] { "Normal", "Mask", "Bent", "Detail", "Height", "Thickness", "Anisotropy", "Tangent", "Smoothness", "Occlusion" })
                if (slot.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static Texture2D Load(string file, string slot, Texture like)
        {
            var full = Path.Combine(Folder, file);
            if (!System.IO.File.Exists(full)) return null;
            var time = System.IO.File.GetLastWriteTimeUtc(full);
            bool linear = IsLinear(slot);
            if (_pics.TryGetValue(file + "|" + linear, out var p) && p.Tex != null)
            {
                if (p.Time != time) Reload(p, full, time);
                return p.Tex;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(full))) return null;
            tex.name = Path.GetFileNameWithoutExtension(file);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (like != null)
            {
                try { tex.wrapMode = like.wrapMode; tex.filterMode = like.filterMode; tex.anisoLevel = like.anisoLevel; } catch { }
            }
            _pics[file + "|" + linear] = new Pic { Tex = tex, Time = time, Linear = linear };
            return tex;
        }

        // loading into the same texture updates everything that already uses it
        static void Reload(Pic p, string full, DateTime time)
        {
            p.Time = time;
            try
            {
                if (ImageConversion.LoadImage(p.Tex, System.IO.File.ReadAllBytes(full)))
                    ToolkitBehaviour.Toast(Path.GetFileName(full) + " updated");
            }
            catch (IOException) { p.Time = default; } // still being written, try again next time
        }

        static void SaveList()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                System.IO.File.WriteAllText(SaveFile, JsonSerializer.Serialize(_swaps, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Texture swaps save: " + e.Message); }
        }

        static void ReadList()
        {
            _loadedList = true;
            try
            {
                if (System.IO.File.Exists(SaveFile)) _swaps = JsonSerializer.Deserialize<List<Swap>>(System.IO.File.ReadAllText(SaveFile)) ?? new List<Swap>();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Texture swaps read: " + e.Message); }
        }

        // ---------- applying ----------

        const string Mine = " (toolkit)";
        static string BaseName(Material m) => m.name.EndsWith(Mine) ? m.name.Substring(0, m.name.Length - Mine.Length) : m.name;

        // what a slot held before we touched it, to put it back
        static readonly Dictionary<string, Texture> _origTex = new Dictionary<string, Texture>();
        static readonly Dictionary<string, Color> _origCol = new Dictionary<string, Color>();
        static string Key(Material m, string slot) => m.Pointer.ToInt64() + "|" + slot;

        static readonly string[] ColorProps = { "_BaseColor", "_Color", "_UnlitColor" };

        static void Set(Material m, Swap s, Texture2D tex)
        {
            if (s.Color != null)
            {
                foreach (var c in ColorProps)
                {
                    if (!m.HasProperty(c)) continue;
                    var k = Key(m, c);
                    if (!_origCol.ContainsKey(k)) _origCol[k] = m.GetColor(c);
                    if (ColorUtility.TryParseHtmlString("#" + s.Color, out var col)) m.SetColor(c, col);
                    break;
                }
                return;
            }
            if (tex == null || !m.HasProperty(s.Slot)) return;
            var key = Key(m, s.Slot);
            if (!_origTex.ContainsKey(key)) _origTex[key] = m.GetTexture(s.Slot);
            m.SetTexture(s.Slot, tex);
        }

        static void Unset(Material m, Swap s)
        {
            if (s.Color != null)
            {
                foreach (var c in ColorProps)
                    if (m.HasProperty(c) && _origCol.TryGetValue(Key(m, c), out var col)) { m.SetColor(c, col); break; }
                return;
            }
            if (_origTex.TryGetValue(Key(m, s.Slot), out var t)) m.SetTexture(s.Slot, t);
        }

        // the materials of this object that the swap is about; for "this one only" they become our own copies
        static List<Material> Targets(Transform root, Swap s, bool makeCopies)
        {
            var res = new List<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || BaseName(m) != s.Material) continue;
                    if (makeCopies && !s.Everywhere && !m.name.EndsWith(Mine))
                    {
                        var copy = new Material(m) { name = m.name + Mine };
                        mats[i] = copy;
                        m = copy;
                        changed = true;
                    }
                    if (!res.Contains(m)) res.Add(m);
                }
                if (changed) r.sharedMaterials = mats;
            }
            return res;
        }

        static IEnumerable<Material> Everywhere(string name)
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                var m = o.TryCast<Material>();
                if (m != null && m.name == name) yield return m;
            }
        }

        static void Apply(Swap s, Transform root)
        {
            Texture like = null;
            Texture2D tex = null;
            if (s.Color == null)
            {
                var first = root != null ? Targets(root, s, false).FirstOrDefault() : Everywhere(s.Material).FirstOrDefault();
                if (first != null && first.HasProperty(s.Slot))
                {
                    var k = Key(first, s.Slot);
                    like = _origTex.TryGetValue(k, out var o) ? o : first.GetTexture(s.Slot);
                }
                tex = Load(s.File, s.Slot, like);
                if (tex == null) { ToolkitBehaviour.Toast("Can't load " + s.File); return; }
            }
            var list = s.Everywhere ? Everywhere(s.Material).ToList() : Targets(root, s, true);
            foreach (var m in list) Set(m, s, tex);
        }

        static Swap Find(Swap like) => _swaps.FirstOrDefault(x => x.Everywhere == like.Everywhere && x.Material == like.Material && x.Slot == like.Slot
            && (x.Everywhere || (x.Scene == like.Scene && x.Path == like.Path)) && (x.Color == null) == (like.Color == null));

        static void Add(Swap s, Transform root)
        {
            if (!_loadedList) ReadList();
            var old = Find(s);
            if (old != null) _swaps.Remove(old);
            _swaps.Add(s);
            SaveList();
            Apply(s, root);
            _done.Add(Done(s, root));
        }

        static void Reset(Swap s, Transform root)
        {
            var list = s.Everywhere ? Everywhere(s.Material).ToList() : root != null ? Targets(root, s, false) : new List<Material>();
            foreach (var m in list) Unset(m, s);
            _swaps.Remove(s);
            SaveList();
        }

        // ---------- keeping them ----------

        static readonly HashSet<string> _done = new HashSet<string>();
        static string Done(Swap s, Transform root) => (root != null ? root.Pointer.ToInt64().ToString() : "all") + "|" + s.Material + "|" + s.Slot + "|" + (s.Color ?? s.File);
        static float _nextLive, _nextKeep;
        static string _scenesSeen = "";
        static float _scenesChanged;

        public static void Update()
        {
            if (!_loadedList) ReadList();
            float now = Time.unscaledTime;
            if (now > _nextLive && _pics.Count > 0)
            {
                _nextLive = now + 1f;
                foreach (var kv in _pics.ToList())
                {
                    var file = kv.Key.Substring(0, kv.Key.LastIndexOf('|'));
                    var full = Path.Combine(Folder, file);
                    if (kv.Value.Tex == null || !System.IO.File.Exists(full)) continue;
                    var t = System.IO.File.GetLastWriteTimeUtc(full);
                    if (t != kv.Value.Time) Reload(kv.Value, full, t);
                }
            }
            if (_swaps.Count == 0 || now < _nextKeep) return;
            _nextKeep = now + 2f;

            var scenes = "";
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes += SceneManager.GetSceneAt(i).handle + ",";
            if (scenes != _scenesSeen) { _scenesSeen = scenes; _scenesChanged = now; _done.RemoveWhere(d => d.StartsWith("all|")); }
            bool settled = now - _scenesChanged > 4f;

            foreach (var s in _swaps.ToList())
            {
                try
                {
                    if (s.Everywhere)
                    {
                        // a level load can bring new copies of the material, check once it has settled
                        if (!settled) continue;
                        var d = Done(s, null);
                        if (_done.Contains(d)) continue;
                        _done.Add(d);
                        Apply(s, null);
                        continue;
                    }
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var sc = SceneManager.GetSceneAt(i);
                        if (!sc.isLoaded || sc.name != s.Scene) continue;
                        var root = SceneEditor.FindByPath(sc, s.Path);
                        if (root == null) continue;
                        var d = Done(s, root);
                        if (_done.Contains(d)) continue;
                        _done.Add(d);
                        Apply(s, root);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Texture swap " + s.Material + ": " + e.Message); }
            }
        }

        // ---------- the inspector tab ----------

        static string _pickFor;      // material|slot waiting for a file pick
        static List<string> _files = new List<string>();

        static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
        }

        static Swap Current(Transform t, Material m, string slot, bool color)
        {
            var like = new Swap { Everywhere = _everywhere, Scene = t.gameObject.scene.name, Path = SceneEditor.PathOf(t), Material = BaseName(m), Slot = slot, Color = color ? "" : null };
            return Find(like) ?? Find(new Swap { Everywhere = !_everywhere, Scene = like.Scene, Path = like.Path, Material = like.Material, Slot = slot, Color = like.Color });
        }

        static void LoadInto(Transform t, Material m, string slot, string file)
        {
            var s = new Swap { Everywhere = _everywhere, Scene = t.gameObject.scene.name, Path = SceneEditor.PathOf(t), Material = BaseName(m), Slot = slot, File = file };
            Game.Try("Load texture", () => Add(s, _everywhere ? null : t));
            ToolkitBehaviour.Toast($"{file} on {(_everywhere ? "everything using " + s.Material : t.name)}. Save the file again to see changes live");
        }

        public static void Draw(Transform t)
        {
            if (!_loadedList) ReadList();
            var r = Gui.Row(1f, 1f);
            if (Gui.Pick(r[0], "This one only", !_everywhere, Gui.TabKind.Choice)) _everywhere = false;
            if (Gui.Pick(r[1], "Everywhere", _everywhere, Gui.TabKind.Choice)) _everywhere = true;
            r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Open the textures folder")) OpenFolder();
            GUI.Label(r[1], $"  {_swaps.Count} swaps kept");
            Gui.Label("Save a picture, paint it, then Load. Saving it again updates the game live.");

            if (_pickFor != null) { DrawPicker(t); return; }

            var seen = new HashSet<IntPtr>();
            foreach (var rend in t.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in rend.sharedMaterials)
                {
                    if (m == null || !seen.Add(m.Pointer)) continue;
                    Gui.Header(BaseName(m) + (m.name.EndsWith(Mine) ? "  (own copy)" : ""));
                    string[] names;
                    try { names = m.GetTexturePropertyNames().ToArray(); } catch { names = new string[0]; }
                    int empty = 0;
                    foreach (var slot in names)
                    {
                        var tex = m.GetTexture(slot);
                        if (tex == null) { empty++; continue; }
                        DrawSlot(t, m, slot, tex);
                    }
                    DrawColor(t, m);
                    if (empty > 0) Gui.Label($"{empty} empty slots", 18);
                }
            }
        }

        static void DrawSlot(Transform t, Material m, string slot, Texture tex)
        {
            var box = new Rect(Gui.X, Gui.Y, 44, 44);
            GUI.Box(box, "");
            try { GUI.DrawTexture(new Rect(box.x + 2, box.y + 2, 40, 40), tex, ScaleMode.ScaleToFit); } catch { }
            float x = Gui.X + 48, w = Gui.W - 48;
            var cur = Current(t, m, slot, false);
            string file = ObjExport.Clean(_origTex.TryGetValue(Key(m, slot), out var orig) && orig != null ? orig.name : tex.name) + ".png";
            GUI.Label(new Rect(x, Gui.Y, w, 20), slot.TrimStart('_') + (cur != null ? "  [swapped" + (cur.Everywhere ? " everywhere]" : "]") : ""));
            GUI.Label(new Rect(x, Gui.Y + 18, w, 20), $"{(orig ?? tex).name}  {tex.width}x{tex.height}");
            Gui.Y += 46;
            var r = Gui.Row(1f, 1f, 1f, 1f);
            var full = Path.Combine(Folder, file);
            bool have = System.IO.File.Exists(full);
            if (GUI.Button(r[0], "Save PNG"))
            {
                if (have) ToolkitBehaviour.Toast(file + " is already in the folder, delete it first for a fresh copy");
                else
                {
                    var png = ObjExport.Png(orig ?? tex);
                    if (png != null) { Directory.CreateDirectory(Folder); System.IO.File.WriteAllBytes(full, png); ToolkitBehaviour.Toast("Saved " + file); }
                }
            }
            if (have && GUI.Button(r[1], "Paint it")) Application.OpenURL("file:///" + full.Replace('\\', '/'));
            if (GUI.Button(r[2], have ? "Load" : "Load...")) { if (have) LoadInto(t, m, slot, file); else { _pickFor = BaseName(m) + "|" + slot; _files = Files(); } }
            if (cur != null && GUI.Button(r[3], "Reset")) { Reset(cur, cur.Everywhere ? null : t); ToolkitBehaviour.Toast(slot + " back to normal"); }
            else if (cur == null && GUI.Button(r[3], "Other...")) { _pickFor = BaseName(m) + "|" + slot; _files = Files(); }
            Gui.Space(2);
        }

        static readonly (string name, string hex)[] Colors = { ("Black", "000000FF"), ("White", "FFFFFFFF"), ("Red", "FF2020FF"), ("Green", "20E040FF"), ("Blue", "2060FFFF") };

        static void DrawColor(Transform t, Material m)
        {
            if (!ColorProps.Any(m.HasProperty)) return;
            var cur = Current(t, m, "#color", true);
            var r = Gui.Row(1.3f, 1f, 1f, 1f, 1f, 1f, 1f);
            GUI.Label(r[0], "Tint");
            for (int i = 0; i < Colors.Length; i++)
                if (GUI.Button(r[i + 1], Colors[i].name))
                {
                    var s = new Swap { Everywhere = _everywhere, Scene = t.gameObject.scene.name, Path = SceneEditor.PathOf(t), Material = BaseName(m), Slot = "#color", Color = Colors[i].hex };
                    Game.Try("Tint", () => Add(s, _everywhere ? null : t));
                }
            if (cur != null && GUI.Button(r[6], "Reset")) Reset(cur, cur.Everywhere ? null : t);
        }

        static List<string> Files()
        {
            Directory.CreateDirectory(Folder);
            return Directory.GetFiles(Folder).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName).OrderBy(f => f).ToList();
        }

        static int _pickPage;

        static void DrawPicker(Transform t)
        {
            var parts = _pickFor.Split('|');
            Gui.Header("Pick a picture for " + parts[1].TrimStart('_'));
            if (Gui.Button("Cancel")) { _pickFor = null; return; }
            if (_files.Count == 0) { Gui.Label("No PNG or JPG in the textures folder yet."); return; }
            int hit = Gui.List(_files, ref _pickPage, f => f, 12);
            if (hit < 0) return;
            Material m = null;
            foreach (var rend in t.GetComponentsInChildren<Renderer>(true))
                foreach (var x in rend.sharedMaterials) if (x != null && BaseName(x) == parts[0]) { m = x; break; }
            if (m != null) LoadInto(t, m, parts[1], _files[hit]);
            _pickFor = null;
        }
    }
}
