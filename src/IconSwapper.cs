using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Road96Toolkit
{
    // Swap the teen head in the top left of the HUD for any other picture:
    // another teen's icon, any sprite the game has loaded, or your own PNG.
    internal static class IconSwapper
    {
        class Pic
        {
            public string Name;
            public Sprite Sprite;   // the game's own sprite, or one we made
            public Texture2D Thumb; // copy for drawing in the menu
            public string File;     // set for your own images
            public bool TriedThumb;
        }

        static int _source; // 0 teens, 1 game sprites, 2 my images
        static readonly List<Pic> _teens = new List<Pic>();
        static readonly List<Pic> _sprites = new List<Pic>();
        static readonly List<Pic> _mine = new List<Pic>();
        static List<Pic> _shown = new List<Pic>();
        static string _search = "", _lastSearch = null;
        static int _lastSource = -1, _page;
        static Pic _sel, _hover;

        static Sprite _swap;          // what we keep on the HUD
        static string _swapName;
        static Sprite _original;
        static bool _fromPicker;

        // used by the teen picker: keep this picture on the HUD as soon as one exists
        internal static void UseSprite(Sprite sp, string name)
        {
            _swap = sp; _swapName = name; _fromPicker = true; _nextCheck = 0f;
        }

        internal static void ClearPending()
        {
            if (_fromPicker) { _fromPicker = false; Restore(); }
        }
        static EnergyBarUI _bar;
        static float _nextCheck;

        public static string Folder => Path.Combine(Paths.ConfigPath, "Road96Toolkit", "icons");
        public static string ExportFolder => Path.Combine(Paths.ConfigPath, "Road96Toolkit", "exported", "icons");

        // saving a whole list goes a few pictures a frame so the game does not freeze
        static readonly Queue<Pic> _toSave = new Queue<Pic>();
        static int _saved, _saveTotal;

        static bool SavePic(Pic p)
        {
            var sp = p.Sprite;
            if (sp == null) return false;
            try
            {
                var tex = sp.texture;
                if (tex == null) return false;
                var tr = sp.textureRect;
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                var png = ObjExport.Png(tex, uv);
                if (png == null) return false;
                Directory.CreateDirectory(ExportFolder);
                var name = ObjExport.Clean(sp.name);
                if (p.Name.StartsWith("Teen #")) name = "teen" + p.Name.Substring(6).Split(' ')[0] + "_" + name;
                File.WriteAllBytes(Path.Combine(ExportFolder, name + ".png"), png);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Save icon: " + e.Message); return false; }
        }

        static void SaveAll(List<Pic> list)
        {
            foreach (var p in list) if (p.File == null) _toSave.Enqueue(p);
            _saveTotal = _toSave.Count;
            _saved = 0;
            ToolkitBehaviour.Toast($"Saving {_saveTotal} pictures...");
        }

        static void SaveSome()
        {
            if (_toSave.Count == 0) return;
            for (int i = 0; i < 8 && _toSave.Count > 0; i++) if (SavePic(_toSave.Dequeue())) _saved++;
            if (_toSave.Count == 0)
            {
                ToolkitBehaviour.Toast($"Saved {_saved} pictures to exported/icons");
                Plugin.Log.LogInfo($"Icons saved to {ExportFolder}: {_saved} of {_saveTotal}");
            }
        }

        public static void Draw()
        {
            Gui.Label(_swap != null ? "HUD icon swapped to: " + _swapName : "HUD icon: the game's own");
            var top = Gui.Row(2);
            if (GUI.Button(top[0], "Put the normal icon back")) Restore();
            if (GUI.Button(top[1], "Open my images folder"))
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            Gui.Space(4);

            var src = Gui.Row(3);
            string[] names = { "Teen icons", "Game pictures", "My images" };
            for (int i = 0; i < 3; i++)
                if (Gui.Pick(src[i], names[i], _source == i, Gui.TabKind.Choice)) { _source = i; _page = 0; }

            var list = _source == 0 ? _teens : _source == 1 ? _sprites : _mine;
            if (list.Count == 0 || GUI.Button(Gui.Row(1)[0], "Rescan"))
            {
                Scan(_source);
                _lastSearch = null;
            }

            if (_source == 2)
                Gui.Label("Drop PNG or JPG files into BepInEx/config/Road96Toolkit/icons, then Rescan.");

            _search = Gui.Search(_search);
            if (_search != _lastSearch || _source != _lastSource)
            {
                _lastSearch = _search;
                _lastSource = _source;
                _shown = list.Where(p => Gui.Contains(p.Name, _search)).ToList();
                _page = 0;
            }

            if (_source != 2)
            {
                var sr = Gui.Row(1f, 1f);
                if (_toSave.Count > 0) GUI.Label(sr[0], $"Saving... {_saveTotal - _toSave.Count} of {_saveTotal}");
                else if (GUI.Button(sr[0], $"Save all {_shown.Count} as PNG")) SaveAll(_shown);
                if (GUI.Button(sr[1], "Open saved pictures"))
                {
                    Directory.CreateDirectory(ExportFolder);
                    Application.OpenURL("file:///" + ExportFolder.Replace('\\', '/'));
                }
            }
            int pick = Gui.List(_shown, ref _page, p => p.Name, 9, p => p == _sel);
            if (pick >= 0) { _sel = _shown[pick]; MakeThumb(_sel); }
            // hovering an entry shows its picture, clicking picks it
            var ev = Event.current;
            if (ev != null && ev.type == EventType.Repaint)
            {
                _hover = Gui.Hovered >= 0 ? _shown[Gui.Hovered] : null;
                if (_hover != null) MakeThumb(_hover);
            }

            Gui.Space(4);
            var show = _hover ?? _sel;
            if (show != null)
            {
                float size = 96;
                var box = new Rect(Gui.X, Gui.Y, size, size);
                GUI.Box(box, "");
                if (show.Thumb != null) GUI.DrawTexture(new Rect(box.x + 4, box.y + 4, size - 8, size - 8), show.Thumb, ScaleMode.ScaleToFit);
                var b = new Rect(Gui.X + size + 8, Gui.Y, Gui.W - size - 8, Gui.LineH);
                GUI.Label(b, show.Name);
                b.y += Gui.LineH + Gui.Gap;
                if (_sel != null && GUI.Button(b, "Use " + _sel.Name.Split(' ')[0] + " as my HUD icon")) Apply(_sel);
                else if (_sel == null) GUI.Label(b, "Click one to pick it");
                b.y += Gui.LineH + Gui.Gap;
                if (_sel != null && _sel.File == null && GUI.Button(b, "Save this picture as PNG"))
                    ToolkitBehaviour.Toast(SavePic(_sel) ? "Saved to exported/icons" : "Could not save that one");
                Gui.Y += size + Gui.Gap;
            }
        }

        static void MakeThumb(Pic p)
        {
            if (p.Thumb == null && p.Sprite != null && !p.TriedThumb) { p.TriedThumb = true; p.Thumb = Thumb(p.Sprite); }
        }

        static Texture2D Thumb(Sprite sp)
        {
            try
            {
                var tex = sp.texture;
                if (tex == null) return null;
                var tr = sp.textureRect;
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                return TeenTab.Bake(tex, uv);
            }
            catch { return null; }
        }

        static void Scan(int which)
        {
            if (which == 0)
            {
                _teens.Clear();
                var settings = Game.Settings;
                if (settings == null) return;
                Game.Try("Teen icons", () =>
                {
                    int n = settings._playerFaces?.Length ?? 0;
                    for (int i = 0; i < n; i++)
                    {
                        var sp = settings.GetPlayerFaceById(i)._icon;
                        if (sp != null) _teens.Add(new Pic { Name = $"Teen #{i}  ({sp.name})", Sprite = sp });
                    }
                });
            }
            else if (which == 1)
            {
                _sprites.Clear();
                Game.Try("Game sprites", () =>
                {
                    var seen = new HashSet<string>();
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sprite>()))
                    {
                        var sp = o.TryCast<Sprite>();
                        if (sp == null || string.IsNullOrEmpty(sp.name) || !seen.Add(sp.name)) continue;
                        var r = sp.rect;
                        if (r.width < 24 || r.height < 24) continue; // skip tiny UI bits
                        _sprites.Add(new Pic { Name = $"{sp.name}  ({(int)r.width}x{(int)r.height})", Sprite = sp });
                    }
                    _sprites.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                });
            }
            else
            {
                foreach (var p in _mine)
                {
                    if (p.Sprite != null && p.Sprite != _swap) UnityEngine.Object.Destroy(p.Sprite);
                    if (p.Thumb != null) UnityEngine.Object.Destroy(p.Thumb);
                }
                _mine.Clear();
                Directory.CreateDirectory(Folder);
                foreach (var f in Directory.GetFiles(Folder).OrderBy(x => x))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                    Game.Try("Load image", () =>
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(f))) return;
                        tex.hideFlags = HideFlags.HideAndDontSave;
                        tex.name = Path.GetFileNameWithoutExtension(f);
                        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        sp.hideFlags = HideFlags.HideAndDontSave;
                        sp.name = tex.name;
                        _mine.Add(new Pic { Name = Path.GetFileName(f), Sprite = sp, Thumb = tex, File = f });
                    });
                }
            }
            Plugin.Log.LogInfo($"Icon swapper: {(which == 0 ? _teens.Count : which == 1 ? _sprites.Count : _mine.Count)} pictures in list {which}");
        }

        static Image Portrait()
        {
            if (_bar == null)
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<EnergyBarUI>()))
                {
                    var b = o.TryCast<EnergyBarUI>();
                    if (b != null && b.gameObject.scene.IsValid()) { _bar = b; break; }
                }
            }
            return _bar?._portrait;
        }

        static void Apply(Pic p)
        {
            Game.Try("Swap icon", () =>
            {
                var img = Portrait();
                if (img == null) { ToolkitBehaviour.Toast("No HUD found, load into a trip first"); return; }
                if (_swap == null) _original = img.sprite;
                _fromPicker = false;
                _swap = p.Sprite;
                _swapName = p.Name;
                img.sprite = _swap;
                img.preserveAspect = true;
                ToolkitBehaviour.Toast("HUD icon swapped");
            });
        }

        static void Restore()
        {
            var had = _swap != null;
            _swap = null;
            _swapName = null;
            if (!had) return;
            Game.Try("Restore icon", () =>
            {
                var img = Portrait();
                if (img != null && _original != null) img.sprite = _original;
                _bar?.ForceUpdateUI();
            });
        }

        // the game redraws its HUD icon now and then, so keep ours on top
        public static void Update()
        {
            SaveSome();
            if (_swap == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                var img = Portrait();
                if (img == null) { _bar = null; return; }
                if (img.sprite == null || img.sprite.Pointer != _swap.Pointer)
                {
                    if (_original == null && img.sprite != null) _original = img.sprite;
                    img.sprite = _swap;
                    img.preserveAspect = true;
                }
            }
            catch { _bar = null; }
        }
    }
}
