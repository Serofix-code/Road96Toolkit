using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // Put a character in any animation: its own ones, or any clip the game has loaded.
    internal static class Poses
    {
        class Posed
        {
            public GameObject Go;
            public Animator Anim;
            public AnimationClip Clip;
            public float T;
            public bool Hold;
            public bool AnimWasEnabled;
            public float AnimSpeed;
        }

        static readonly List<Posed> _posed = new List<Posed>();
        static IntPtr _forGo;
        static List<AnimationClip> _own = new List<AnimationClip>();
        static List<AnimationClip> _all;
        static int _idx;
        static bool _useAll;

        static List<AnimationClip> Own(Animator a)
        {
            var res = new List<AnimationClip>();
            try
            {
                var rc = a.runtimeAnimatorController;
                if (rc == null) return res;
                var seen = new HashSet<string>();
                foreach (var c in rc.animationClips)
                    if (c != null && seen.Add(c.name)) res.Add(c);
            }
            catch { }
            res.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase));
            return res;
        }

        static List<AnimationClip> All()
        {
            if (_all != null) return _all;
            _all = new List<AnimationClip>();
            Game.Try("Find animations", () =>
            {
                var seen = new HashSet<string>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<AnimationClip>()))
                {
                    var c = o.TryCast<AnimationClip>();
                    if (c == null || c.length <= 0f || c.legacy) continue;
                    if (seen.Add(c.name)) _all.Add(c);
                }
            });
            _all.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase));
            return _all;
        }

        static Posed Find(GameObject go) => _posed.FirstOrDefault(p => p.Go != null && p.Go.Pointer == go.Pointer);

        internal static void Play(GameObject go, Animator anim, AnimationClip clip)
        {
            if (clip == null) return;
            var p = Find(go);
            // the character's own state with that name, the clean way
            try
            {
                int hash = Animator.StringToHash(clip.name);
                if (p == null && anim.enabled && anim.HasState(0, hash)) { anim.Play(hash, 0, 0f); ToolkitBehaviour.Toast("Playing " + clip.name); return; }
            }
            catch { }
            // otherwise sample the clip on the body every frame
            if (p == null)
            {
                p = new Posed { Go = go, Anim = anim, AnimWasEnabled = anim.enabled, AnimSpeed = anim.speed };
                _posed.Add(p);
            }
            p.Clip = clip;
            p.T = 0f;
            p.Hold = false;
            // humanoid clips need the animator to map bones, so keep it on but stopped
            if (clip.humanMotion) { anim.enabled = true; anim.speed = 0f; }
            else anim.enabled = false;
            ToolkitBehaviour.Toast("Posing with " + clip.name);
        }

        public static void Stop(GameObject go)
        {
            var p = Find(go);
            if (p == null) return;
            _posed.Remove(p);
            if (p.Anim != null) { p.Anim.enabled = p.AnimWasEnabled; p.Anim.speed = p.AnimSpeed <= 0f ? 1f : p.AnimSpeed; }
        }

        public static void LateUpdate()
        {
            for (int i = _posed.Count - 1; i >= 0; i--)
            {
                var p = _posed[i];
                if (p.Go == null || p.Anim == null || p.Clip == null) { _posed.RemoveAt(i); continue; }
                if (!p.Hold) p.T += Time.deltaTime;
                float len = Mathf.Max(0.01f, p.Clip.length);
                float t = p.T % len;
                try { p.Clip.SampleAnimation(p.Anim.gameObject, t); }
                catch { _posed.RemoveAt(i); }
            }
        }

        public static void Draw(GameObject go, Animator anim)
        {
            if (go.Pointer != _forGo) { _forGo = go.Pointer; _own = Own(anim); _idx = 0; }
            var list = _useAll ? All() : _own;
            if (list.Count == 0 && !_useAll) { _useAll = true; list = All(); }
            if (_idx >= list.Count) _idx = 0;
            var cur = list.Count > 0 ? list[_idx] : null;
            var posed = Find(go);

            var r = Gui.Row(1f, 4f, 1f);
            if (GUI.Button(r[0], "<") && list.Count > 0) { _idx = (_idx - 1 + list.Count) % list.Count; if (posed != null) Play(go, anim, list[_idx]); }
            GUI.Label(r[1], cur != null ? $"{cur.name}  ({_idx + 1}/{list.Count})" : "No animations found");
            if (GUI.Button(r[2], ">") && list.Count > 0) { _idx = (_idx + 1) % list.Count; if (posed != null) Play(go, anim, list[_idx]); }

            r = Gui.Row(4);
            if (GUI.Button(r[0], "Play")) Play(go, anim, cur);
            if (GUI.Button(r[1], posed != null && posed.Hold ? "[ Held ]" : "Hold pose") && posed != null) posed.Hold = !posed.Hold;
            if (GUI.Button(r[2], "Stop")) Stop(go);
            if (GUI.Button(r[3], _useAll ? "[ All ]" : "Own only")) { _useAll = !_useAll; _idx = 0; if (_useAll) _all = null; }
        }
    }
}
