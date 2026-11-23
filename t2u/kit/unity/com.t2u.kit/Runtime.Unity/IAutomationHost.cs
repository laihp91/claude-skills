using Newtonsoft.Json.Linq;
using UnityEngine;

namespace T2U.Kit.Automation
{
    /// <summary>
    /// What a game's bootstrap offers so the kit's <see cref="AutomationDriver"/> can replay web recordings in the real
    /// scene (with view and UI) — the C# side of the web harness (__t2u.begin / input / step / render).
    /// Implement it on the bootstrap MonoBehaviour (the one that owns the game core and the views).
    /// </summary>
    public interface IAutomationHost
    {
        /// <summary>
        /// True while the driver runs the game: the bootstrap must stop stepping the core itself (no FixedStepper ticks)
        /// AND stop drawing itself — the driver calls <see cref="Render"/> (web harness: draws only on render()). A view
        /// that stamps or spawns while drawing (decals, particles) would otherwise draw several frames into one shot.
        /// </summary>
        bool AutomationActive { set; }

        /// <summary>A new game with this seed, views and UI reset to it (web: __t2u.begin({seed})).</summary>
        void Begin(uint seed);

        /// <summary>One input event exactly as recorded (without "f").</summary>
        void Input(JObject ev);

        /// <summary>One fixed step (1/60) of the core only, no drawing (web: __t2u.tick()).</summary>
        void Step();

        /// <summary>Draws the current state once: UI and views (web: __t2u.render()).</summary>
        void Render();

        /// <summary>Lay the view out for a screen of this size in pixels (the web shot's viewport). Default: nothing.</summary>
        void Viewport(int width, int height) { }

        /// <summary>The camera that draws the game world. Default: Camera.main.</summary>
        Camera CaptureCamera => Camera.main;
    }
}
