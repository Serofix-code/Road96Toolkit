using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Road96Toolkit
{
    // A level editor on top of the freecam, laid out like a game engine editor:
    //   top      toolbar: layout files, tools (select, move, rotate, scale), snap, pause, undo
    //   left     Hierarchy (every loaded scene as a tree) and Spawn (characters and props)
    //   right    Inspector of the selected object: transform fields, components, character, actions
    //   bottom   Console (the live BepInEx log) and Saved objects (things that stay in a level)
    internal static class DevView
    {
        public static bool Active { get; private set; }
        public static bool Typing { get; private set; }
        public static bool OverUi { get; private set; }
        public static bool BusyWithMouse => _ghost != null || _grab || _dragging;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        enum Tool { Select, Move, Rotate, Scale }
        static Tool _tool = Tool.Select;
        static bool _snap;

        // panels
        static int _leftTab, _bottomTab, _inspTab;
        static bool _bottomOpen = true, _tallAssets = true;
        static readonly float[] AreaSizes = { 10f, 25f, 50f, 100f, 250f };
        static int _area = 1;
        static Rect _top, _left, _right, _bottom, _status, _preview; // pixels, top left origin

        // palette
        static int _source; // 0 characters here, 1 other levels, 2 props
        static List<Spawner.Source> _items = new List<Spawner.Source>();
        static List<Spawner.Source> _shown = new List<Spawner.Source>();
        static string _search = "", _lastSearch = null;
        static int _page, _lastSource = -1, _catalogSeen = -1;
        static Spawner.Source _hover;
        static float _hoverSince;

        // placing
        static GameObject _ghost;
        static string _ghostName;
        static string _ghostFrom;
        static GameObject _ghostSource;
        static readonly List<Collider> _ghostColliders = new List<Collider>();
        static float _ghostYaw;

        // selection and mouse tools
        static Transform _sel;
        static bool _grab;
        static string _underMouse;
        static float _nextNpcScan;
        static readonly List<NPC> _npcs = new List<NPC>();
        static bool _pressed, _dragging;
        static Vector2 _pressMouse;
        static Transform _pressPick;
        static Vector3 _dragPos, _dragScale, _dragOffset;
        static Quaternion _dragRot;

        // time
        static bool _paused, _slow;

        // fps
        static float _fps, _fpsTime;
        static int _fpsFrames;

        public static void Toggle() { if (Active) Disable(); else Enable(); }

        public static void Enable()
        {
            if (!Freecam.Active) Freecam.Enable();
            if (!Freecam.Active) return;
            Active = true;
            ToolkitBehaviour.Instance?.CloseMenu();
            _lastSource = -1;
            _rowsDirty = true;
            ToolkitBehaviour.Toast("Dev view on. Hold right mouse to look around");
        }

        public static void Disable()
        {
            if (!Active) return;
            CancelPlace();
            _grab = false; _dragging = false; _pressed = false;
            Active = false;
            Typing = false;
            if (_paused || _slow) { _paused = _slow = false; Time.timeScale = 1f; }
            NpcPreview.Hide();
            Freecam.Disable();
            ToolkitBehaviour.Toast("Dev view off");
        }

        public static Transform Selected => _sel;
        public static void FocusOn(Transform t) { if (t != null) Focus(t); }

        // used by the asset manager: place copies of any object, a mesh template included
        static Mesh _ghostMesh;
        static IList<Material> _ghostMats;
        internal static void PlaceFrom(GameObject go, string name, Mesh mesh, IList<Material> mats)
        {
            if (go == null) return;
            StartPlace(new Spawner.Source { Go = go, Name = name });
            _ghostMesh = mesh; _ghostMats = mats;
            if (_ghost != null) ToolkitBehaviour.Toast("Click in the world to place " + name);
        }

        public static void Select(Transform t)
        {
            _sel = t;
            if (t != null) Reveal(t);
        }

        // the pick key while moving or placing something lets go of it
        public static bool DropHeld()
        {
            if (!Active) return false;
            if (_ghost != null) { Place(); _sel = null; return true; }
            if (_grab && _sel != null)
            {
                _grab = false;
                SceneEditor.Remember(_sel);
                ToolkitBehaviour.Toast("Dropped " + _sel.name);
                _sel = null;
                return true;
            }
            return false;
        }

        static Camera Cam => Game.MainCamera;

        static bool MouseRay(out Ray ray)
        {
            ray = default;
            var cam = Cam;
            if (cam == null) return false;
            ray = cam.ScreenPointToRay(InputHelper.MousePosition());
            return true;
        }

        static bool MouseGround(out Vector3 point)
        {
            point = default;
            if (!MouseRay(out var ray)) return false;
            if (Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            point = ray.origin + ray.direction * 6f;
            return true;
        }

        // ---------------------------------------------------------------- undo

        struct Undo { public Transform T; public Vector3 Pos, Scale; public Quaternion Rot; public bool Active; public float Time; }
        static readonly List<Undo> _undo = new List<Undo>();

        static void Push(Transform t)
        {
            if (t == null) return;
            if (_undo.Count > 0)
            {
                var top = _undo[_undo.Count - 1];
                // a run of small changes to the same thing is one step
                if (top.T == t && Time.unscaledTime - top.Time < 0.6f) { top.Time = Time.unscaledTime; _undo[_undo.Count - 1] = top; return; }
            }
            _undo.Add(new Undo { T = t, Pos = t.position, Rot = t.rotation, Scale = t.localScale, Active = t.gameObject.activeSelf, Time = Time.unscaledTime });
            if (_undo.Count > 60) _undo.RemoveAt(0);
        }

        static void DoUndo()
        {
            while (_undo.Count > 0)
            {
                var u = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
                if (u.T == null) continue;
                u.T.SetPositionAndRotation(u.Pos, u.Rot);
                u.T.localScale = u.Scale;
                u.T.gameObject.SetActive(u.Active);
                SceneEditor.Remember(u.T);
                _sel = u.T;
                ToolkitBehaviour.Toast("Undid a change to " + u.T.name);
                return;
            }
            ToolkitBehaviour.Toast("Nothing to undo");
        }

        static void Changed(Transform t) => SceneEditor.Remember(t);

        // ---------------------------------------------------------------- update

        public static void Update()
        {
            if (!Active) return;
            if (!Freecam.Active) { Active = false; CancelPlace(); return; }

            _fpsFrames++;
            _fpsTime += Time.unscaledDeltaTime;
            if (_fpsTime > 0.5f) { _fps = _fpsFrames / _fpsTime; _fpsFrames = 0; _fpsTime = 0f; }
            if (_paused) Time.timeScale = 0f;
            else if (_slow) Time.timeScale = 0.25f;

            var m = InputHelper.MousePosition();
            var gm = new Vector2(m.x, Screen.height - m.y);
            OverUi = _top.Contains(gm) || _left.Contains(gm) || _right.Contains(gm) || _bottom.Contains(gm)
                || _status.Contains(gm) || _preview.Contains(gm) || ToolkitBehaviour.IsOpen;

            if (Time.unscaledTime > _nextNpcScan)
            {
                _nextNpcScan = Time.unscaledTime + 2f;
                _npcs.Clear();
                Game.Try("Dev view npcs", () =>
                {
                    foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>()) if (n != null) _npcs.Add(n);
                });
            }

            if (Typing)
            {
                if (InputHelper.Down(KeyCode.Escape) || InputHelper.Down(KeyCode.Return)) Unfocus();
                return;
            }

            if (InputHelper.Down(KeyCode.Escape))
            {
                if (_ghost != null) CancelPlace();
                else if (_grab) _grab = false;
                else if (_dragging) EndDrag(true);
                else _sel = null;
            }

            if (_ghost != null) { UpdatePlacing(); return; }

            if (_grab && _sel != null)
            {
                if (MouseGround(out var p)) _sel.position = Snap(p);
                if (InputHelper.Down(KeyCode.R)) _sel.Rotate(0, 15f, 0, Space.World);
                if (InputHelper.LeftMouseDown() || InputHelper.Down(KeyCode.G)) { _grab = false; Changed(_sel); }
                return;
            }

            bool ctrl = InputHelper.Held(KeyCode.LeftControl) || InputHelper.Held(KeyCode.RightControl);
            if (ctrl && InputHelper.Down(KeyCode.Z)) DoUndo();
            if (ctrl && InputHelper.Down(KeyCode.S)) SceneEditor.Save();
            if (InputHelper.Down(KeyCode.Alpha1)) _tool = Tool.Select;
            if (InputHelper.Down(KeyCode.Alpha2)) _tool = Tool.Move;
            if (InputHelper.Down(KeyCode.Alpha3)) _tool = Tool.Rotate;
            if (InputHelper.Down(KeyCode.Alpha4)) _tool = Tool.Scale;
            if (InputHelper.Down(KeyCode.F) && _sel != null) Focus(_sel);
            if (InputHelper.Down(KeyCode.P)) TogglePause();

            if (_pressed)
            {
                if (!InputHelper.LeftMouseHeld())
                {
                    _pressed = false;
                    if (_dragging) EndDrag(false);
                    else { _sel = _pressPick; if (_sel != null) { Reveal(_sel); ToolkitBehaviour.Toast("Selected " + _sel.name); } }
                }
                else if (!_dragging && _tool != Tool.Select && _sel != null && (InputHelper.MousePosition() - _pressMouse).magnitude > 4f) BeginDrag();
                if (_dragging) UpdateDrag();
                return;
            }

            _underMouse = null;
            if (!OverUi)
            {
                var t = Pick();
                _underMouse = t != null ? t.name : null;
                if (InputHelper.LeftMouseDown())
                {
                    Unfocus();
                    _pressed = true;
                    _pressMouse = InputHelper.MousePosition();
                    // with a tool picked, dragging works on what is selected, a click still selects
                    _pressPick = t;
                }
            }

            if (_sel != null)
            {
                if (InputHelper.Down(KeyCode.G)) { Push(_sel); _grab = true; }
                if (InputHelper.Down(KeyCode.Delete)) Delete();
                if (ctrl && InputHelper.Down(KeyCode.D)) Duplicate();
            }
        }

        static void Unfocus()
        {
            try { GUIUtility.keyboardControl = 0; } catch { }
            Typing = false;
        }

        static void TogglePause()
        {
            _paused = !_paused;
            if (!_paused) Time.timeScale = _slow ? 0.25f : 1f;
            ToolkitBehaviour.Toast(_paused ? "Game paused, the camera still moves" : "Game running");
        }

        static Vector3 Snap(Vector3 p)
        {
            if (!_snap) return p;
            const float g = 0.25f;
            return new Vector3(Mathf.Round(p.x / g) * g, p.y, Mathf.Round(p.z / g) * g);
        }

        static void BeginDrag()
        {
            _dragging = true;
            Push(_sel);
            _dragPos = _sel.position; _dragRot = _sel.rotation; _dragScale = _sel.localScale;
            _dragOffset = Vector3.zero;
            if (_tool == Tool.Move && MouseRay(out var ray) && new Plane(Vector3.up, _dragPos).Raycast(ray, out var d))
                _dragOffset = ray.GetPoint(d) - _dragPos;
        }

        static void UpdateDrag()
        {
            if (_sel == null) { _dragging = false; return; }
            var delta = InputHelper.MousePosition() - _pressMouse;
            bool shift = InputHelper.Held(KeyCode.LeftShift);
            switch (_tool)
            {
                case Tool.Move:
                    if (shift)
                    {
                        // up and down, faster the further away it is
                        float dist = Vector3.Distance(Freecam.Position, _dragPos);
                        var p = _dragPos + Vector3.up * delta.y * 0.0025f * Mathf.Max(1f, dist);
                        if (_snap) p.y = Mathf.Round(p.y / 0.25f) * 0.25f;
                        _sel.position = p;
                    }
                    else if (MouseRay(out var ray) && new Plane(Vector3.up, _dragPos).Raycast(ray, out var d))
                    {
                        var p = ray.GetPoint(d) - _dragOffset;
                        p.y = _dragPos.y;
                        _sel.position = Snap(p);
                    }
                    break;
                case Tool.Rotate:
                    float a = delta.x * 0.5f;
                    if (_snap) a = Mathf.Round(a / 15f) * 15f;
                    _sel.rotation = shift
                        ? Quaternion.AngleAxis(a, Freecam.Rotation * Vector3.right) * _dragRot
                        : Quaternion.AngleAxis(a, Vector3.up) * _dragRot;
                    break;
                case Tool.Scale:
                    float k = Mathf.Exp(delta.x * 0.005f);
                    if (_snap) k = Mathf.Max(0.1f, Mathf.Round(k * 10f) / 10f);
                    _sel.localScale = _dragScale * k;
                    break;
            }
        }

        static void EndDrag(bool cancel)
        {
            _dragging = false;
            _pressed = false;
            if (_sel == null) return;
            if (cancel) { _sel.SetPositionAndRotation(_dragPos, _dragRot); _sel.localScale = _dragScale; }
            Changed(_sel);
        }

        static void Focus(Transform t)
        {
            var b = BoundsOf(t);
            float r = Mathf.Max(b.extents.magnitude, 0.5f);
            var dir = Freecam.Rotation * Vector3.forward;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            var pos = b.center - dir * r * 2.5f + Vector3.up * r * 0.3f;
            Freecam.MoveTo(pos, Quaternion.LookRotation(dir).eulerAngles.y);
        }

        static Bounds BoundsOf(Transform t)
        {
            Bounds b = default;
            bool any = false;
            try
            {
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            }
            catch { }
            return any ? b : new Bounds(t.position + Vector3.up * 0.5f, Vector3.one);
        }

        // what is under the mouse: characters first (by their visible bounds), then anything with a collider
        static Transform Pick()
        {
            if (!MouseRay(out var ray)) return null;
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var n in _npcs)
            {
                if (n == null) continue;
                try
                {
                    foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>())
                        if (r.bounds.IntersectRay(ray, out var d) && d < bestDist) { bestDist = d; best = n.transform; }
                }
                catch { }
            }
            foreach (var g in Spawner.Spawned)
            {
                if (g == null) continue;
                try
                {
                    foreach (var r in g.GetComponentsInChildren<Renderer>())
                        if (r.bounds.IntersectRay(ray, out var d) && d < bestDist) { bestDist = d; best = g.transform; }
                }
                catch { }
            }
            if (Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore) && hit.distance < bestDist - 0.3f)
            {
                var t = hit.collider.transform;
                var rb = hit.collider.attachedRigidbody;
                if (rb != null) t = rb.transform;
                best = t;
            }
            return best;
        }

        // ---------------------------------------------------------------- placing, copying, deleting

        static void StartPlace(Spawner.Source s)
        {
            CancelPlace();
            if (s?.Go == null) return;
            Game.Try("Place ghost", () =>
            {
                bool was = s.Go.activeSelf;
                if (was) s.Go.SetActive(false);
                _ghost = UnityEngine.Object.Instantiate(s.Go.Cast<UnityEngine.Object>()).Cast<GameObject>();
                if (was) s.Go.SetActive(true);
                if (_ghost.transform.parent != null) _ghost.transform.SetParent(null, true);
                _ghost.hideFlags = HideFlags.None;
                _ghost.name = s.Name + " (toolkit)";
                _ghostFrom = CharacterCatalog.FromOf(s.Go) ?? "here";
                _ghostSource = s.Go.scene.IsValid() && s.Go.transform.root.name != "ToolkitTemplates" ? s.Go : null;
                _ghostMesh = null; _ghostMats = null;
                Spawner.MakeStill(_ghost);
                _ghostColliders.Clear();
                foreach (var c in _ghost.GetComponentsInChildren<Collider>(true))
                    if (c.enabled) { c.enabled = false; _ghostColliders.Add(c); }
                _ghost.SetActive(true);
                _ghostName = s.Name;
                var cam = Cam;
                _ghostYaw = cam != null ? cam.transform.eulerAngles.y + 180f : 0f;
            });
        }

        static void UpdatePlacing()
        {
            if (_ghost == null) return;
            if (MouseGround(out var p)) _ghost.transform.position = Snap(p);
            if (InputHelper.Down(KeyCode.R)) _ghostYaw += 15f;
            _ghost.transform.rotation = Quaternion.Euler(0, _ghostYaw, 0);
            if (!OverUi && InputHelper.LeftMouseDown())
            {
                var name = _ghostName;
                Place();
                ToolkitBehaviour.Toast("Placed " + name + ". Shift+click to keep placing");
                // hold shift to drop several in a row
                if (InputHelper.Held(KeyCode.LeftShift))
                {
                    var src = _items.FirstOrDefault(i => i.Name == name);
                    if (src != null) StartPlace(src);
                }
            }
        }

        static void Place()
        {
            if (_ghost == null) return;
            var placed = _ghost;
            foreach (var c in _ghostColliders) if (c != null) c.enabled = true;
            _ghostColliders.Clear();
            if (!Spawner.StayStill) Game.Try("Wake clone", () => { foreach (var r in placed.GetComponentsInChildren<RoutineController>(true)) r.enabled = true; });
            Spawner.Track(placed, _ghostFrom);
            if (_ghostSource != null) SavedObjects.NoteSource(placed, _ghostSource);
            if (_ghostMesh != null) SavedObjects.NoteMesh(placed, _ghostMesh, _ghostMats);
            if (SavedObjects.AutoKeep) SavedObjects.Keep(placed);
            _sel = placed.transform;
            Reveal(_sel);
            _ghost = null;
        }

        static void CancelPlace()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost);
            _ghost = null;
            _ghostColliders.Clear();
        }

        static void Delete()
        {
            if (_sel == null) return;
            var go = _sel.gameObject;
            if (go.name.EndsWith("(toolkit)"))
            {
                // things you made are really gone, and leave the saved list too
                SavedObjects.Forget(go);
                ToolkitBehaviour.Toast("Deleted " + go.name);
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                // things from the level are hidden, so Ctrl+Z and the saved layout still work
                Push(_sel);
                go.SetActive(false);
                Changed(_sel);
                ToolkitBehaviour.Toast("Removed " + go.name + " (Ctrl+Z brings it back)");
            }
            _sel = null;
            _grab = false;
            _rowsDirty = true;
        }

        static void Duplicate()
        {
            if (_sel == null) return;
            var go = _sel.gameObject;
            Game.Try("Duplicate", () =>
            {
                var clone = UnityEngine.Object.Instantiate(go.Cast<UnityEngine.Object>(), _sel.position + _sel.right, _sel.rotation).Cast<GameObject>();
                clone.name = go.name.EndsWith("(toolkit)") ? go.name : go.name + " (toolkit)";
                if (_sel.parent != null) clone.transform.SetParent(_sel.parent, true);
                Spawner.Track(clone, Spawner.OriginOf(go));
                SavedObjects.NoteSource(clone, go);
                if (SavedObjects.AutoKeep) SavedObjects.Keep(clone);
                _sel = clone.transform;
                _grab = true;
                _rowsDirty = true;
            });
        }

        // ---------------------------------------------------------------- drawing

        static Rect Px(Rect r, float s) => new Rect(r.x * s, r.y * s, r.width * s, r.height * s);

        static void Panel(Rect r)
        {
            GUI.Box(r, "");
            GUI.Box(r, "");
        }

        public static void Draw(float scale)
        {
            if (!Active) { _top = _left = _right = _bottom = _status = _preview = Rect.zero; return; }
            float sw = Screen.width / scale, sh = Screen.height / scale;

            DrawSelectionBox();
            DrawGizmo();

            const float topH = 30f, statusH = 24f, side = 6f;
            float bottomH = !_bottomOpen ? 28f : _bottomTab == 1 && _tallAssets ? sh * 0.5f : Mathf.Min(220f, sh * 0.28f);

            var top = new Rect(0, 0, sw, topH);
            var left = new Rect(side, topH + 4, 310, sh - topH - statusH - 10);
            var right = new Rect(sw - 340 - side, topH + 4, 340, sh - topH - statusH - 10);
            var bottom = new Rect(left.xMax + 6, sh - statusH - 4 - bottomH, right.x - left.xMax - 12, bottomH);
            var status = new Rect(0, sh - statusH, sw, statusH);

            DrawToolbar(top);
            DrawLeft(left);
            DrawInspector(right);
            DrawBottom(bottom);
            DrawStatus(status);

            _top = Px(top, scale); _left = Px(left, scale); _right = Px(right, scale);
            _bottom = Px(bottom, scale); _status = Px(status, scale);

            if (NpcPreview.Showing && !ToolkitBehaviour.IsOpen)
            {
                NpcPreview.Draw(left);
                _preview = Px(new Rect(left.xMax + 8, left.y, 300, 498), scale);
            }
            else _preview = Rect.zero;

            if (_underMouse != null && _ghost == null && !_grab && !_dragging && !OverUi)
            {
                var m = InputHelper.MousePosition();
                GUI.Label(new Rect(m.x / scale + 16, (Screen.height - m.y) / scale + 8, 400, 22), _underMouse);
            }

            try { Typing = GUIUtility.keyboardControl != 0; } catch { Typing = false; }
        }

        static bool ToolButton(Rect r, string text, bool on)
        {
            var old = GUI.color;
            if (on) GUI.color = new Color(0.45f, 0.75f, 1f);
            bool hit = GUI.Button(r, text);
            GUI.color = old;
            return hit;
        }

        static void DrawToolbar(Rect area)
        {
            Panel(area);
            float x = area.x + 6, y = area.y + 4, h = area.height - 8;
            Rect Next(float w) { var r = new Rect(x, y, w, h); x += w + 3; return r; }

            if (GUI.Button(Next(96), "Save layout")) SceneEditor.Save();
            if (GUI.Button(Next(96), "Load layout")) SceneEditor.Load();
            x += 10;
            if (ToolButton(Next(74), "Select 1", _tool == Tool.Select)) _tool = Tool.Select;
            if (ToolButton(Next(74), "Move 2", _tool == Tool.Move)) _tool = Tool.Move;
            if (ToolButton(Next(74), "Rotate 3", _tool == Tool.Rotate)) _tool = Tool.Rotate;
            if (ToolButton(Next(74), "Scale 4", _tool == Tool.Scale)) _tool = Tool.Scale;
            if (ToolButton(Next(60), "Snap", _snap)) _snap = !_snap;
            x += 10;
            if (ToolButton(Next(70), _paused ? "Play P" : "Pause P", _paused)) TogglePause();
            if (ToolButton(Next(60), "0.25x", _slow)) { _slow = !_slow; if (!_slow && !_paused) Time.timeScale = 1f; }
            if (GUI.Button(Next(70), "Undo")) DoUndo();
            x += 10;
            if (GUI.Button(Next(110), Spawner.StayStill ? "Placed: still" : "Placed: alive")) Spawner.ToggleStill();
            x += 10;
            if (GUI.Button(Next(170), $"Save {AreaSizes[_area]:0} m around as OBJ")) ObjExport.ExportArea(Freecam.Position, AreaSizes[_area]);
            if (GUI.Button(Next(30), "+")) _area = (_area + 1) % AreaSizes.Length;

            var close = new Rect(area.xMax - 66, y, 60, h);
            if (GUI.Button(close, "Close")) Disable();
            var info = new Rect(x + 10, y, close.x - x - 20, h);
            var old = GUI.color;
            GUI.color = new Color(0.8f, 0.85f, 0.9f);
            GUI.Label(info, $"{TravelTab.SceneName}    {_fps:0} fps    time {Time.timeScale:0.##}x    {SavedObjects.Count} saved objects");
            GUI.color = old;
        }

        static void DrawStatus(Rect area)
        {
            Panel(area);
            string help = _ghost != null
                ? $"Placing {_ghostName}  |  click to drop, Shift+click to drop more, R turn, {Plugin.PickKey} drop here, Esc cancel"
                : _grab ? $"Moving {_sel?.name}  |  click, G or {Plugin.PickKey} to drop, R turn, Esc stop"
                : _dragging ? "Drag to change it, hold Shift for up and down (or tilt), Esc puts it back"
                : $"Right mouse look, WASD Q E fly  |  click select, 1 2 3 4 tools then drag, G grab, F focus, Del delete, Ctrl+D copy, Ctrl+Z undo, Ctrl+S save  |  {Plugin.DevViewKey} leave";
            GUI.Label(new Rect(area.x + 8, area.y + 2, area.width - 16, area.height - 4), help);
        }

        // ---------------------------------------------------------------- left: hierarchy and spawn

        static void DrawLeft(Rect area)
        {
            Panel(area);
            Gui.Begin(new Rect(area.x + 6, area.y + 6, area.width - 12, area.height - 12));
            _leftTab = Gui.Tabs(new[] { "Hierarchy", "Spawn" }, _leftTab, Gui.TabKind.Main);
            if (_leftTab == 0) DrawHierarchy();
            else DrawPalette();
        }

        class Row { public Transform T; public int Depth; public bool Kids; public string Scene; public int Handle; public string Label; }
        static List<Row> _rows = new List<Row>();
        static readonly HashSet<IntPtr> _open = new HashSet<IntPtr>();
        static readonly HashSet<int> _openScenes = new HashSet<int>();
        static bool _rowsDirty = true;
        static float _nextRows;
        static string _hSearch = "", _hLastSearch = "";
        static Vector2 _hScroll;
        static int _scrollToRow = -1;
        const float RowH = 20f;

        static void Reveal(Transform t)
        {
            try
            {
                _openScenes.Add(t.gameObject.scene.handle);
                for (var p = t.parent; p != null; p = p.parent) _open.Add(p.Pointer);
                _rowsDirty = true;
                _revealTarget = t;
            }
            catch { }
        }
        static Transform _revealTarget;

        static void BuildRows()
        {
            var rows = new List<Row>();
            Game.Try("Hierarchy", () =>
            {
                bool search = _hSearch.Trim().Length >= 2;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var sc = SceneManager.GetSceneAt(i);
                    if (!sc.isLoaded) continue;
                    var roots = sc.GetRootGameObjects();
                    if (search)
                    {
                        foreach (var r in roots)
                        {
                            if (r == null) continue;
                            foreach (var t in r.GetComponentsInChildren<Transform>(true))
                            {
                                if (t == null || !Gui.Contains(t.name, _hSearch)) continue;
                                rows.Add(new Row { T = t, Depth = 0, Label = t.name + "   " + sc.name });
                                if (rows.Count >= 400) return;
                            }
                        }
                        continue;
                    }
                    rows.Add(new Row { Scene = sc.name, Handle = sc.handle, Kids = roots.Length > 0 });
                    if (!_openScenes.Contains(sc.handle)) continue;
                    foreach (var r in roots) if (r != null) Add(rows, r.transform, 1);
                    if (rows.Count > 6000) return;
                }
            });
            _rows = rows;
            if (_revealTarget != null)
            {
                int idx = _rows.FindIndex(r => r.T == _revealTarget);
                if (idx >= 0) _scrollToRow = idx;
                _revealTarget = null;
            }
        }

        static void Add(List<Row> rows, Transform t, int depth)
        {
            int n = t.childCount;
            rows.Add(new Row { T = t, Depth = depth, Kids = n > 0 });
            if (n == 0 || !_open.Contains(t.Pointer) || rows.Count > 6000) return;
            for (int i = 0; i < n; i++) Add(rows, t.GetChild(i), depth + 1);
        }

        static void DrawHierarchy()
        {
            _hSearch = Gui.Search(_hSearch);
            var r = Gui.Row(1f, 1f, 1f);
            if (GUI.Button(r[0], "Refresh")) _rowsDirty = true;
            if (GUI.Button(r[1], "Fold all")) { _open.Clear(); _openScenes.Clear(); _rowsDirty = true; }
            if (GUI.Button(r[2], "Show selected") && _sel != null) Reveal(_sel);
            if (_hSearch != _hLastSearch) { _hLastSearch = _hSearch; _rowsDirty = true; _hScroll = Vector2.zero; }
            if (_rowsDirty || Time.unscaledTime > _nextRows) { _rowsDirty = false; _nextRows = Time.unscaledTime + 1.5f; BuildRows(); }

            var list = new Rect(Gui.X, Gui.Y, Gui.W, Gui.Bottom - Gui.Y);
            var view = new Rect(0, 0, list.width - 18, Math.Max(list.height, _rows.Count * RowH));
            if (_scrollToRow >= 0)
            {
                float y = _scrollToRow * RowH;
                if (y < _hScroll.y || y > _hScroll.y + list.height - RowH * 2) _hScroll.y = Mathf.Max(0, y - list.height / 3f);
                _scrollToRow = -1;
            }
            _hScroll = GUI.BeginScrollView(list, _hScroll, view);
            int first = Math.Max(0, (int)(_hScroll.y / RowH));
            int last = Math.Min(_rows.Count, first + (int)(list.height / RowH) + 2);
            var old = GUI.color;
            for (int i = first; i < last; i++)
            {
                var row = _rows[i];
                float y = i * RowH, x = row.Depth * 12f;
                if (row.T == null && row.Scene != null)
                {
                    bool open = _openScenes.Contains(row.Handle);
                    GUI.color = new Color(1f, 0.75f, 0.35f);
                    if (GUI.Button(new Rect(0, y, view.width, RowH - 1), (open ? "[-] " : "[+] ") + "Scene: " + row.Scene))
                    {
                        if (!_openScenes.Remove(row.Handle)) _openScenes.Add(row.Handle);
                        _rowsDirty = true;
                    }
                    GUI.color = old;
                    continue;
                }
                if (row.T == null) continue;
                if (row.Kids && GUI.Button(new Rect(x, y, 22, RowH - 1), _open.Contains(row.T.Pointer) ? "-" : "+"))
                {
                    if (!_open.Remove(row.T.Pointer)) _open.Add(row.T.Pointer);
                    _rowsDirty = true;
                }
                bool active = false;
                try { active = row.T.gameObject.activeInHierarchy; } catch { }
                GUI.color = row.T == _sel ? new Color(0.45f, 0.75f, 1f) : active ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                string name;
                try { name = row.Label ?? row.T.name; } catch { name = "?"; }
                if (GUI.Button(new Rect(x + 24, y, view.width - x - 24, RowH - 1), name)) { _sel = row.T; }
                GUI.color = old;
            }
            GUI.EndScrollView();
            if (_rows.Count == 0) GUI.Label(new Rect(list.x, list.y, list.width, 22), _hSearch.Trim().Length >= 2 ? "Nothing found" : "No scenes loaded");
        }

        static void DrawPalette()
        {
            var r = Gui.Row(3);
            string[] names = { "Here", "Other levels", "Props" };
            for (int i = 0; i < 3; i++)
                if (Gui.Pick(r[i], names[i], _source == i, Gui.TabKind.Choice)) _source = i;

            if (_source != _lastSource || (_source == 1 && _catalogSeen != CharacterCatalog.Entries.Count))
            {
                _lastSource = _source;
                _catalogSeen = CharacterCatalog.Entries.Count;
                _items = Spawner.Gather(_source);
                _lastSearch = null;
            }
            if (_source == 1 && _items.Count == 0)
                Gui.Label("Copy characters from a level in the Spawner tab first.");

            if (Gui.Button("Rescan")) { _items = Spawner.Gather(_source); _lastSearch = null; }
            _search = Gui.Search(_search);
            if (_search != _lastSearch)
            {
                _lastSearch = _search;
                _shown = _items.Where(s => Gui.Contains(s.Name, _search)).ToList();
                _page = 0;
            }

            int rows = Math.Max(5, (int)((Gui.Bottom - Gui.Y - 60) / (Gui.LineH + Gui.Gap)));
            int pick = Gui.List(_shown, ref _page, s => s.Name, rows);
            if (pick >= 0) StartPlace(_shown[pick]);

            // rest the mouse on a character to see it in 3D
            var ev = Event.current;
            if (ev != null && ev.type == EventType.Repaint && _source != 2 && Plugin.PreviewOnHover.Value)
            {
                var h = Gui.Hovered >= 0 && Gui.Hovered < _shown.Count ? _shown[Gui.Hovered] : null;
                if (h != _hover) { _hover = h; _hoverSince = Time.unscaledTime; }
                else if (h != null && h.Go != null && NpcPreview.Source != h.Go && Time.unscaledTime - _hoverSince > 0.3f)
                    NpcPreview.Show(h.Go, h.Name);
            }
            Gui.Label("Click a name, then click in the world to place it.");
        }

        // ---------------------------------------------------------------- right: inspector

        static readonly Dictionary<string, string> _buf = new Dictionary<string, string>();
        static Vector2 _iScroll;
        static float _iHeight = 800f;
        static readonly float[] Steps = { 0.01f, 0.1f, 0.25f, 1f, 5f };
        static int _step = 2;

        static bool Field(Rect r, string id, float v, out float nv)
        {
            nv = v;
            string focused = null;
            try { focused = GUI.GetNameOfFocusedControl(); } catch { }
            bool has = focused == id;
            string shown = has && _buf.TryGetValue(id, out var b) ? b : v.ToString("0.###", Inv);
            GUI.SetNextControlName(id);
            string ns = Gui.TextField(r, shown);
            if (ns == shown) return false;
            _buf[id] = ns;
            if (float.TryParse(ns, NumberStyles.Float, Inv, out var f) && Math.Abs(f - v) > 0.00001f) { nv = f; return true; }
            return false;
        }

        static void Vec(string label, string id, Vector3 v, Action<Vector3> set)
        {
            var r = Gui.Row(0.9f, 2f, 2f, 2f);
            GUI.Label(r[0], label);
            for (int i = 0; i < 3; i++)
            {
                if (Field(r[i + 1], id + i, v[i], out var nv))
                {
                    var c = v; c[i] = nv;
                    set(c);
                }
            }
        }

        static void DrawInspector(Rect area)
        {
            Panel(area);
            var t = _sel;
            var inner = new Rect(area.x + 6, area.y + 6, area.width - 12, area.height - 12);
            if (t == null)
            {
                Gui.Begin(inner);
                Gui.Header("Inspector");
                Gui.Label("Nothing selected.");
                Gui.Label("Click something in the world or in the Hierarchy.");
                Gui.Space(8);
                Gui.Header("This level");
                Gui.Label($"Placed by you: {Spawner.Spawned.Count(g => g != null)}");
                Gui.Label($"Saved objects here: {SavedObjects.Count}");
                Gui.Label($"Characters around: {_npcs.Count}");
                Gui.Space(8);
                Gui.Label("Spawn tab on the left puts new things in.");
                Gui.Label("Keep forever in the inspector makes them stay.");
                return;
            }

            var view = new Rect(0, 0, inner.width - 18, Math.Max(inner.height, _iHeight));
            _iScroll = GUI.BeginScrollView(inner, _iScroll, view);
            Gui.Begin(new Rect(0, 0, view.width, 100000f));
            try { DrawInspectorBody(t); }
            catch (Exception e) { Gui.Label("Can't show this one: " + e.GetType().Name); }
            _iHeight = Gui.Y + 10f;
            GUI.EndScrollView();
        }

        static void DrawInspectorBody(Transform t)
        {
            var go = t.gameObject;
            var npc = go.GetComponent<NPC>();

            // name and active, like the top of an editor inspector
            var r = Gui.Row(0.6f, 4f);
            if (GUI.Button(r[0], go.activeSelf ? "[x]" : "[ ]")) { Push(t); go.SetActive(!go.activeSelf); Changed(t); }
            string focused = null;
            try { focused = GUI.GetNameOfFocusedControl(); } catch { }
            string shownName = focused == "insp_name" && _buf.TryGetValue("insp_name", out var bn) ? bn : go.name;
            GUI.SetNextControlName("insp_name");
            var nn = Gui.TextField(r[1], shownName);
            if (nn != shownName) { _buf["insp_name"] = nn; if (nn.Trim().Length > 0) go.name = nn; }

            bool ours = go.name.EndsWith("(toolkit)");
            bool kept = SavedObjects.IsKept(go);
            string what = npc != null ? "Character" : "Object";
            Gui.Label($"{what} in {go.scene.name}, layer {LayerMask.LayerToName(go.layer)}" + (kept ? ", kept forever" : ours ? ", placed by you" : ""));

            r = Gui.Row(1f, 1f, 1f, 1f);
            if (GUI.Button(r[0], "Parent") && t.parent != null) { Select(t.parent); return; }
            if (GUI.Button(r[1], "Root")) { Select(t.root); return; }
            if (GUI.Button(r[2], "Focus F")) Focus(t);
            if (GUI.Button(r[3], "Deselect")) { _sel = null; return; }

            Gui.Header("Transform");
            Vec("Pos", "p", t.position, v => { Push(t); t.position = v; Changed(t); });
            Vec("Rot", "r", t.eulerAngles, v => { Push(t); t.eulerAngles = v; Changed(t); });
            Vec("Size", "s", t.localScale, v => { Push(t); t.localScale = v; Changed(t); });
            r = Gui.Row(1.2f, 1f, 1f, 1f, 1f, 1f);
            GUI.Label(r[0], "Step");
            for (int i = 0; i < Steps.Length; i++)
                if (Gui.Pick(r[i + 1], Steps[i].ToString("0.##", Inv), _step == i, Gui.TabKind.Choice)) _step = i;
            float st = Steps[_step];
            r = Gui.Row(6);
            string[] ax = { "X-", "X+", "Y-", "Y+", "Z-", "Z+" };
            Vector3[] dirs = { Vector3.left, Vector3.right, Vector3.down, Vector3.up, Vector3.back, Vector3.forward };
            for (int i = 0; i < 6; i++)
                if (GUI.Button(r[i], ax[i])) { Push(t); t.position += dirs[i] * st; Changed(t); }
            r = Gui.Row(5);
            float[] turns = { -90, -15, 15, 90 };
            for (int i = 0; i < 4; i++)
                if (GUI.Button(r[i], (turns[i] > 0 ? "+" : "") + turns[i])) { Push(t); t.Rotate(0, turns[i], 0, Space.World); Changed(t); }
            if (GUI.Button(r[4], "Face me"))
            {
                var d = Freecam.Position - t.position; d.y = 0;
                if (d.sqrMagnitude > 0.001f) { Push(t); t.rotation = Quaternion.LookRotation(d); Changed(t); }
            }
            r = Gui.Row(1f, 1f, 1f, 1f);
            if (GUI.Button(r[0], "Size -")) { Push(t); t.localScale *= 0.9f; Changed(t); }
            if (GUI.Button(r[1], "Size +")) { Push(t); t.localScale *= 1.1f; Changed(t); }
            if (GUI.Button(r[2], "Reset turn")) { Push(t); t.rotation = Quaternion.Euler(0, t.eulerAngles.y, 0); Changed(t); }
            if (GUI.Button(r[3], "Reset size")) { Push(t); t.localScale = Vector3.one; Changed(t); }
            r = Gui.Row(1f, 1f, 1f);
            if (GUI.Button(r[0], _grab ? "[ Moving ]" : "Grab G")) { if (!_grab) Push(t); _grab = !_grab; }
            if (GUI.Button(r[1], "Drop to ground"))
                if (Physics.Raycast(t.position + Vector3.up * 0.5f, Vector3.down, out var hit, 200f)) { Push(t); t.position = hit.point; Changed(t); }
            if (GUI.Button(r[2], "Bring to me"))
            {
                var p = Freecam.Position + Freecam.Rotation * Vector3.forward * 3f;
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit2, 50f)) p = hit2.point;
                Push(t); t.position = p; Changed(t);
            }

            Gui.Space(4);
            bool hasChar = npc != null || go.GetComponentInChildren<Animator>() != null;
            bool hasFx = go.GetComponentInChildren<ParticleSystem>(true) != null;
            var tabs = new List<string> { "Actions", "Components" };
            if (hasChar) tabs.Add("Character");
            if (hasFx) tabs.Add("Effects");
            if (_inspTab >= tabs.Count) _inspTab = 0;
            _inspTab = Gui.Tabs(tabs.ToArray(), _inspTab, Gui.TabKind.Section);
            switch (tabs[_inspTab])
            {
                case "Actions": DrawActions(t, go, ours, kept); break;
                case "Components": DrawComponents(go); break;
                case "Character": DrawCharacter(t, go); break;
                case "Effects": DrawEffects(go); break;
            }
        }

        static void DrawActions(Transform t, GameObject go, bool ours, bool kept)
        {
            if (Gui.Toggle("Keep forever (comes back every time this level loads)", kept))
            {
                if (kept) { SavedObjects.Forget(go); ToolkitBehaviour.Toast(go.name + " is not kept any more"); }
                else if (!ours) ToolkitBehaviour.Toast("Copy it first (Ctrl+D), the copy can be kept. Moving level objects is saved with Save layout");
                else SavedObjects.Keep(go);
            }
            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Copy Ctrl+D")) Duplicate();
            if (GUI.Button(r[1], ours ? "Delete Del" : "Remove Del")) { Delete(); return; }
            r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Save as OBJ")) ObjExport.Export(go);
            if (GUI.Button(r[1], "Open models folder"))
            {
                System.IO.Directory.CreateDirectory(ObjExport.Folder);
                Application.OpenURL("file:///" + ObjExport.Folder.Replace('\\', '/'));
            }
            if (ObjExport.LastResult != null) Gui.Label(ObjExport.LastResult);
            r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "3D preview")) NpcPreview.Show(go, go.name);
            if (GUI.Button(r[1], "Scene editor")) { SceneEditor.Select(t); ToolkitBehaviour.Instance?.OpenTab(ToolkitBehaviour.SceneEditorTab); }
            Gui.Label("Path: " + SceneEditor.PathOf(t));
            int kids = t.childCount;
            Gui.Label($"{kids} children, {go.GetComponentsInChildren<Renderer>(true).Count} renderers");
        }

        static int _compPage;

        static void DrawComponents(GameObject go)
        {
            var comps = go.GetComponents<Component>();
            Gui.Label($"{comps.Count} components. Click one to switch it on or off.");
            var old = GUI.color;
            foreach (var c in comps)
            {
                if (c == null) continue;
                string tn;
                try { tn = c.GetIl2CppType().Name; } catch { tn = "?"; }
                var be = c.TryCast<Behaviour>();
                var re = c.TryCast<Renderer>();
                var co = c.TryCast<Collider>();
                bool? on = be != null ? be.enabled : re != null ? re.enabled : co != null ? co.enabled : (bool?)null;
                GUI.color = on == false ? new Color(0.65f, 0.65f, 0.65f) : Color.white;
                string label = (on == null ? "      " : on.Value ? "[x]  " : "[ ]  ") + tn;
                if (Gui.Button(label) && on != null)
                {
                    if (be != null) be.enabled = !on.Value;
                    else if (re != null) re.enabled = !on.Value;
                    else co.enabled = !on.Value;
                }
                GUI.color = old;
            }
            Gui.Space(4);
            Gui.Label("Children with their own parts are in the Hierarchy.");
        }

        static void DrawCharacter(Transform t, GameObject go)
        {
            bool still = IsStill(go);
            var r = Gui.Row(2);
            if (GUI.Button(r[0], (still ? "[ON] " : "[off] ") + "Stand still"))
            {
                if (still) Wake(go); else Spawner.MakeStill(go);
            }
            var talk = go.GetComponentInChildren<AudioSource>();
            if (talk != null && GUI.Button(r[1], (talk.enabled ? "[ON] " : "[off] ") + "Sounds"))
                foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = !talk.enabled;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                r = Gui.Row(2f, 1f, 1f, 1f, 1f);
                GUI.Label(r[0], $"Anim {(anim.enabled ? anim.speed.ToString("0.##") + "x" : "frozen")}");
                if (GUI.Button(r[1], "Freeze")) anim.enabled = !anim.enabled;
                if (GUI.Button(r[2], "0.5x")) { anim.enabled = true; anim.speed = 0.5f; }
                if (GUI.Button(r[3], "1x")) { anim.enabled = true; anim.speed = 1f; }
                if (GUI.Button(r[4], "2x")) { anim.enabled = true; anim.speed = 2f; }
                Poses.Draw(go, anim);
            }
        }

        static void DrawEffects(GameObject go)
        {
            var all = go.GetComponentsInChildren<ParticleSystem>(true);
            Gui.Label($"{all.Count} particle effects in here.");
            var r = Gui.Row(1f, 1f, 1f);
            if (GUI.Button(r[0], "Play")) foreach (var p in all) { p.gameObject.SetActive(true); p.Play(true); }
            if (GUI.Button(r[1], "Stop")) foreach (var p in all) p.Stop(true);
            if (GUI.Button(r[2], "Restart")) foreach (var p in all) { p.Clear(true); p.Play(true); }
            foreach (var p in all.Take(12))
            {
                if (p == null) continue;
                r = Gui.Row(3f, 1f);
                GUI.Label(r[0], (p.isPlaying ? "playing  " : "stopped  ") + p.name);
                if (GUI.Button(r[1], "Pick")) { Select(p.transform); return; }
            }
        }

        static bool IsStill(GameObject go)
        {
            try
            {
                foreach (var c in go.GetComponentsInChildren<RoutineController>(true)) if (c.enabled) return false;
                foreach (var a in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) if (a.enabled) return false;
            }
            catch { }
            return true;
        }

        static void Wake(GameObject go)
        {
            Game.Try("Wake", () =>
            {
                foreach (var c in go.GetComponentsInChildren<RoutineController>(true)) c.enabled = true;
                foreach (var c in go.GetComponentsInChildren<Routine>(true)) c.enabled = true;
                foreach (var a in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) a.enabled = true;
            });
        }

        // ---------------------------------------------------------------- bottom: console and saved objects

        static Vector2 _cScroll;
        static bool _cInfo = true, _cWarn = true, _cErr = true, _cFollow = true;
        static string _cSearch = "";
        static int _cSeen = -1;
        static List<LogConsole.Line> _cLines = new List<LogConsole.Line>();

        static void DrawBottom(Rect area)
        {
            Panel(area);
            var tabs = new Rect(area.x + 6, area.y + 4, area.width - 12, 22);
            float w = 130;
            if (Gui.Pick(new Rect(tabs.x, tabs.y, w, 22), "Console", _bottomTab == 0 && _bottomOpen, Gui.TabKind.Main)) { _bottomTab = 0; _bottomOpen = true; }
            if (Gui.Pick(new Rect(tabs.x + w + 3, tabs.y, w, 22), "Assets", _bottomTab == 1 && _bottomOpen, Gui.TabKind.Main)) { _bottomTab = 1; _bottomOpen = true; }
            if (Gui.Pick(new Rect(tabs.x + (w + 3) * 2, tabs.y, w, 22), "Saved objects", _bottomTab == 2 && _bottomOpen, Gui.TabKind.Main)) { _bottomTab = 2; _bottomOpen = true; }
            if (GUI.Button(new Rect(tabs.xMax - 70, tabs.y, 70, 22), _bottomOpen ? "Hide" : "Show")) _bottomOpen = !_bottomOpen;
            if (_bottomOpen && _bottomTab == 1 && GUI.Button(new Rect(tabs.xMax - 144, tabs.y, 70, 22), _tallAssets ? "Smaller" : "Bigger")) _tallAssets = !_tallAssets;
            if (!_bottomOpen) return;
            var inner = new Rect(area.x + 6, area.y + 30, area.width - 12, area.height - 34);
            if (_bottomTab == 0) DrawConsole(inner);
            else if (_bottomTab == 1) AssetBrowser.Draw(inner);
            else { Gui.Begin(inner); SavedObjects.Draw(); }
        }

        static void DrawConsole(Rect area)
        {
            float x = area.x;
            Rect Next(float wd) { var r = new Rect(x, area.y, wd, 20); x += wd + 3; return r; }
            if (ToolButton(Next(60), "Info", _cInfo)) { _cInfo = !_cInfo; _cSeen = -1; }
            if (ToolButton(Next(80), "Warnings", _cWarn)) { _cWarn = !_cWarn; _cSeen = -1; }
            if (ToolButton(Next(60), "Errors", _cErr)) { _cErr = !_cErr; _cSeen = -1; }
            if (ToolButton(Next(60), "Follow", _cFollow)) _cFollow = !_cFollow;
            if (GUI.Button(Next(60), "Clear")) LogConsole.Clear();
            GUI.Label(Next(50), "Search");
            var s = Gui.TextField(Next(Math.Max(80, area.xMax - x)), _cSearch);
            if (s != _cSearch) { _cSearch = s; _cSeen = -1; }

            if (_cSeen != LogConsole.Version)
            {
                _cSeen = LogConsole.Version;
                _cLines = LogConsole.Snapshot().Where(l =>
                {
                    bool err = (l.Level & (BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Fatal)) != 0;
                    bool warn = (l.Level & BepInEx.Logging.LogLevel.Warning) != 0;
                    if (err ? !_cErr : warn ? !_cWarn : !_cInfo) return false;
                    return _cSearch.Length == 0 || Gui.Contains(l.Text, _cSearch);
                }).ToList();
                if (_cFollow) _cScroll.y = float.MaxValue;
            }

            const float lh = 18f;
            var list = new Rect(area.x, area.y + 24, area.width, area.height - 24);
            var view = new Rect(0, 0, Math.Max(list.width - 18, 1400), Math.Max(list.height, _cLines.Count * lh));
            _cScroll = GUI.BeginScrollView(list, _cScroll, view);
            int first = Math.Max(0, (int)(_cScroll.y / lh));
            int last = Math.Min(_cLines.Count, first + (int)(list.height / lh) + 2);
            var old = GUI.color;
            for (int i = first; i < last; i++)
            {
                var l = _cLines[i];
                GUI.color = LogConsole.ColorOf(l.Level);
                GUI.Label(new Rect(4, i * lh, view.width - 8, lh), l.Time + "  " + l.Text.Replace('\n', ' '));
            }
            GUI.color = old;
            GUI.EndScrollView();
        }

        // ---------------------------------------------------------------- world overlays

        static float S => Mathf.Clamp(Plugin.MenuScale.Value, 0.5f, 3f);

        static bool ToGui(Camera cam, Vector3 world, out Vector2 p)
        {
            var sp = cam.WorldToScreenPoint(world);
            p = new Vector2(sp.x / S, (Screen.height - sp.y) / S);
            return sp.z > 0.05f;
        }

        static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            var m = GUI.matrix;
            var old = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = old;
        }

        // the three axes of the selected object, so you can see which way it points
        static void DrawGizmo()
        {
            if (_sel == null || _tool == Tool.Select) return;
            var cam = Cam;
            if (cam == null) return;
            try
            {
                var o = _sel.position;
                if (!ToGui(cam, o, out var a)) return;
                float len = Vector3.Distance(cam.transform.position, o) * 0.12f;
                bool local = _tool == Tool.Rotate;
                var axes = new[] { local ? _sel.right : Vector3.right, local ? _sel.up : Vector3.up, local ? _sel.forward : Vector3.forward };
                var cols = new[] { new Color(1f, 0.3f, 0.3f), new Color(0.4f, 1f, 0.4f), new Color(0.4f, 0.6f, 1f) };
                string[] names = { "X", "Y", "Z" };
                for (int i = 0; i < 3; i++)
                {
                    if (!ToGui(cam, o + axes[i] * len, out var b)) continue;
                    Line(a, b, cols[i], _dragging ? 4f : 3f);
                    var old = GUI.color;
                    GUI.color = cols[i];
                    GUI.Label(new Rect(b.x + 3, b.y - 10, 20, 20), names[i]);
                    GUI.color = old;
                }
                string hint = _tool == Tool.Move ? "drag: move, Shift: up/down" : _tool == Tool.Rotate ? "drag: turn, Shift: tilt" : "drag: size";
                GUI.Label(new Rect(a.x + 10, a.y + 6, 220, 20), hint);
            }
            catch { }
        }

        // a frame around the selected thing, worked out from its visible bounds
        static void DrawSelectionBox()
        {
            if (_sel == null) return;
            var cam = Cam;
            if (cam == null) return;
            try
            {
                var b = BoundsOf(_sel);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var sp = cam.WorldToScreenPoint(c);
                    if (sp.z < 0) return;
                    minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                    minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
                }
                float s = S;
                var rect = new Rect(minX / s, (Screen.height - maxY) / s, (maxX - minX) / s, (maxY - minY) / s);
                var old = GUI.color;
                GUI.color = _grab || _dragging ? new Color(1f, 0.8f, 0.2f) : SavedObjects.IsKept(_sel.gameObject) ? new Color(0.5f, 1f, 0.55f) : new Color(0.3f, 0.9f, 1f);
                var tex = Texture2D.whiteTexture;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), tex);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2, rect.width, 2), tex);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), tex);
                GUI.DrawTexture(new Rect(rect.xMax - 2, rect.y, 2, rect.height), tex);
                GUI.color = old;
            }
            catch { }
        }
    }
}
