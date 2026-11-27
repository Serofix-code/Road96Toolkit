using System;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using UnityEngine;

namespace Road96Toolkit
{
    // Over the shoulder camera. The teen has no body in this game, so you can pick any character
    // as a stand in body that walks with you.
    internal static class ThirdPerson
    {
        public static bool Active { get; private set; }

        static Camera _cam;
        static CinemachineBrain _brain;
        static bool _brainWasEnabled;
        static float _dist = 2.8f, _height = 0.25f, _side = 0.45f;
        static GameObject _body;
        static string _bodyName = "";
        static Animator _bodyAnim;
        static Vector3 _lastPos;
        static float _moveSpeed;
        static int _page;
        static List<Spawner.Source> _choices = new List<Spawner.Source>();

        public static void Toggle() { if (Active) Disable(); else Enable(); }

        public static void Enable()
        {
            if (Freecam.Active || PhotoMode.Active) { ToolkitBehaviour.Toast("Leave the freecam first"); return; }
            if (Game.Player == null) { ToolkitBehaviour.Toast("No player here"); return; }
            _cam = Game.MainCamera;
            if (_cam == null) { ToolkitBehaviour.Toast("No camera found"); return; }
            Game.Try("Third person brain", () =>
            {
                _brain = _cam.GetComponent<CinemachineBrain>();
                if (_brain != null) { _brainWasEnabled = _brain.enabled; _brain.enabled = false; }
            });
            if (_body != null) _body.SetActive(true);
            _lastPos = Game.Player.transform.position;
            Active = true;
            ToolkitBehaviour.Toast(_body == null ? "Third person on. Pick a body in the Freecam tab" : "Third person on");
        }

        public static void Disable()
        {
            if (!Active) return;
            Active = false;
            Game.Try("Third person restore", () => { if (_brain != null) _brain.enabled = _brainWasEnabled; });
            if (_body != null) _body.SetActive(false);
            ToolkitBehaviour.Toast("Third person off");
        }

        static void SetBody(Spawner.Source s)
        {
            if (_body != null) UnityEngine.Object.Destroy(_body);
            _body = null; _bodyAnim = null;
            if (s?.Go == null) { _bodyName = ""; return; }
            Game.Try("Third person body", () =>
            {
                var p = Game.Player;
                var pos = p != null ? p.transform.position : Vector3.zero;
                var clone = UnityEngine.Object.Instantiate(s.Go.Cast<UnityEngine.Object>(), pos, Quaternion.identity).Cast<GameObject>();
                clone.name = "Road96Toolkit body " + s.Name;
                if (clone.transform.parent != null) clone.transform.SetParent(null, true);
                clone.hideFlags = HideFlags.None;
                // a puppet only: no scripts, no colliders, nothing that can talk or walk away
                foreach (var m in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (m != null) m.enabled = false;
                foreach (var c in clone.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = false;
                foreach (var a in clone.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) if (a != null) a.enabled = false;
                foreach (var a in clone.GetComponentsInChildren<AudioSource>(true)) if (a != null) a.enabled = false;
                clone.SetActive(Active);
                _body = clone;
                _bodyName = s.Name;
                _bodyAnim = clone.GetComponentInChildren<Animator>();
                if (_bodyAnim != null) _bodyAnim.applyRootMotion = false;
                ToolkitBehaviour.Toast("Your body is now " + s.Name);
            });
        }

        public static void Update()
        {
            if (Active && (Freecam.Active || Game.Player == null)) Disable();
            if (Active && InputHelper.Held(KeyCode.Equals)) _dist = Mathf.Min(10f, _dist + Time.unscaledDeltaTime * 3f);
            if (Active && InputHelper.Held(KeyCode.Minus)) _dist = Mathf.Max(0.8f, _dist - Time.unscaledDeltaTime * 3f);
        }

        public static void LateUpdate()
        {
            if (!Active || _cam == null) return;
            var p = Game.Player;
            if (p == null) return;
            Game.Try("Third person", () =>
            {
                var vcam = p._virtualCamera;
                var eyeT = vcam != null ? vcam.transform : p.transform;
                var eye = eyeT.position;
                var rot = eyeT.rotation;
                var want = eye + rot * new Vector3(_side, _height, -_dist);
                var dir = want - eye;
                if (Physics.SphereCast(eye, 0.2f, dir.normalized, out var hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.transform.root != p.transform.root)
                    want = eye + dir.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
                _cam.transform.SetPositionAndRotation(want, rot);

                if (_body == null) return;
                var feet = p.transform.position;
                var cc = p.GetComponent<CharacterController>();
                if (cc != null) feet = p.transform.TransformPoint(cc.center) - Vector3.up * (cc.height / 2f);
                var yaw = Quaternion.Euler(0, rot.eulerAngles.y, 0);
                _body.transform.SetPositionAndRotation(feet, yaw);

                float dt = Mathf.Max(0.0001f, Time.deltaTime);
                var flat = feet - _lastPos; flat.y = 0;
                _moveSpeed = Mathf.Lerp(_moveSpeed, flat.magnitude / dt, 0.2f);
                _lastPos = feet;
                if (_bodyAnim != null) Animate(_bodyAnim, _moveSpeed);
            });
        }

        // the characters' animators use different names, so try the usual ones
        static void Animate(Animator a, float speed)
        {
            try
            {
                foreach (var prm in a.parameters)
                {
                    if (prm == null || prm.type != AnimatorControllerParameterType.Float) continue;
                    var n = prm.name.ToLowerInvariant();
                    if (n == "speed" || n == "velocity" || n == "movespeed" || n == "forward" || n == "locomotion" || n == "walkspeed")
                        a.SetFloat(prm.nameHash, speed);
                }
            }
            catch { }
        }

        public static void Draw()
        {
            Gui.Header("Third person (experimental)");
            Gui.Label("Your teen has no body in this game, so pick a character to walk as.");
            var r = Gui.Row(2);
            if (GUI.Button(r[0], (Active ? "[ON] " : "[off] ") + "Third person (" + Plugin.ThirdPersonKey + ")")) Toggle();
            if (GUI.Button(r[1], "No body")) SetBody(null);
            r = Gui.Row(2f, 1f, 1f);
            GUI.Label(r[0], $"Distance {_dist:0.0} m  (- and = keys)");
            if (GUI.Button(r[1], "-")) _dist = Mathf.Max(0.8f, _dist - 0.4f);
            if (GUI.Button(r[2], "+")) _dist = Mathf.Min(10f, _dist + 0.4f);
            r = Gui.Row(2f, 1f, 1f);
            GUI.Label(r[0], $"Shoulder {_side:0.00}");
            if (GUI.Button(r[1], "Left")) _side = Mathf.Max(-1.5f, _side - 0.15f);
            if (GUI.Button(r[2], "Right")) _side = Mathf.Min(1.5f, _side + 0.15f);

            r = Gui.Row(2f, 1f);
            GUI.Label(r[0], "Body: " + (_body != null ? _bodyName : "none"));
            if (GUI.Button(r[1], "Find characters")) _choices = Spawner.Gather(0).Concat(Spawner.Gather(1)).ToList();
            if (_choices.Count > 0)
            {
                int pick = Gui.List(_choices, ref _page, s => s.Name + "   (" + s.Kind + ")", 5);
                if (pick >= 0) SetBody(_choices[pick]);
            }
        }
    }
}
