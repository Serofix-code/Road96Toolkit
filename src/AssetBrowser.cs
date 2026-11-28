using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // The Assets panel of the dev view: everything the game has loaded, sorted by kind, with what the
    // level uses marked. Pick one to see it, place it in the world, put it on the selected object, or
    // save it as a file (models as OBJ, pictures as PNG, sounds as WAV).
    internal static class AssetBrowser
    {
        enum Kind { Prefabs, Characters, Meshes, Materials, Textures, Sprites, Animations, Sounds, Effects, Lights }
        static readonly string[] KindNames = { "Prefabs", "Characters", "Meshes", "Materials", "Textures", "Sprites", "Animations", "Sounds", "Effects", "Lights" };

        class Asset
        {
            public string Name, Info;
            public UnityEngine.Object Obj;
            public GameObject Go;
            public bool Here;
            public int Uses;
            public Texture Preview;
            public bool TriedPreview;
        }

        static Kind _kind = Kind.Prefabs;
        static readonly Dictionary<Kind, List<Asset>> _all = new Dictionary<Kind, List<Asset>>();
        static List<Asset> _shown = new List<Asset>();
        static string _search = "", _lastSearch = null;
        static bool _hereOnly, _lastHere;
        static Kind _lastKind = (Kind)(-1);
        static Asset _sel;
        static Vector2 _scroll;
        static int _userIdx;
        static string _scanned;

        // who uses a mesh or a material in the loaded scenes
        static readonly Dictionary<IntPtr, List<Renderer>> _meshUsers = new Dictionary<IntPtr, List<Renderer>>();
        static readonly Dictionary<IntPtr, List<Renderer>> _matUsers = new Dictionary<IntPtr, List<Renderer>>();
        static readonly HashSet<IntPtr> _texHere = new HashSet<IntPtr>();

        public static string ModelsFolder => ObjExport.Folder;
        static string Exported => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "exported");

        static void Users()
        {
            _meshUsers.Clear(); _matUsers.Clear(); _texHere.Clear();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                Mesh m = null;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) m = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                if (m != null) Add(_meshUsers, m.Pointer, r);
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (!_matUsers.ContainsKey(mat.Pointer))
                        try { foreach (var id in mat.GetTexturePropertyNameIDs()) { var t = mat.GetTexture(id); if (t != null) _texHere.Add(t.Pointer); } } catch { }
                    Add(_matUsers, mat.Pointer, r);
                }
            }
        }

        static void Add(Dictionary<IntPtr, List<Renderer>> d, IntPtr k, Renderer r)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<Renderer>();
            l.Add(r);
        }

        static IEnumerable<T> AllOf<T>() where T : UnityEngine.Object
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
            {
                var t = o.TryCast<T>();
                if (t != null && !string.IsNullOrEmpty(t.name)) yield return t;
            }
        }

        static void Scan(Kind k)
        {
            var list = new List<Asset>();
            Game.Try("Assets " + k, () =>
            {
                if (k == Kind.Meshes || k == Kind.Materials || k == Kind.Textures) Users();
                switch (k)
                {
                    case Kind.Prefabs:
                    {
                        var seen = new HashSet<string>();
                        foreach (var go in AllOf<GameObject>())
                        {
                            if (go.scene.IsValid() || go.transform.parent != null || (go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                            if (go.name.StartsWith("ToolkitTemplate") || !seen.Add(go.name)) continue;
                            int rs = go.GetComponentsInChildren<Renderer>(true).Count;
                            bool fx = go.GetComponentInChildren<ParticleSystem>(true) != null;
                            if (rs == 0 && !fx) continue;
                            list.Add(new Asset { Name = go.name, Go = go, Obj = go, Info = fx ? "effect" : rs + " parts" });
                        }
                        break;
                    }
                    case Kind.Characters:
                        foreach (var s in Spawner.Gather(0).Concat(Spawner.Gather(1)))
                            list.Add(new Asset { Name = s.Name, Go = s.Go, Obj = s.Go, Info = s.Kind, Here = s.Kind == "in scene" });
                        break;
                    case Kind.Meshes:
                        foreach (var m in AllOf<Mesh>())
                        {
                            _meshUsers.TryGetValue(m.Pointer, out var u);
                            list.Add(new Asset { Name = m.name, Obj = m, Uses = u?.Count ?? 0, Here = u != null, Info = $"{m.vertexCount} verts" + (m.subMeshCount > 1 ? $", {m.subMeshCount} parts" : "") + (m.isReadable ? "" : ", locked") });
                        }
                        break;
                    case Kind.Materials:
                        foreach (var m in AllOf<Material>())
                        {
                            _matUsers.TryGetValue(m.Pointer, out var u);
                            string sh = "";
                            try { sh = m.shader != null ? m.shader.name : ""; } catch { }
                            list.Add(new Asset { Name = m.name, Obj = m, Uses = u?.Count ?? 0, Here = u != null, Info = sh });
                        }
                        break;
                    case Kind.Textures:
                        foreach (var t in AllOf<Texture2D>())
                        {
                            if (t.width < 8 || t.height < 8) continue;
                            list.Add(new Asset { Name = t.name, Obj = t, Preview = t, TriedPreview = true, Here = _texHere.Contains(t.Pointer), Info = $"{t.width}x{t.height} {t.format}" });
                        }
                        break;
                    case Kind.Sprites:
                        foreach (var s in AllOf<Sprite>())
                        {
                            var r = s.rect;
                            list.Add(new Asset { Name = s.name, Obj = s, Info = $"{(int)r.width}x{(int)r.height}" });
                        }
                        break;
                    case Kind.Animations:
                        foreach (var c in AllOf<AnimationClip>())
                            list.Add(new Asset { Name = c.name, Obj = c, Info = $"{c.length:0.0} s" + (c.legacy ? ", legacy" : "") });
                        break;
                    case Kind.Sounds:
                        foreach (var c in AllOf<AudioClip>())
                            list.Add(new Asset { Name = c.name, Obj = c, Info = $"{c.length:0.0} s, {c.channels} ch, {c.frequency / 1000f:0.#} kHz" });
                        break;
                    case Kind.Effects:
                        foreach (var p in AllOf<ParticleSystem>())
                        {
                            var go = p.gameObject;
                            // only the top effect of a group
                            if (go.transform.parent != null && go.transform.parent.GetComponentInParent<ParticleSystem>() != null) continue;
                            bool scene = go.scene.IsValid();
                            list.Add(new Asset { Name = go.name, Go = go, Obj = go, Here = scene, Info = scene ? (p.isPlaying ? "in level, playing" : "in level") : "prefab" });
                        }
                        break;
                    case Kind.Lights:
                        foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                            list.Add(new Asset { Name = l.name, Go = l.gameObject, Obj = l, Here = true, Info = $"{l.type}, range {l.range:0.#}" + (l.enabled ? "" : ", off") });
                        break;
                }
            });
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _all[k] = list;
            _scanned = TravelTab.SceneName;
        }

        // ---------- placing ----------

        static GameObject _templates;

        // a hidden object with this mesh and its materials, to place copies of
        internal static GameObject MeshTemplate(Mesh mesh, IList<Material> mats)
        {
            if (_templates == null)
            {
                _templates = new GameObject("ToolkitTemplates");
                _templates.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_templates);
            }
            var go = new GameObject(mesh.name);
            go.transform.SetParent(_templates.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mats != null && mats.Count > 0)
            {
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(mats.Count);
                for (int i = 0; i < mats.Count; i++) arr[i] = mats[i];
                mr.sharedMaterials = arr;
            }
            try { go.AddComponent<MeshCollider>().sharedMesh = mesh; } catch { }
            return go;
        }

        static IList<Material> MatsFor(Mesh m)
        {
            if (_meshUsers.TryGetValue(m.Pointer, out var u) && u.Count > 0) return u[0].sharedMaterials.ToList();
            return new List<Material>();
        }

        static void Place(Asset a)
        {
            switch (_kind)
            {
                case Kind.Meshes:
                    var mesh = a.Obj.TryCast<Mesh>();
                    var mats = MatsFor(mesh);
                    DevView.PlaceFrom(MeshTemplate(mesh, mats), mesh.name, mesh, mats);
                    break;
                default:
                    if (a.Go != null) DevView.PlaceFrom(a.Go, a.Name, null, null);
                    break;
            }
        }

        // ---------- saving ----------

        static readonly Queue<Asset> _toSave = new Queue<Asset>();
        static int _saved, _saveTotal;

        static bool Save(Asset a, Kind k, bool quiet)
        {
            try
            {
                switch (k)
                {
                    case Kind.Prefabs:
                    case Kind.Characters:
                    case Kind.Effects:
                        if (a.Go == null) return false;
                        ObjExport.Export(a.Go);
                        return true;
                    case Kind.Meshes:
                        var mesh = a.Obj.TryCast<Mesh>();
                        return ObjExport.ExportMesh(mesh, MatsFor(mesh), quiet);
                    case Kind.Materials:
                        var mat = a.Obj.TryCast<Material>();
                        var tex = MainTex(mat);
                        return tex != null && WriteFile(Path.Combine(Exported, "textures"), ObjExport.Clean(mat.name), ".png", ObjExport.Png(tex));
                    case Kind.Textures:
                        var t = a.Obj.TryCast<Texture>();
                        return WriteFile(Path.Combine(Exported, "textures"), ObjExport.Clean(t.name), ".png", ObjExport.Png(t));
                    case Kind.Sprites:
                        var sp = a.Obj.TryCast<Sprite>();
                        var st = sp.texture;
                        if (st == null) return false;
                        var tr = sp.textureRect;
                        return WriteFile(Path.Combine(Exported, "icons"), ObjExport.Clean(sp.name), ".png",
                            ObjExport.Png(st, new Rect(tr.x / st.width, tr.y / st.height, tr.width / st.width, tr.height / st.height)));
                    case Kind.Sounds:
                        return WriteFile(Path.Combine(Exported, "sounds"), ObjExport.Clean(a.Name), ".wav", Wav(a.Obj.TryCast<AudioClip>()));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Save {a.Name}: {e.Message}"); }
            return false;
        }

        static bool WriteFile(string dir, string name, string ext, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ext), bytes);
            return true;
        }

        static readonly string[] ColorMaps = { "_BaseColorMap", "_MainTex", "_BaseMap", "_UnlitColorMap" };

        static Texture MainTex(Material m)
        {
            if (m == null) return null;
            foreach (var k in ColorMaps)
                if (m.HasProperty(k)) { var t = m.GetTexture(k); if (t != null) return t; }
            return null;
        }

        // 16 bit PCM. Sounds the game streams from disk or keeps compressed come out silent
        static byte[] Wav(AudioClip c)
        {
            if (c == null || c.samples <= 0) return null;
            int n = c.samples * c.channels;
            var data = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(n);
            if (!c.GetData(data, 0)) return null;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)c.channels);
            w.Write(c.frequency); w.Write(c.frequency * c.channels * 2); w.Write((short)(c.channels * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(n * 2);
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                float f = data[i];
                if (f != 0f) any = true;
                w.Write((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
            }
            if (!any) { Plugin.Log.LogInfo($"Sound {c.name} came out silent (load type {c.loadType})"); return null; }
            return ms.ToArray();
        }

        static Kind _saveKind;

        public static void Update()
        {
            if (_toSave.Count == 0) return;
            // models are heavy, pictures and sounds are not
            int per = _saveKind == Kind.Meshes ? 1 : 6;
            for (int i = 0; i < per && _toSave.Count > 0; i++) if (Save(_toSave.Dequeue(), _saveKind, true)) _saved++;
            if (_toSave.Count == 0)
            {
                ToolkitBehaviour.Toast($"Saved {_saved} of {_saveTotal} to {Exported}");
                Plugin.Log.LogInfo($"Asset export: {_saved} of {_saveTotal} {_saveKind}");
            }
        }

        static bool CanSaveAll => _kind == Kind.Meshes || _kind == Kind.Textures || _kind == Kind.Sprites || _kind == Kind.Sounds || _kind == Kind.Materials;

        // ---------- sound preview ----------

        static AudioSource _player;

        static void PlaySound(AudioClip c)
        {
            if (_player == null)
            {
                var go = new GameObject("ToolkitSoundPreview");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _player = go.AddComponent<AudioSource>();
                _player.spatialBlend = 0f;
            }
            _player.Stop();
            _player.clip = c;
            _player.Play();
        }

        // ---------- drawing ----------

        public static void Draw(Rect area)
        {
            if (_lastKind != _kind || !_all.ContainsKey(_kind) || _scanned != TravelTab.SceneName) { Scan(_kind); _lastSearch = null; }
            if (_lastKind != _kind) { _lastKind = _kind; _sel = null; _scroll = Vector2.zero; }

            // kinds on the left
            var left = new Rect(area.x, area.y, 130, area.height);
            float y = left.y;
            for (int i = 0; i < KindNames.Length; i++)
            {
                int count = _all.TryGetValue((Kind)i, out var l) ? l.Count : -1;
                if (Gui.Pick(new Rect(left.x, y, left.width, 20), KindNames[i] + (count >= 0 ? $"  {count}" : ""), _kind == (Kind)i, Gui.TabKind.Choice)) _kind = (Kind)i;
                y += 22;
            }

            // details on the right
            var right = new Rect(area.xMax - 290, area.y, 290, area.height);
            var mid = new Rect(left.xMax + 6, area.y, right.x - left.xMax - 12, area.height);

            float x = mid.x;
            Rect Next(float w) { var r = new Rect(x, mid.y, w, 20); x += w + 3; return r; }
            GUI.Label(Next(46), "Search");
            var s = Gui.TextField(Next(Math.Max(80, mid.width - 46 - 3 * 4 - 80 - 80 - (CanSaveAll ? 120 : 0))), _search);
            if (s != _search) _search = s;
            if (ToolButton(Next(80), "Used here", _hereOnly)) _hereOnly = !_hereOnly;
            if (GUI.Button(Next(80), "Refresh")) { Scan(_kind); _lastSearch = null; }
            if (CanSaveAll)
            {
                if (_toSave.Count > 0) GUI.Label(Next(120), $"Saving {_saveTotal - _toSave.Count}/{_saveTotal}");
                else if (GUI.Button(Next(120), $"Save all {_shown.Count}")) { _saveKind = _kind; foreach (var a in _shown) _toSave.Enqueue(a); _saveTotal = _toSave.Count; _saved = 0; }
            }

            if (_search != _lastSearch || _hereOnly != _lastHere)
            {
                _lastSearch = _search; _lastHere = _hereOnly;
                _shown = (_all.TryGetValue(_kind, out var l) ? l : new List<Asset>())
                    .Where(a => (!_hereOnly || a.Here) && (_search.Length == 0 || Gui.Contains(a.Name, _search) || Gui.Contains(a.Info, _search))).ToList();
                _scroll = Vector2.zero;
            }

            const float rh = 20f;
            var list = new Rect(mid.x, mid.y + 24, mid.width, mid.height - 24);
            var view = new Rect(0, 0, list.width - 18, Math.Max(list.height, _shown.Count * rh));
            _scroll = GUI.BeginScrollView(list, _scroll, view);
            int first = Math.Max(0, (int)(_scroll.y / rh));
            int last = Math.Min(_shown.Count, first + (int)(list.height / rh) + 2);
            var old = GUI.color;
            float nameW = view.width * 0.58f;
            for (int i = first; i < last; i++)
            {
                var a = _shown[i];
                GUI.color = a == _sel ? new Color(0.45f, 0.75f, 1f) : a.Here ? Color.white : new Color(0.78f, 0.78f, 0.78f);
                if (GUI.Button(new Rect(0, i * rh, nameW, rh - 1), a.Name)) { _sel = a; _userIdx = 0; }
                GUI.color = new Color(0.7f, 0.8f, 0.85f);
                GUI.Label(new Rect(nameW + 6, i * rh, view.width - nameW - 6, rh), (a.Here ? "here  " : "") + (a.Uses > 0 ? $"x{a.Uses}  " : "") + a.Info);
            }
            GUI.color = old;
            GUI.EndScrollView();
            if (_shown.Count == 0) GUI.Label(new Rect(list.x + 4, list.y, list.width, 22), "Nothing here");

            DrawDetails(right);
        }

        static bool ToolButton(Rect r, string text, bool on)
        {
            var old = GUI.color;
            if (on) GUI.color = new Color(0.45f, 0.75f, 1f);
            bool hit = GUI.Button(r, text);
            GUI.color = old;
            return hit;
        }

        static void DrawDetails(Rect area)
        {
            GUI.Box(area, "");
            var a = _sel;
            Gui.Begin(new Rect(area.x + 6, area.y + 4, area.width - 12, area.height - 8));
            if (a == null || a.Obj == null)
            {
                Gui.Label("Pick an asset on the left.");
                Gui.Label(DiskMeshes.Status);
                if (Gui.Button("Open the exported folder")) { Directory.CreateDirectory(Exported); Application.OpenURL("file:///" + Exported.Replace('\\', '/')); }
                return;
            }
            Gui.Label(a.Name);
            Gui.Label(KindNames[(int)_kind] + ", " + a.Info);

            if (!a.TriedPreview)
            {
                a.TriedPreview = true;
                try
                {
                    if (_kind == Kind.Materials) a.Preview = MainTex(a.Obj.TryCast<Material>());
                    else if (_kind == Kind.Sprites) a.Preview = a.Obj.TryCast<Sprite>().texture;
                }
                catch { }
            }
            if (a.Preview != null)
            {
                var box = new Rect(Gui.X, Gui.Y, 84, 84);
                GUI.Box(box, "");
                var r = new Rect(box.x + 2, box.y + 2, 80, 80);
                if (_kind == Kind.Sprites)
                {
                    var sp = a.Obj.TryCast<Sprite>();
                    var tr = sp.textureRect; var t = sp.texture;
                    GUI.DrawTextureWithTexCoords(r, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height));
                }
                else GUI.DrawTexture(r, a.Preview, ScaleMode.ScaleToFit);
                Gui.Y += 88;
            }

            var sel = DevView.Selected;
            switch (_kind)
            {
                case Kind.Prefabs:
                case Kind.Characters:
                    if (Gui.Button("Place in the world")) Place(a);
                    if (Gui.Button("Save as OBJ")) Save(a, _kind, false);
                    if (Gui.Button("3D preview")) NpcPreview.Show(a.Go, a.Name);
                    break;
                case Kind.Meshes:
                    if (Gui.Button("Place in the world")) Place(a);
                    if (Gui.Button("Save as OBJ")) Save(a, _kind, false);
                    UsersButtons(_meshUsers, a);
                    break;
                case Kind.Materials:
                    if (sel != null && Gui.Button("Put it on " + sel.name)) PutOn(sel, a.Obj.TryCast<Material>());
                    if (Gui.Button("Save its picture as PNG")) Toast(Save(a, _kind, false));
                    UsersButtons(_matUsers, a);
                    break;
                case Kind.Textures:
                case Kind.Sprites:
                    if (Gui.Button("Save as PNG")) Toast(Save(a, _kind, false));
                    break;
                case Kind.Animations:
                    var anim = sel != null ? sel.GetComponentInChildren<Animator>() : null;
                    if (anim != null) { if (Gui.Button("Play it on " + sel.name)) Poses.Play(sel.gameObject, anim, a.Obj.TryCast<AnimationClip>()); }
                    else Gui.Label("Select a character to play it on.");
                    break;
                case Kind.Sounds:
                    var row = Gui.Row(1f, 1f);
                    if (GUI.Button(row[0], "Play")) PlaySound(a.Obj.TryCast<AudioClip>());
                    if (GUI.Button(row[1], "Stop") && _player != null) _player.Stop();
                    if (Gui.Button("Save as WAV")) Toast(Save(a, _kind, false));
                    break;
                case Kind.Effects:
                    if (a.Here)
                    {
                        if (Gui.Button("Select it")) DevView.Select(a.Go.transform);
                        var ps = a.Go.GetComponent<ParticleSystem>();
                        var r2 = Gui.Row(1f, 1f);
                        if (ps != null && GUI.Button(r2[0], "Play")) { a.Go.SetActive(true); ps.Play(true); }
                        if (ps != null && GUI.Button(r2[1], "Stop")) ps.Stop(true);
                    }
                    if (Gui.Button(a.Here ? "Place a copy" : "Place in the world")) Place(a);
                    break;
                case Kind.Lights:
                    var light = a.Obj.TryCast<Light>();
                    if (Gui.Button("Select it")) DevView.Select(a.Go.transform);
                    if (light != null && Gui.Toggle("On", light.enabled)) light.enabled = !light.enabled;
                    if (Gui.Button("Place a copy")) Place(a);
                    break;
            }
            if (_kind == Kind.Meshes || _kind == Kind.Prefabs || _kind == Kind.Characters || _kind == Kind.Effects)
                if (ObjExport.LastResult != null) Gui.Label(ObjExport.LastResult, Gui.LineH * 2);
        }

        static void Toast(bool ok) => ToolkitBehaviour.Toast(ok ? "Saved to " + Exported : "Could not save that one, see the log");

        static void UsersButtons(Dictionary<IntPtr, List<Renderer>> map, Asset a)
        {
            if (!map.TryGetValue(a.Obj.Pointer, out var u) || u.Count == 0) { Gui.Label("Not used in this level."); return; }
            if (Gui.Button($"Show who uses it ({(_userIdx % u.Count) + 1} of {u.Count})"))
            {
                var r = u[_userIdx++ % u.Count];
                if (r != null) { DevView.Select(r.transform); DevView.FocusOn(r.transform); }
            }
        }

        static void PutOn(Transform t, Material m)
        {
            if (m == null) return;
            int n = 0;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(mats.Length);
                for (int i = 0; i < mats.Length; i++) arr[i] = m;
                r.sharedMaterials = arr;
                n++;
            }
            ToolkitBehaviour.Toast($"{m.name} put on {n} parts of {t.name}");
        }
    }
}
