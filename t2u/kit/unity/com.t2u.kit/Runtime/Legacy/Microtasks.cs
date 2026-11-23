using System;
using System.Collections.Generic;

namespace T2U.Kit.Legacy
{
    /// <summary>
    /// The JavaScript job queue. Promise reactions are queued here and run, FIFO, when <see cref="Drain"/> is called —
    /// which the game does after every Update and after every input (the browser does the same after each task).
    /// </summary>
    public sealed class Microtasks
    {
        readonly Queue<Action> _queue = new Queue<Action>();

        public int Count => _queue.Count;

        public void Enqueue(Action job) => _queue.Enqueue(job);

        /// <summary>Run jobs until the queue is empty (including jobs queued while draining).</summary>
        public void Drain()
        {
            while (_queue.Count > 0) _queue.Dequeue()();
        }
    }
}
