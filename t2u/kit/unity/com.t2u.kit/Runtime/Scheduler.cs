using System;
using System.Collections.Generic;

namespace T2U.Kit
{
    /// <summary>
    /// Timers and tweens driven only by <see cref="Update"/> (game time). Twin of the template's src/core/lib/clock.js:
    ///  - items run in creation order; an item created during Update is first advanced on the NEXT Update;
    ///  - After(sec): fires once when elapsed &gt;= sec; Every(sec): fires at most once per Update, keeps the remainder;
    ///  - Tween(sec, fn(k, raw), ease, done): fn every Update with raw = min(1, t/sec), k = ease(raw); done() after raw reaches 1;
    ///  - Cancel(id) is safe at any time (also inside callbacks); Clear() drops everything.
    /// No per-frame allocations (lists are recycled).
    /// </summary>
    public sealed class Scheduler
    {
        const int KAfter = 0, KEvery = 1, KTween = 2;

        sealed class Item
        {
            public int Id, Kind;
            public double T, D;
            public Action Fn;
            public Action<double, double> TweenFn;
            public Func<double, double> Ease;
            public Action Done;
            public bool Dead;
        }

        static readonly List<Item> None = new List<Item>();
        List<Item> _items = new List<Item>();
        List<Item> _running = None;
        List<Item> _keepPool = new List<Item>();
        List<Item> _freshPool = new List<Item>();
        int _nextId = 1;

        /// <summary>Sum of all dt passed to Update (JS clock.time).</summary>
        public double Time;

        int Add(Item it)
        {
            it.Id = _nextId++;
            _items.Add(it);
            return it.Id;
        }

        public int After(double sec, Action fn) => Add(new Item { Kind = KAfter, D = sec, Fn = fn });
        public int Every(double sec, Action fn) => Add(new Item { Kind = KEvery, D = sec, Fn = fn });

        public int Tween(double sec, Action<double, double> fn, Func<double, double> ease = null, Action done = null) =>
            Add(new Item { Kind = KTween, D = sec, TweenFn = fn, Ease = ease ?? Easing.Linear, Done = done });

        public int Tween(double sec, Action<double> fn, Func<double, double> ease = null, Action done = null) =>
            Tween(sec, (k, raw) => fn(k), ease, done);

        public void Cancel(int id)
        {
            foreach (var it in _items) if (it.Id == id) it.Dead = true;
            foreach (var it in _running) if (it.Id == id) it.Dead = true;
        }

        public void Clear()
        {
            foreach (var it in _items) it.Dead = true;
            foreach (var it in _running) it.Dead = true;
            _items = new List<Item>();
        }

        /// <summary>Live timers/tweens (call outside Update).</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var it in _items) if (!it.Dead) n++;
                return n;
            }
        }

        public void Update(double dt)
        {
            Time += dt;
            var running = _items;
            _running = running;
            _items = _freshPool;
            _items.Clear();
            var keep = _keepPool;
            keep.Clear();
            for (int i = 0; i < running.Count; i++)
            {
                var it = running[i];
                if (it.Dead) continue;
                it.T += dt;
                if (it.Kind == KTween)
                {
                    double raw = Math.Min(1, it.T / it.D);
                    it.TweenFn(it.Ease(raw), raw);
                    if (raw >= 1 && !it.Dead)
                    {
                        it.Dead = true;
                        it.Done?.Invoke();
                    }
                }
                else if (it.T >= it.D)
                {
                    if (it.Kind == KEvery) it.T -= it.D;
                    else it.Dead = true;
                    it.Fn();
                }
                if (!it.Dead) keep.Add(it);
            }
            _running = None;
            var created = _items;
            foreach (var it in created) if (!it.Dead) keep.Add(it);
            _items = keep;
            running.Clear();
            created.Clear();
            _keepPool = running;
            _freshPool = created;
        }
    }
}
