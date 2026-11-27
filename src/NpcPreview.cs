using System;
using UnityEngine;
using Il2CppInterop.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Road96Toolkit
{
    // Small turntable that renders a copy of a character far below the map into a texture,
    // shown in its own little window next to the toolkit.
    internal static class NpcPreview
    {
        const int Layer = 31;
        static readonly Vector3 Spot = new Vector3(0f, -4000f, 0f);

        static GameObject _model, _camGo, _lightGo, _fillGo, _volumeGo;
        static Exposure _exposure;
        static Camera _cam;
        static RenderTexture _rt;
        static GameObject _source;
        static string _name;
        static float _yaw;
        static float _zoom = 1f;
        static Bounds _bounds;

        public static bool Showing => _model != null;
        public static GameObject Source => _source;

        public static void Show(GameObject template, string name)
        {
            Hide();
            if (template == null) return;
            _source = template;
            _name = name;
            Game.Try("Preview", () =>
            {
                bool was = template.activeSelf;
                if (was) template.SetActive(false);
                _model = UnityEngine.Object.Instantiate(template.Cast<UnityEngine.Object>(), Spot, Quaternion.identity).Cast<GameObject>();
                if (was) template.SetActive(true);
                _model.name = "Road96Toolkit_Preview";
                UnityEngine.Object.DontDestroyOnLoad(_model);
                Spawner.MakeStill(_model);
                foreach (var c in _model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                foreach (var a in _model.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
                SetLayer(_model.transform, Layer);
                _model.SetActive(true);

                _bounds = new Bounds(Spot + Vector3.up, Vector3.one);
                bool first = true;
                foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled) continue;
                    if (first) { _bounds = r.bounds; first = false; }
                    else _bounds.Encapsulate(r.bounds);
                }

                if (_rt == null)
                {
                    _rt = new RenderTexture(384, 512, 24, RenderTextureFormat.ARGB32);
                    _rt.Create();
                }

                _camGo = new GameObject("Road96Toolkit_PreviewCam");
                UnityEngine.Object.DontDestroyOnLoad(_camGo);
                _cam = _camGo.AddComponent<Camera>();
                _cam.targetTexture = _rt;
                _cam.cullingMask = 1 << Layer;
                _cam.fieldOfView = 30f;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 50f;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 1f);
                Game.Try("Preview HDRP camera", () =>
                {
                    var hd = _camGo.AddComponent<HDAdditionalCameraData>();
                    hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                    hd.backgroundColorHDR = new Color(0.03f, 0.035f, 0.045f, 1f);
                    // only our own volume, so the level's fog and its night time auto exposure
                    // (which blew the picture out to white) stay out of the preview
                    hd.volumeLayerMask = 1 << Layer;
                });

                Game.Try("Preview volume", () =>
                {
                    _volumeGo = new GameObject("Road96Toolkit_PreviewVolume");
                    UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
                    _volumeGo.layer = Layer;
                    _volumeGo.transform.position = Spot;
                    var box = _volumeGo.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = new Vector3(60f, 60f, 60f);
                    var vol = _volumeGo.AddComponent<Volume>();
                    vol.isGlobal = false;
                    vol.priority = 1000f;
                    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    _exposure = profile.Add(Il2CppType.Of<Exposure>(), true).Cast<Exposure>();
                    _exposure.mode.Override(ExposureMode.Fixed);
                    _exposure.fixedExposure.Override(Plugin.PreviewExposure.Value);
                    var fog = profile.Add(Il2CppType.Of<Fog>(), true).Cast<Fog>();
                    fog.enabled.Override(false);
                    vol.sharedProfile = profile;
                });

                // key light in front and a weaker fill from the side, both next to the model
                _lightGo = MakeLight("Road96Toolkit_PreviewLight", 900f);
                _fillGo = MakeLight("Road96Toolkit_PreviewFill", 300f);

                _yaw = 180f;
                _zoom = 1f;
            });
        }

        public static void Hide()
        {
            if (_model != null) UnityEngine.Object.Destroy(_model);
            if (_camGo != null) UnityEngine.Object.Destroy(_camGo);
            if (_lightGo != null) UnityEngine.Object.Destroy(_lightGo);
            if (_fillGo != null) UnityEngine.Object.Destroy(_fillGo);
            if (_volumeGo != null) UnityEngine.Object.Destroy(_volumeGo);
            _model = _camGo = _lightGo = _fillGo = _volumeGo = null;
            _exposure = null;
            _cam = null;
            _source = null;
        }

        static GameObject MakeLight(string name, float lumen)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.cullingMask = 1 << Layer;
            Game.Try("Preview HDRP light", () =>
            {
                var hl = go.AddComponent<HDAdditionalLightData>();
                hl.intensity = lumen;
            });
            return go;
        }

        static void SetExposure(float ev)
        {
            Plugin.PreviewExposure.Value = Mathf.Clamp(ev, 0f, 16f);
            try { _exposure?.fixedExposure.Override(Plugin.PreviewExposure.Value); } catch { }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        public static void Update()
        {
            if (_model == null)
            {
                if (_camGo != null) Hide();
                return;
            }
            _yaw += Time.unscaledDeltaTime * 30f;
            var center = _bounds.center;
            float h = Mathf.Max(_bounds.size.y, 0.5f);
            float dist = h / (2f * Mathf.Tan(15f * Mathf.Deg2Rad)) * 1.15f * _zoom;
            var dir = Quaternion.Euler(8f, _yaw, 0f) * Vector3.forward;
            _camGo.transform.position = center - dir * dist;
            _camGo.transform.LookAt(center);
            _lightGo.transform.position = center - dir * 1.5f + Vector3.up * 1.2f;
            if (_fillGo != null) _fillGo.transform.position = center + Quaternion.Euler(0, 100f, 0) * (-dir) * 1.8f;
        }

        // drawn by the main behaviour, to the right of the toolkit window
        public static void Draw(Rect anchor)
        {
            if (_model == null || _rt == null) return;
            var r = new Rect(anchor.xMax + 8, anchor.y, 300, 498);
            GUI.Box(r, "");
            GUI.Box(r, _name ?? "Preview");
            var img = new Rect(r.x + 8, r.y + 26, r.width - 16, (r.width - 16) * 4f / 3f);
            try { GUI.DrawTexture(img, _rt, ScaleMode.ScaleToFit); } catch { }
            float y = img.yMax + 6;
            var b1 = new Rect(r.x + 8, y, (r.width - 24) / 3f, 22);
            var b2 = new Rect(b1.xMax + 4, y, b1.width, 22);
            var b3 = new Rect(b2.xMax + 4, y, b1.width, 22);
            if (GUI.Button(b1, "Zoom in")) _zoom = Mathf.Max(0.3f, _zoom * 0.8f);
            if (GUI.Button(b2, "Zoom out")) _zoom = Mathf.Min(3f, _zoom * 1.25f);
            if (GUI.Button(b3, "Close")) { Hide(); return; }
            var d1 = new Rect(r.x + 8, y + 26, (r.width - 20) / 2f, 22);
            var d2 = new Rect(d1.xMax + 4, d1.y, d1.width, 22);
            if (GUI.Button(d1, "Darker")) SetExposure(Plugin.PreviewExposure.Value + 0.5f);
            if (GUI.Button(d2, "Brighter")) SetExposure(Plugin.PreviewExposure.Value - 0.5f);
            if (GUI.Button(new Rect(r.x + 8, y + 52, r.width - 16, 22), "Spawn this character"))
                Spawner.SpawnObject(_source, _name);
        }
    }
}
