using System;
using System.Collections.Generic;
using BlueEyes.Entities;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Small wrappers around the game singletons so the tabs stay readable.
    internal static class Game
    {
        public static NB.NarrativeController Controller
        {
            get
            {
                try
                {
                    var c = NB.NarrativeController.Instance;
                    return c == null ? null : c;
                }
                catch { return null; }
            }
        }

        public static NB.NarrativeContext Context
        {
            get
            {
                var c = Controller;
                if (c == null) return null;
                try { return c.CurrentContext; } catch { return null; }
            }
        }

        public static NB.NarrativeSettings Settings
        {
            get
            {
                var c = Controller;
                if (c == null) return null;
                try { return c._settings; } catch { return null; }
            }
        }

        public static NB.NarrativeBrain Brain
        {
            get
            {
                var c = Controller;
                if (c == null) return null;
                try { return c.CurrentBrain; } catch { return null; }
            }
        }

        public static PlayerBlueEyes Player
        {
            get
            {
                try
                {
                    var p = PlayerBlueEyes.Instance;
                    return p == null ? null : p;
                }
                catch { return null; }
            }
        }

        public static EndRunRitual Ritual
        {
            get
            {
                try
                {
                    var r = EndRunRitual.Instance;
                    return r == null ? null : r;
                }
                catch { return null; }
            }
        }

        public static Camera MainCamera
        {
            get
            {
                try
                {
                    var c = Camera.main;
                    return c == null ? null : c;
                }
                catch { return null; }
            }
        }

        static readonly HashSet<string> _warned = new HashSet<string>();

        // Runs an action and logs a failure only once per label so the log does not flood.
        public static bool Try(string label, Action a)
        {
            try
            {
                a();
                return true;
            }
            catch (Exception e)
            {
                if (_warned.Add(label + e.GetType().Name))
                    Plugin.Log.LogWarning($"{label} failed: {e.GetType().Name}: {e.Message}");
                ToolkitBehaviour.Toast($"{label} failed, see log");
                return false;
            }
        }
    }
}
