using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Road96Toolkit
{
    // An editor mode on top of the freecam: a palette of characters and props on the left
    // (with the 3D preview on hover), click in the world to place or select, and an inspector
    // on the right to move, turn, scale and tweak characters.
    internal static class DevView
    {
        public static bool Active { get; private set; }

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
        static readonly List<Collider> _ghostColliders = new List<Collider>();
        static float _ghostYaw;

        // selection
        static Transform _sel;
        static bool _grab;
        static string _underMouse;
        static float _nextNpcScan;
        static readonly List<NPC> _npcs = new List<NPC>();

        // screen rects of our panels in pixels (top left origin), to know when the mouse is over them
        static Rect _left, _right, _preview;
        public static bool OverUi { get; private set; }
        public static bool BusyWithMouse => _ghost != null || _grab;

        public static void Toggle() { if (Active) Disable(); else Enable(); }

        public static void Enable()
        {
            if (!Freecam.Active) Freecam.Enable();
            if (!Freecam.Active) return;
            Active = true;
            ToolkitBehaviour.Instance?.CloseMenu();
            _lastSource = -1;
            ToolkitBehaviour.Toast("Dev view on. Hold right mouse to look around");
        }

        public static void Disable()
        {
            if (!Active) return;
            CancelPlace();
            _grab = false;
            Active = false;
            NpcPreview.Hide();
            Freecam.Disable();
            ToolkitBehaviour.Toast("Dev view off");
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

        public static void Update()
        {
            if (!Active) return;
            if (!Freecam.Active) { Active = false; CancelPlace(); return; }

            var m = InputHelper.MousePosition();
            var gm = new Vector2(m.x, Screen.height - m.y);
            OverUi = _left.Contains(gm) || _right.Contains(gm) || _preview.Contains(gm) || ToolkitBehaviour.IsOpen;

            if (Time.unscaledTime > _nextNpcScan)
            {
                _nextNpcScan = Time.unscaledTime + 2f;
                _npcs.Clear();
                Game.Try("Dev view npcs", () =>
                {
                    foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>()) if (n != null) _npcs.Add(n);
                });
            }

            if (InputHelper.Down(KeyCode.Escape)) { if (_ghost != null) CancelPlace(); else if (_grab) _grab = false; else _sel = null; }

            if (_ghost != null) { UpdatePlacing(); return; }

            if (_grab && _sel != null)
            {
                if (MouseGround(out var p)) _sel.position = p;
                if (InputHelper.Down(KeyCode.R)) _sel.Rotate(0, 15f, 0, Space.World);
                if (InputHelper.LeftMouseDown() || InputHelper.Down(KeyCode.G)) { _grab = false; SceneEditor.Remember(_sel); }
                return;
            }

            _underMouse = null;
            if (!OverUi)
            {
                var t = Pick();
                _underMouse = t != null ? t.name : null;
                if (InputHelper.LeftMouseDown()) { _sel = t; if (t != null) ToolkitBehaviour.Toast("Selected " + t.name); }
            }

            if (_sel != null)
            {
                if (InputHelper.Down(KeyCode.G)) _grab = true;
                if (InputHelper.Down(KeyCode.Delete)) Delete();
                if (InputHelper.Held(KeyCode.LeftControl) && InputHelper.Down(KeyCode.D)) Duplicate();
            }
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
                    {
                        if (r.bounds.IntersectRay(ray, out var d) && d < bestDist) { bestDist = d; best = n.transform; }
                    }
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
            if (MouseGround(out var p)) _ghost.transform.position = p;
            if (InputHelper.Down(KeyCode.R)) _ghostYaw += 15f;
            _ghost.transform.rotation = Quaternion.Euler(0, _ghostYaw, 0);
            if (!OverUi && InputHelper.LeftMouseDown())
            {
                var placed = _ghost;
                foreach (var c in _ghostColliders) if (c != null) c.enabled = true;
                _ghostColliders.Clear();
                if (!Spawner.StayStill) Game.Try("Wake clone", () => { foreach (var r in placed.GetComponentsInChildren<RoutineController>(true)) r.enabled = true; });
                Spawner.Track(placed, _ghostFrom);
                _sel = placed.transform;
                _ghost = null;
                ToolkitBehaviour.Toast("Placed " + _ghostName + ". Shift+click to keep placing");
                // hold shift to drop several in a row
                if (InputHelper.Held(KeyCode.LeftShift))
                {
                    var src = _items.FirstOrDefault(i => i.Name == _ghostName);
                    if (src != null) StartPlace(src);
                }
            }
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
            ToolkitBehaviour.Toast("Deleted " + go.name);
            UnityEngine.Object.Destroy(go);
            _sel = null;
            _grab = false;
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
                _sel = clone.transform;
                _grab = true;
            });
        }

        // ---------------------------------------------------------------- drawing

        public static void Draw(float scale)
        {
            if (!Active) { _left = _right = _preview = Rect.zero; return; }
            float sw = Screen.width / scale, sh = Screen.height / scale;

            DrawSelectionBox();

            var left = new Rect(10, 40, 320, sh - 90);
            DrawPalette(left);
            _left = Px(left, scale);

            if (_sel != null)
            {
                var right = new Rect(sw - 340, 40, 330, Mathf.Min(sh - 90, 720));
                DrawInspector(right);
                _right = Px(right, scale);
            }
            else _right = Rect.zero;

            if (NpcPreview.Showing && !ToolkitBehaviour.IsOpen)
            {
                NpcPreview.Draw(left);
                _preview = Px(new Rect(left.xMax + 8, left.y, 300, 498), scale);
            }
            else _preview = Rect.zero;

            string help = _ghost != null
                ? $"Placing {_ghostName}  |  click to drop  Shift+click drop more  R turn  Esc cancel"
                : _grab ? "Moving  |  click or G to drop  R turn  Esc stop"
                : "Dev view  |  hold right mouse to look  WASD Q E move  click select  G move  R turn  Del delete  Ctrl+D copy  " + Plugin.DevViewKey + " leave";
            GUI.Box(new Rect(sw / 2f - 360, sh - 34, 720, 24), help);
            if (_underMouse != null && _ghost == null && !_grab && !OverUi)
            {
                var m = InputHelper.MousePosition();
                GUI.Label(new Rect(m.x / scale + 16, (Screen.height - m.y) / scale + 8, 400, 22), _underMouse);
            }
        }

        static Rect Px(Rect r, float s) => new Rect(r.x * s, r.y * s, r.width * s, r.height * s);

        static void DrawPalette(Rect area)
        {
            GUI.Box(area, "");
            GUI.Box(area, "");
            Gui.Begin(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));
            Gui.Header("Spawn palette");
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

            if (Gui.Button("Rescan")) _items = Spawner.Gather(_source);
            _search = Gui.Search(_search);
            if (_search != _lastSearch)
            {
                _lastSearch = _search;
                _shown = _items.Where(s => Gui.Contains(s.Name, _search)).ToList();
                _page = 0;
            }

            int rows = Math.Max(5, (int)((Gui.Bottom - Gui.Y - 80) / (Gui.LineH + Gui.Gap)));
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
            if (Gui.Button("Leave dev view")) Disable();
        }

        static void DrawInspector(Rect area)
        {
            var t = _sel;
            if (t == null) return;
            var go = t.gameObject;
            GUI.Box(area, "");
            GUI.Box(area, "");
            Gui.Begin(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));
            Gui.Header(go.name);
            var npc = go.GetComponent<NPC>();
            Gui.Label((npc != null ? "Character" : "Object") + "  in " + go.scene.name);

            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Parent") && t.parent != null) { _sel = t.parent; return; }
            if (GUI.Button(r[1], "Root")) { _sel = t.root; return; }

            Gui.Header("Move");
            r = Gui.Row(2);
            if (GUI.Button(r[0], _grab ? "[ Moving ]" : "Move with mouse (G)")) _grab = !_grab;
            if (GUI.Button(r[1], "Drop to ground"))
                if (Physics.Raycast(t.position + Vector3.up * 0.5f, Vector3.down, out var hit, 200f)) { t.position = hit.point; SceneEditor.Remember(t); }
            r = Gui.Row(6);
            string[] ax = { "X-", "X+", "Y-", "Y+", "Z-", "Z+" };
            Vector3[] dirs = { Vector3.left, Vector3.right, Vector3.down, Vector3.up, Vector3.back, Vector3.forward };
            for (int i = 0; i < 6; i++)
                if (GUI.Button(r[i], ax[i])) { t.position += dirs[i] * 0.25f; SceneEditor.Remember(t); }

            Gui.Header("Turn and size");
            r = Gui.Row(5);
            float[] turns = { -90, -15, 15, 90 };
            for (int i = 0; i < 4; i++)
                if (GUI.Button(r[i], (turns[i] > 0 ? "+" : "") + turns[i])) { t.Rotate(0, turns[i], 0, Space.World); SceneEditor.Remember(t); }
            if (GUI.Button(r[4], "Face me"))
            {
                var d = Freecam.Position - t.position; d.y = 0;
                if (d.sqrMagnitude > 0.001f) { t.rotation = Quaternion.LookRotation(d); SceneEditor.Remember(t); }
            }
            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Size {t.localScale.x:0.##}");
            if (GUI.Button(r[1], "-")) { t.localScale *= 0.9f; SceneEditor.Remember(t); }
            if (GUI.Button(r[2], "+")) { t.localScale *= 1.1f; SceneEditor.Remember(t); }
            if (GUI.Button(r[3], "1x")) { t.localScale = Vector3.one; SceneEditor.Remember(t); }

            var anim = go.GetComponentInChildren<Animator>();
            if (npc != null || anim != null)
            {
                Gui.Header("Character");
                bool still = IsStill(go);
                r = Gui.Row(2);
                if (GUI.Button(r[0], (still ? "[ON] " : "[off] ") + "Stand still"))
                {
                    if (still) Wake(go); else Spawner.MakeStill(go);
                }
                if (GUI.Button(r[1], "Bring to me"))
                {
                    var p = Freecam.Position + Freecam.Rotation * Vector3.forward * 3f;
                    if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 50f)) p = hit.point;
                    t.position = p;
                    SceneEditor.Remember(t);
                }
                if (anim != null)
                {
                    r = Gui.Row(2f, 1f, 1f, 1f, 1f);
                    GUI.Label(r[0], $"Animation {(anim.enabled ? anim.speed.ToString("0.##") + "x" : "frozen")}");
                    if (GUI.Button(r[1], "Freeze")) anim.enabled = !anim.enabled;
                    if (GUI.Button(r[2], "0.5x")) { anim.enabled = true; anim.speed = 0.5f; }
                    if (GUI.Button(r[3], "1x")) { anim.enabled = true; anim.speed = 1f; }
                    if (GUI.Button(r[4], "2x")) { anim.enabled = true; anim.speed = 2f; }
                }
                if (anim != null) Poses.Draw(go, anim);
                var talk = go.GetComponentInChildren<AudioSource>();
                if (talk != null && Gui.Toggle("Sounds", talk.enabled))
                    foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = !talk.enabled;
            }

            Gui.Header("Object");
            r = Gui.Row(2);
            if (GUI.Button(r[0], (go.activeSelf ? "[ON] " : "[off] ") + "Visible")) { go.SetActive(!go.activeSelf); SceneEditor.Remember(t); }
            if (GUI.Button(r[1], "3D preview")) NpcPreview.Show(go, go.name);
            r = Gui.Row(3);
            if (GUI.Button(r[0], "Copy (Ctrl+D)")) Duplicate();
            if (GUI.Button(r[1], "Delete (Del)")) { Delete(); return; }
            if (GUI.Button(r[2], "Deselect")) { _sel = null; return; }
            if (Gui.Button("Open in the scene editor")) { SceneEditor.Select(t); ToolkitBehaviour.Instance?.OpenTab(ToolkitBehaviour.SceneEditorTab); }
            Gui.Label($"Pos {t.position.x:0.0}, {t.position.y:0.0}, {t.position.z:0.0}   Yaw {t.eulerAngles.y:0}");
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

        // a frame around the selected thing, worked out from its visible bounds
        static void DrawSelectionBox()
        {
            if (_sel == null) return;
            var cam = Cam;
            if (cam == null) return;
            try
            {
                Bounds b = default;
                bool any = false;
                foreach (var r in _sel.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) b = new Bounds(_sel.position + Vector3.up, Vector3.one * 0.5f);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var sp = cam.WorldToScreenPoint(c);
                    if (sp.z < 0) return;
                    minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                    minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
                }
                float s = Mathf.Clamp(Plugin.MenuScale.Value, 0.5f, 3f);
                var rect = new Rect(minX / s, (Screen.height - maxY) / s, (maxX - minX) / s, (maxY - minY) / s);
                var old = GUI.color;
                GUI.color = _grab ? new Color(1f, 0.8f, 0.2f) : new Color(0.3f, 0.9f, 1f);
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
