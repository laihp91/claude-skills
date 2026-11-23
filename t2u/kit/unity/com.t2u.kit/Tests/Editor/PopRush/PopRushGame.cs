using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using T2U.Kit.Replay;

namespace T2U.Kit.Samples.PopRush
{
    public sealed class PopRushConfig
    {
        public sealed class Size { public double W, H; }
        public Size World;
        public double ComboWindow, BombPenalty, AdBonusTime, AdSeconds, PopTime;
    }

    public sealed class PopRushLevel
    {
        public int Id, Goal;
        public double Time, SpawnEvery, BombChance;
        public double[] Speed, Radius;
    }

    /// <summary>
    /// 1:1 port of the template's src/core/game.js — the reference example of a Unity-ready port: same names, same
    /// order of operations, same RNG calls, kit twins for clock (Scheduler), easing and Math (JsMath).
    /// </summary>
    public sealed class PopRushGame : IReplayGame
    {
        public sealed class Bubble
        {
            public int Id;
            public string Kind;
            public double R, X0, X, Y, VY, Phase, Amp, Age, S = 1;
            public bool Popping;
        }

        readonly PopRushConfig _config;
        readonly List<PopRushLevel> _levels;
        readonly SeededRandom _rng;
        readonly Scheduler _clock = new Scheduler();
        readonly Action<string, JObject> _out;
        readonly double W, H;

        // read-only for view/UI
        public string State = "ready";
        public string Ui = "";
        public int LevelNo, Score, Goal, Misses, Combo, Stars;
        public double Time;
        public bool Revived;
        public readonly List<Bubble> Bubbles = new List<Bubble>();
        int _nextId = 1;
        double _spawnT;
        int _comboId;
        PopRushLevel _level;

        public PopRushGame(PopRushConfig config, List<PopRushLevel> levels, uint seed, Action<string, JObject> output, int? savedLevel = null)
        {
            _config = config;
            _levels = levels;
            _rng = new SeededRandom(seed);
            _out = output ?? ((n, d) => { });
            W = config.World.W;
            H = config.World.H;
            LevelNo = savedLevel ?? 1;
        }

        void LoadLevel(int n)
        {
            _clock.Clear();
            LevelNo = n;
            _level = _levels[(n - 1) % _levels.Count];
            State = "playing";
            Ui = "";
            Time = _level.Time;
            Score = 0;
            Goal = _level.Goal;
            Misses = 0;
            Combo = 0;
            Stars = 0;
            Revived = false;
            Bubbles.Clear();
            _spawnT = 0;
            _comboId = 0;
            _out("level", new JObject { ["n"] = n });
        }

        void Spawn()
        {
            var L = _level;
            var kind = _rng.Next() < L.BombChance ? "bomb" : "normal";
            double r = _rng.Range(L.Radius[0], L.Radius[1]);
            var b = new Bubble { Id = _nextId++, Kind = kind, R = r };
            b.X0 = _rng.Range(-W / 2 + r, W / 2 - r);
            b.X = 0;
            b.Y = -r;
            b.VY = _rng.Range(L.Speed[0], L.Speed[1]);
            b.Phase = _rng.Range(0, 2 * Math.PI);
            b.Amp = _rng.Range(0.2, 0.6);
            b.X = b.X0;
            Bubbles.Add(b);
            _out("spawn", new JObject { ["id"] = b.Id, ["kind"] = kind });
        }

        void RestartCombo()
        {
            if (_comboId != 0) _clock.Cancel(_comboId);
            _comboId = _clock.After(_config.ComboWindow, () =>
            {
                Combo = 0;
                _comboId = 0;
            });
        }

        void Pop(Bubble b)
        {
            b.Popping = true;
            if (b.Kind == "bomb")
            {
                Time = Math.Max(0, Time - _config.BombPenalty);
                Combo = 0;
                _out("sfx", new JObject { ["n"] = "bomb" });
                _out("vibrate", new JObject { ["ms"] = 80 });
            }
            else
            {
                Score++;
                Combo++;
                RestartCombo();
                _out("sfx", new JObject { ["n"] = "pop", ["rate"] = Js.R3(1 + Math.Min(Combo, 8) * 0.05) });
            }
            _out("pop", new JObject { ["id"] = b.Id, ["kind"] = b.Kind, ["x"] = Js.R3(b.X), ["y"] = Js.R3(b.Y), ["combo"] = Combo });
            _clock.Tween(_config.PopTime, k => { b.S = 1 + 0.4 * k; }, Easing.OutQuad, () =>
            {
                int i = Bubbles.IndexOf(b);
                if (i >= 0) Bubbles.RemoveAt(i);
            });
        }

        void Tap(double x, double y)
        {
            for (int i = Bubbles.Count - 1; i >= 0; i--)
            {
                var b = Bubbles[i];
                if (b.Popping) continue;
                if (JsMath.Hypot(x - b.X, y - b.Y) <= b.R)
                {
                    Pop(b);
                    return;
                }
            }
            _out("sfx", new JObject { ["n"] = "tap_miss" });
        }

        void Win()
        {
            State = "won";
            Stars = Misses == 0 ? 3 : Misses <= 3 ? 2 : 1;
            _out("sfx", new JObject { ["n"] = "win" });
            _out("win", new JObject { ["level"] = LevelNo, ["stars"] = Stars });
            _out("save", new JObject { ["level"] = LevelNo + 1 });
            _clock.After(0.8, () => { Ui = "win"; });
        }

        void Lose()
        {
            State = "lost";
            _out("sfx", new JObject { ["n"] = "lose" });
            _out("lose", new JObject { ["level"] = LevelNo });
            _clock.After(0.5, () => { Ui = "lose"; });
        }

        void Click(string id)
        {
            if (Ui == "win" && id == "next")
            {
                _out("sfx", new JObject { ["n"] = "click" });
                LoadLevel(LevelNo + 1);
            }
            else if (Ui == "lose" && id == "retry")
            {
                _out("sfx", new JObject { ["n"] = "click" });
                LoadLevel(LevelNo);
            }
            else if (Ui == "lose" && id == "moreTime" && !Revived)
            {
                _out("sfx", new JObject { ["n"] = "click" });
                Ui = "ad";
                _out("ad", new JObject { ["placement"] = "moreTime" });
            }
        }

        public void Input(JObject ev)
        {
            switch ((string)ev["t"])
            {
                case "start":
                    LoadLevel((int?)ev["level"] ?? LevelNo);
                    break;
                case "pd":
                    if (State == "playing") Tap((double)ev["x"], (double)ev["y"]);
                    break;
                case "click":
                    Click((string)ev["id"]);
                    break;
                case "adDone":
                    if (Ui != "ad") break;
                    if ((bool?)ev["ok"] == true)
                    {
                        Revived = true;
                        Time += _config.AdBonusTime;
                        State = "playing";
                        Ui = "";
                    }
                    else Ui = "lose";
                    break;
            }
        }

        public void Update(double dt)
        {
            _clock.Update(dt);
            if (State != "playing") return;
            Time = Math.Max(0, Time - dt);
            _spawnT -= dt;
            if (_spawnT <= 0)
            {
                Spawn();
                _spawnT += _level.SpawnEvery;
            }
            foreach (var b in Bubbles)
            {
                if (b.Popping) continue;
                b.Age += dt;
                b.Y += b.VY * dt;
                b.X = b.X0 + JsMath.Sin(b.Age * 2 + b.Phase) * b.Amp;
            }
            for (int i = Bubbles.Count - 1; i >= 0; i--)
            {
                var b = Bubbles[i];
                if (!b.Popping && b.Y - b.R > H)
                {
                    Bubbles.RemoveAt(i);
                    if (b.Kind == "normal")
                    {
                        Misses++;
                        _out("miss", new JObject { ["id"] = b.Id });
                    }
                }
            }
            if (Score >= Goal) Win();
            else if (Time <= 0) Lose();
        }

        public JObject Snapshot()
        {
            var bubbles = new JArray();
            foreach (var b in Bubbles)
                bubbles.Add(new JObject
                {
                    ["id"] = b.Id, ["k"] = b.Kind, ["x"] = Js.R3(b.X), ["y"] = Js.R3(b.Y), ["r"] = Js.R3(b.R), ["s"] = Js.R3(b.S), ["pop"] = b.Popping ? 1 : 0,
                });
            return new JObject
            {
                ["state"] = State, ["ui"] = Ui, ["level"] = LevelNo, ["time"] = Js.R3(Time), ["score"] = Score, ["goal"] = Goal,
                ["misses"] = Misses, ["combo"] = Combo, ["rng"] = _rng.Calls, ["timers"] = _clock.Count, ["bubbles"] = bubbles,
            };
        }
    }
}
