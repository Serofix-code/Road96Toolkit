using System;
using BlueEyes.ApplicationStates;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;
using SceneData = Tool7.Core.SceneManagement.SceneData;

namespace Road96Toolkit
{
    // Shows the name of the level for a few seconds after it finishes loading.
    internal static class LevelPopup
    {
        static SceneData _loading;
        static string _title, _sub;
        static float _until;
        static float _nextPoll;

        public static void Update()
        {
            if (!Plugin.LevelNamePopup.Value || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;

            SceneData now = null;
            foreach (var st in GameStates.States())
            {
                var load = st.TryCast<LoadSceneDataState>();
                if (load == null) continue;
                try { now = load._settings?._toLoad; } catch { }
                if (now != null) break;
            }

            if (now != null) { _loading = now; return; }
            if (_loading == null) return;

            // the load state just left the stack, so the level is in
            var sd = _loading;
            _loading = null;
            Show(sd);
        }

        public static void Show(SceneData sd)
        {
            string level = sd.name, title = null, id = null, npc = null;
            Game.Try("Level popup", () =>
            {
                NB.NarrativeElement el = null;
                var b = sd.TryCast<NB.BubbleSceneData>();
                if (b != null) el = b._correspondingBubble;
                if (el != null)
                {
                    id = el.ID;
                    npc = el.NPC;
                    title = ItemsTab.Loc(el.Title);
                }
            });
            // skip the menus and the small technical scenes
            var low = level.ToLowerInvariant();
            if (id == null && (low.Contains("menu") || low.Contains("splash") || low.Contains("core") || low.Contains("boot"))) return;

            _title = !string.IsNullOrEmpty(title) && title != id ? title : level;
            _sub = level + (id != null ? "   |   " + id : "") + (!string.IsNullOrEmpty(npc) ? "   |   " + npc : "");
            _until = Time.unscaledTime + Mathf.Max(1f, Plugin.LevelNamePopupSeconds.Value);
            Plugin.Log.LogInfo($"Level loaded: {_title} ({_sub})");
        }

        static GUIStyle _big, _small;

        public static void Draw()
        {
            if (_title == null || Time.unscaledTime > _until) return;
            float left = _until - Time.unscaledTime;
            float alpha = Mathf.Clamp01(left / 0.6f);
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            }
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            var r = new Rect(Screen.width / 2f - 320, Screen.height * 0.16f, 640, 86);
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x, r.y + 8, r.width, 44), _title, _big);
            GUI.Label(new Rect(r.x, r.y + 52, r.width, 24), _sub, _small);
            GUI.color = old;
        }
    }
}
