using System;
using System.Collections.Generic;

namespace T2U.Kit.Legacy
{
    /// <summary>
    /// Minimal ECMAScript Promise (fulfil-only) with spec-exact job ordering, so ported JS promise chains
    /// run their continuations in the same order and on the same frame as in the browser:
    ///  - reactions of a fulfilled promise are queued as jobs (one job each, in registration order);
    ///  - resolving with another promise costs two extra jobs (NewPromiseResolveThenableJob + its reaction);
    ///  - Promise.all resolves after the last element's reaction job.
    /// </summary>
    public sealed class JsPromise
    {
        readonly Microtasks _mt;
        List<Action> _reactions = new List<Action>();
        bool _alreadyResolved;

        public bool IsFulfilled { get; private set; }

        public JsPromise(Microtasks mt) { _mt = mt; }

        /// <summary>The promise's resolve function. Pass another promise to adopt its state.</summary>
        public void Resolve(JsPromise thenable = null)
        {
            if (_alreadyResolved) return;
            _alreadyResolved = true;
            if (thenable == null) { Fulfill(); return; }
            // NewPromiseResolveThenableJob: calls thenable.then(resolve) in a job
            _mt.Enqueue(() => thenable.React(Fulfill));
        }

        void Fulfill()
        {
            if (IsFulfilled) return;
            IsFulfilled = true;
            var rs = _reactions;
            _reactions = null;
            foreach (var r in rs) _mt.Enqueue(r);
        }

        /// <summary>PerformPromiseThen without a derived promise (used internally).</summary>
        void React(Action onFulfilled)
        {
            if (IsFulfilled) _mt.Enqueue(onFulfilled);
            else _reactions.Add(onFulfilled);
        }

        /// <summary>p.then(() => { ...; return promiseOrNull; })</summary>
        public JsPromise Then(Func<JsPromise> onFulfilled)
        {
            var derived = new JsPromise(_mt);
            React(() => derived.Resolve(onFulfilled()));
            return derived;
        }

        /// <summary>p.then(() => { ... })  (callback returns undefined)</summary>
        public JsPromise Then(Action onFulfilled)
        {
            var derived = new JsPromise(_mt);
            React(() => { onFulfilled(); derived.Resolve(); });
            return derived;
        }

        /// <summary>Promise.all([...]) with native promises.</summary>
        public static JsPromise All(Microtasks mt, params JsPromise[] items)
        {
            var result = new JsPromise(mt);
            int remaining = 1;
            foreach (var p in items)
            {
                remaining++;
                p.React(() => { if (--remaining == 0) result.Resolve(); });
            }
            if (--remaining == 0) result.Resolve();
            return result;
        }
    }
}
