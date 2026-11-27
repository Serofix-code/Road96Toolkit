using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Road96Toolkit
{
    // Photo mode: pauses the game, flies the camera, hides the HUD, adds camera effects on top of the
    // game's own look through a high priority volume, moves the sun, and saves screenshots.
    internal static class PhotoMode
    {
        public static bool Active { get; private set; }
        public static bool PanelVisible { get; private set; } = true;
        public static bool HideEverything => Active && (!PanelVisible || _captureFrames > 0);

        static float _prevTimeScale = 1f;
        static bool _paused, _hudWasHidden;
        static int _captureFrames;
        static int _superSize = 1;
        static bool _grid;
        static int _preset;

        // effects, all neutral by default so the game's own look stays
        static float _exposure, _contrast, _saturation, _hue, _temperature, _tint, _vignette, _grain;
        static Color _filter = Color.white;
        static bool _dof;
        static float _focus = 3f, _blur = 4f;
        static float _sunYaw, _sunPitch;

        static GameObject _volumeGo;
        static Exposure _exp;
        static ColorAdjustments _color;
        static WhiteBalance _white;
        static Vignette _vig;
        static FilmGrain _film;
        static DepthOfField _depth;
        static Light _sun;
        static Quaternion _sunOriginal;

        // a saved set of photo mode options
        public class Look
        {
            public string Name { get; set; } = "";
            public int Preset { get; set; }
            public float Exposure { get; set; }
            public float Contrast { get; set; }
            public float Saturation { get; set; }
            public float Hue { get; set; }
            public float Temperature { get; set; }
            public float Tint { get; set; }
            public float Vignette { get; set; }
            public float Grain { get; set; }
            public float[] Filter { get; set; } = { 1, 1, 1 };
            public bool Dof { get; set; }
            public float Focus { get; set; } = 3f;
            public float Blur { get; set; } = 4f;
            public float Fov { get; set; } = 60f;
            public float Roll { get; set; }
            public float SunYaw { get; set; }
            public float SunPitch { get; set; }
            public bool Grid { get; set; }
            public int Size { get; set; } = 1;
        }

        class LookFile
        {
            public Look Last { get; set; }
            public List<Look> Saved { get; set; } = new List<Look>();
        }

        static LookFile _looks;
        static int _page, _lookPage;
        static string _lookName = "";
        static Look _selLook;
        static string LooksPath => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "photo-looks.json");

        static LookFile Looks
        {
            get
            {
                if (_looks != null) return _looks;
                try { if (File.Exists(LooksPath)) _looks = JsonSerializer.Deserialize<LookFile>(File.ReadAllText(LooksPath)); }
                catch (Exception e) { Plugin.Log.LogWarning("Photo looks file: " + e.Message); }
                _looks ??= new LookFile();
                _looks.Saved ??= new List<Look>();
                return _looks;
            }
        }

        static void SaveLooks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LooksPath));
                File.WriteAllText(LooksPath, JsonSerializer.Serialize(Looks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Saving photo looks: " + e.Message); }
        }

        static Look Capture(string name) => new Look
        {
            Name = name, Preset = _preset, Exposure = _exposure, Contrast = _contrast, Saturation = _saturation, Hue = _hue,
            Temperature = _temperature, Tint = _tint, Vignette = _vignette, Grain = _grain,
            Filter = new[] { _filter.r, _filter.g, _filter.b }, Dof = _dof, Focus = _focus, Blur = _blur,
            Fov = Freecam.Fov, Roll = Freecam.Roll, SunYaw = _sunYaw, SunPitch = _sunPitch, Grid = _grid, Size = _superSize,
        };

        static void Use(Look l, bool camera)
        {
            if (l == null) return;
            _preset = l.Preset; _exposure = l.Exposure; _contrast = l.Contrast; _saturation = l.Saturation; _hue = l.Hue;
            _temperature = l.Temperature; _tint = l.Tint; _vignette = l.Vignette; _grain = l.Grain;
            _filter = l.Filter != null && l.Filter.Length >= 3 ? new Color(l.Filter[0], l.Filter[1], l.Filter[2]) : Color.white;
            _dof = l.Dof; _focus = l.Focus; _blur = l.Blur; _sunYaw = l.SunYaw; _sunPitch = l.SunPitch;
            _grid = l.Grid; _superSize = l.Size is 1 or 2 or 4 ? l.Size : 1;
            if (camera) { Freecam.Fov = l.Fov; Freecam.Roll = l.Roll; }
            Apply();
        }

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Road 96 Photos");

        static readonly string[] Presets = { "None", "Black and white", "Warm", "Cold", "Vintage", "Dramatic", "Dreamy" };

        public static void Toggle() { if (Active) Disable(); else Enable(); }

        public static void Enable()
        {
            if (DevView.Active) DevView.Disable();
            if (!Freecam.Active) Freecam.Enable();
            if (!Freecam.Active) return;
            Active = true;
            PanelVisible = true;
            ToolkitBehaviour.Instance?.CloseMenu();

            _prevTimeScale = Time.timeScale;
            if (Plugin.PhotoPause.Value) { Time.timeScale = 0f; _paused = true; }
            _hudWasHidden = WorldTab.HudHidden;
            if (!_hudWasHidden) WorldTab.ToggleHud();

            BuildVolume();
            FindSun();
            if (Plugin.PhotoRememberLast.Value && Looks.Last != null) Use(Looks.Last, false);
            Apply();
            ToolkitBehaviour.Toast($"Photo mode. {Plugin.PhotoKey} to leave, P for a photo, H hides this panel");
        }

        public static void Disable()
        {
            if (!Active) return;
            Active = false;
            Looks.Last = Capture("Last used");
            SaveLooks();
            if (_paused) { Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale; _paused = false; WorldTab.FixSound(false); }
            if (!_hudWasHidden && WorldTab.HudHidden) WorldTab.ToggleHud();
            if (_volumeGo != null) UnityEngine.Object.Destroy(_volumeGo);
            _volumeGo = null;
            if (_sun != null) _sun.transform.rotation = _sunOriginal;
            _sun = null;
            Freecam.Disable();
            ToolkitBehaviour.Toast("Photo mode off");
        }

        static void BuildVolume()
        {
            Game.Try("Photo volume", () =>
            {
                var cam = Game.MainCamera;
                int layer = 0;
                if (cam != null)
                {
                    var hd = cam.GetComponent<HDAdditionalCameraData>();
                    int mask = hd != null ? hd.volumeLayerMask.value : 1;
                    for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { layer = i; break; }
                }
                _volumeGo = new GameObject("Road96Toolkit_PhotoVolume");
                UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
                _volumeGo.layer = layer;
                var vol = _volumeGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 10000f;
                vol.weight = 1f;
                var p = ScriptableObject.CreateInstance<VolumeProfile>();
                _exp = p.Add(Il2CppType.Of<Exposure>(), false).Cast<Exposure>();
                _color = p.Add(Il2CppType.Of<ColorAdjustments>(), false).Cast<ColorAdjustments>();
                _white = p.Add(Il2CppType.Of<WhiteBalance>(), false).Cast<WhiteBalance>();
                _vig = p.Add(Il2CppType.Of<Vignette>(), false).Cast<Vignette>();
                _film = p.Add(Il2CppType.Of<FilmGrain>(), false).Cast<FilmGrain>();
                _depth = p.Add(Il2CppType.Of<DepthOfField>(), false).Cast<DepthOfField>();
                vol.sharedProfile = p;
            });
        }

        static void FindSun()
        {
            _sun = null;
            Game.Try("Find sun", () =>
            {
                float best = -1f;
                foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                    if (l.intensity > best) { best = l.intensity; _sun = l; }
                }
                if (_sun != null) _sunOriginal = _sun.transform.rotation;
            });
            _sunYaw = _sunPitch = 0f;
        }

        // only override what moved away from neutral, the rest stays the game's
        static void Apply()
        {
            try
            {
                if (_exp != null)
                {
                    _exp.active = Math.Abs(_exposure) > 0.001f;
                    _exp.compensation.Override(_exposure);
                }
                if (_color != null)
                {
                    bool any = Math.Abs(_contrast) > 0.1f || Math.Abs(_saturation) > 0.1f || Math.Abs(_hue) > 0.1f || _filter != Color.white;
                    _color.active = any;
                    _color.contrast.Override(_contrast);
                    _color.saturation.Override(_saturation);
                    _color.hueShift.Override(_hue);
                    _color.colorFilter.Override(_filter);
                }
                if (_white != null)
                {
                    _white.active = Math.Abs(_temperature) > 0.1f || Math.Abs(_tint) > 0.1f;
                    _white.temperature.Override(_temperature);
                    _white.tint.Override(_tint);
                }
                if (_vig != null)
                {
                    _vig.active = _vignette > 0.001f;
                    _vig.intensity.Override(_vignette);
                    _vig.smoothness.Override(0.4f);
                }
                if (_film != null)
                {
                    _film.active = _grain > 0.001f;
                    _film.intensity.Override(_grain);
                }
                if (_depth != null)
                {
                    _depth.active = _dof;
                    _depth.focusMode.Override(DepthOfFieldMode.Manual);
                    float near = Mathf.Max(0.05f, _focus - _focus * 0.5f / Mathf.Max(0.5f, _blur * 0.25f));
                    _depth.nearFocusStart.Override(0f);
                    _depth.nearFocusEnd.Override(near);
                    float far = _focus + _focus * 1.5f / Mathf.Max(0.5f, _blur * 0.5f);
                    _depth.farFocusStart.Override(far);
                    _depth.farFocusEnd.Override(far + _focus * 3f / Mathf.Max(0.5f, _blur));
                }
                if (_sun != null)
                    _sun.transform.rotation = Quaternion.Euler(0, _sunYaw, 0) * _sunOriginal * Quaternion.Euler(_sunPitch, 0, 0);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Photo effects: " + e.Message); }
        }

        static void SetPreset(int i)
        {
            _preset = i;
            _contrast = _saturation = _hue = _temperature = _tint = _vignette = _grain = 0f;
            _filter = Color.white;
            switch (i)
            {
                case 1: _saturation = -100f; _contrast = 15f; break;
                case 2: _temperature = 35f; _saturation = 10f; _vignette = 0.25f; break;
                case 3: _temperature = -35f; _tint = -5f; _contrast = 10f; break;
                case 4: _saturation = -35f; _temperature = 20f; _grain = 0.5f; _vignette = 0.4f; _filter = new Color(1f, 0.93f, 0.8f); break;
                case 5: _contrast = 45f; _saturation = 15f; _vignette = 0.45f; break;
                case 6: _contrast = -20f; _saturation = -10f; _filter = new Color(1f, 0.92f, 1f); _vignette = 0.3f; break;
            }
            Apply();
        }

        public static void Update()
        {
            if (!Active) return;
            if (!Freecam.Active) { Active = false; Disable(); return; }
            if (_captureFrames > 0) _captureFrames--;

            if (InputHelper.Down(KeyCode.H)) PanelVisible = !PanelVisible;
            if (InputHelper.Down(KeyCode.P)) TakePhoto();
            if (_paused && Time.timeScale != 0f) Time.timeScale = 0f; // something else unpaused it
        }

        static void TakePhoto()
        {
            Game.Try("Photo", () =>
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, "Road96_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                _captureFrames = 2; // hide our own UI while the frame is grabbed
                ScreenCapture.CaptureScreenshot(path, _superSize);
                ToolkitBehaviour.Toast("Photo saved to Pictures/Road 96 Photos");
                Plugin.Log.LogInfo("Photo: " + path);
            });
        }

        static float Slider(string name, float v, float step, float min, float max, string fmt = "0")
        {
            var r = Gui.Row(2.2f, 1f, 1f, 0.9f);
            GUI.Label(r[0], name + ": " + v.ToString(fmt));
            float nv = v;
            if (GUI.Button(r[1], "-")) nv = Mathf.Clamp(v - step, min, max);
            if (GUI.Button(r[2], "+")) nv = Mathf.Clamp(v + step, min, max);
            if (GUI.Button(r[3], "0")) nv = Mathf.Clamp(0f, min, max);
            if (nv != v) { _preset = -1; }
            return nv;
        }

        public static void Draw(float scale)
        {
            if (!Active || HideEverything) return;
            float sw = Screen.width / scale, sh = Screen.height / scale;

            if (_grid)
            {
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, 0.35f);
                var t = Texture2D.whiteTexture;
                for (int i = 1; i < 3; i++)
                {
                    GUI.DrawTexture(new Rect(sw * i / 3f, 0, 1, sh), t);
                    GUI.DrawTexture(new Rect(0, sh * i / 3f, sw, 1), t);
                }
                GUI.color = old;
            }

            var area = new Rect(10, 40, 340, Mathf.Min(sh - 60, 760));
            GUI.Box(area, "");
            GUI.Box(area, "");
            Gui.Begin(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));
            Gui.Header("Photo mode");
            var r = Gui.Row(1.4f, 1f);
            if (GUI.Button(r[0], "Take photo (P)")) TakePhoto();
            if (GUI.Button(r[1], "Size " + _superSize + "x")) _superSize = _superSize == 1 ? 2 : _superSize == 2 ? 4 : 1;
            r = Gui.Row(3);
            if (GUI.Button(r[0], "Hide panel (H)")) PanelVisible = false;
            if (GUI.Button(r[1], (_grid ? "[ON] " : "[off] ") + "Grid")) _grid = !_grid;
            if (GUI.Button(r[2], _paused ? "Unpause" : "Pause"))
            {
                if (_paused) { Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale; _paused = false; WorldTab.FixSound(false); }
                else { _prevTimeScale = Time.timeScale; Time.timeScale = 0f; _paused = true; }
            }
            r = Gui.Row(2);
            if (GUI.Button(r[0], _page == 0 ? "[ Options ]" : "Options")) _page = 0;
            if (GUI.Button(r[1], _page == 1 ? $"[ Saved looks ({Looks.Saved.Count}) ]" : $"Saved looks ({Looks.Saved.Count})")) _page = 1;
            if (_page == 1) { DrawLooks(); return; }

            Gui.Header("Camera");
            Freecam.Fov = Slider("Field of view", Freecam.Fov, 5f, 5f, 150f);
            Freecam.Roll = Slider("Roll", Freecam.Roll, 5f, -180f, 180f);
            var before = (_exposure, _contrast, _saturation, _hue, _temperature, _vignette, _grain, _dof, _focus, _blur, _sunYaw, _sunPitch);
            _exposure = Slider("Exposure", _exposure, 0.25f, -5f, 5f, "0.00");

            Gui.Header("Look");
            r = Gui.Row(2f, 1f);
            GUI.Label(r[0], "Filter: " + (_preset >= 0 ? Presets[_preset] : "Custom"));
            if (GUI.Button(r[1], "Next filter")) SetPreset((_preset + 1 + Presets.Length) % Presets.Length);
            _contrast = Slider("Contrast", _contrast, 10f, -100f, 100f);
            _saturation = Slider("Saturation", _saturation, 10f, -100f, 100f);
            _temperature = Slider("Warmth", _temperature, 10f, -100f, 100f);
            _hue = Slider("Hue", _hue, 15f, -180f, 180f);
            _vignette = Slider("Vignette", _vignette, 0.1f, 0f, 1f, "0.0");
            _grain = Slider("Film grain", _grain, 0.1f, 0f, 1f, "0.0");

            Gui.Header("Focus");
            if (Gui.Toggle("Depth of field", _dof)) _dof = !_dof;
            r = Gui.Row(2.2f, 1f, 1f, 0.9f);
            GUI.Label(r[0], $"Focus at: {_focus:0.0} m");
            if (GUI.Button(r[1], "-")) _focus = Mathf.Max(0.3f, _focus / 1.25f);
            if (GUI.Button(r[2], "+")) _focus = Mathf.Min(500f, _focus * 1.25f);
            if (GUI.Button(r[3], "Aim")) FocusOnCenter();
            _blur = Slider("Blur", _blur, 1f, 1f, 10f);

            if (_sun != null)
            {
                Gui.Header("Sun");
                _sunYaw = Slider("Sun turn", _sunYaw, 15f, -180f, 180f);
                _sunPitch = Slider("Sun height", _sunPitch, 5f, -80f, 80f);
            }

            if ((_exposure, _contrast, _saturation, _hue, _temperature, _vignette, _grain, _dof, _focus, _blur, _sunYaw, _sunPitch) != before) Apply();

            Gui.Space(4);
            r = Gui.Row(2);
            if (GUI.Button(r[0], "Reset all")) { SetPreset(0); _exposure = 0; _dof = false; _sunYaw = _sunPitch = 0; Apply(); }
            if (GUI.Button(r[1], "Leave (" + Plugin.PhotoKey + ")")) { Disable(); return; }
            if (Gui.Button("Open the photos folder"))
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }
            Gui.Label("Hold right mouse to look, WASD Q E to fly, wheel for speed.");
        }

        static void DrawLooks()
        {
            Gui.Header("Save these options");
            var r = Gui.Row(1f, 2.4f);
            GUI.Label(r[0], "Name");
            _lookName = Gui.TextField(r[1], _lookName);
            r = Gui.Row(2);
            if (GUI.Button(r[0], "Save as a new look"))
            {
                var name = string.IsNullOrWhiteSpace(_lookName) ? "Look " + (Looks.Saved.Count + 1) : _lookName.Trim();
                Looks.Saved.RemoveAll(l => l.Name == name);
                Looks.Saved.Add(Capture(name));
                SaveLooks();
                _lookName = "";
                ToolkitBehaviour.Toast("Saved look: " + name);
            }
            if (GUI.Button(r[1], "Overwrite selected") && _selLook != null)
            {
                var i = Looks.Saved.IndexOf(_selLook);
                if (i >= 0) { Looks.Saved[i] = Capture(_selLook.Name); _selLook = Looks.Saved[i]; SaveLooks(); ToolkitBehaviour.Toast("Updated " + _selLook.Name); }
            }
            if (Gui.Toggle("Start photo mode with the options I used last", Plugin.PhotoRememberLast.Value))
                Plugin.PhotoRememberLast.Value = !Plugin.PhotoRememberLast.Value;

            Gui.Header("Your looks");
            int pick = Gui.List(Looks.Saved, ref _lookPage, l => l.Name + (l.Preset > 0 && l.Preset < Presets.Length ? "   (" + Presets[l.Preset] + ")" : ""), 10, l => l == _selLook);
            if (pick >= 0) { _selLook = Looks.Saved[pick]; Use(_selLook, Plugin.PhotoLookMovesCamera.Value); ToolkitBehaviour.Toast("Using " + _selLook.Name); }

            r = Gui.Row(2);
            if (GUI.Button(r[0], "Delete selected") && _selLook != null)
            {
                Looks.Saved.Remove(_selLook);
                _selLook = null;
                SaveLooks();
            }
            if (GUI.Button(r[1], (Plugin.PhotoLookMovesCamera.Value ? "[ON] " : "[off] ") + "Looks set FOV and roll"))
                Plugin.PhotoLookMovesCamera.Value = !Plugin.PhotoLookMovesCamera.Value;
            Gui.Label("Click a look to use it. Saved in BepInEx/config/Road96Toolkit/photo-looks.json");
            Gui.Space(4);
            if (Gui.Button("Leave photo mode (" + Plugin.PhotoKey + ")")) Disable();
        }

        static void FocusOnCenter()
        {
            var cam = Game.MainCamera;
            if (cam == null) return;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            {
                _focus = Mathf.Max(0.3f, hit.distance);
                _dof = true;
                Apply();
            }
        }
    }
}
