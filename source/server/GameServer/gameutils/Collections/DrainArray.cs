using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DOL.GS
{
    // A lock-free, multi-producer single-consumer collection optimized for high-throughput concurrent additions.
    // Allows concurrent, lock-free add operations from multiple producer threads.
    // Designed for a single consumer to drain items; draining and adding must not occur simultaneously.
    // If the internal buffer overflows, excess items are temporarily stored in a concurrent queue and the buffer is automatically resized (never shrinks).
    // With deferAddsDuringDrain, an add that comes in while a drain runs (another thread, e.g. a timer) is kept for the
    // next drain instead of throwing; the packet queues use this so a packet sent at the wrong moment is not lost.
    public class DrainArray<T>
    {
        private T[] _buffer;
        private ConcurrentQueue<T> _overflowQueue = new();
        private readonly ConcurrentQueue<T> _lateQueue;   // adds made during a drain, sent first on the next drain
        public int _writeIndex;
        public int _adding;
        public bool _draining;
        public bool _overflowed;

        public bool Any => Volatile.Read(ref _writeIndex) > 0 || _lateQueue?.IsEmpty == false; // Not accurate.

        public DrainArray(int initialCapacity = 1024, bool deferAddsDuringDrain = false)
        {
            _buffer = new T[initialCapacity];
            _writeIndex = 0;

            if (deferAddsDuringDrain)
                _lateQueue = new();
        }

        /// <summary>Adds an item. Returns false when it came in during a drain and was kept for the next one.</summary>
        public bool Add(T item)
        {
            Interlocked.Increment(ref _adding);

            try
            {
                if (Volatile.Read(ref _draining))
                {
                    if (_lateQueue == null)
                        throw new InvalidOperationException($"Cannot {nameof(Add)} while {nameof(DrainTo)} is in progress.");

                    _lateQueue.Enqueue(item);
                    return false;
                }

                int index = Interlocked.Increment(ref _writeIndex) - 1;

                if (index < _buffer.Length)
                    _buffer[index] = item;
                else
                {
                    _overflowQueue.Enqueue(item);
                    _overflowed = true;
                }

                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _adding);
            }
        }

        public void DrainTo(Action<T> action)
        {
            DrainTo(static (item, act) => act(item), action);
        }

        public void DrainTo<TState>(Action<T, TState> action, TState state)
        {
            if (Interlocked.Exchange(ref _draining, true) != false)
                throw new InvalidOperationException($"Concurrent {nameof(DrainTo)} detected.");

            try
            {
                if (Volatile.Read(ref _adding) > 0)
                {
                    SpinWait spinner = new();

                    while (Volatile.Read(ref _adding) > 0)
                        spinner.SpinOnce(-1);
                }

                // Adds kept from the last drain are older than anything in the buffer, so they go first. Only those
                // there now: an add made during this drain waits for the next one.
                if (_lateQueue != null)
                {
                    for (int late = _lateQueue.Count; late > 0 && _lateQueue.TryDequeue(out T lateItem); late--)
                        action(lateItem, state);
                }

                int count = Math.Min(_buffer.Length, Interlocked.Exchange(ref _writeIndex, 0));

                if (count == 0)
                    return;

                for (int i = 0; i < count; i++)
                    action(_buffer[i], state);

                if (_overflowed)
                {
                    int overflowCount = 0;

                    while (_overflowQueue.TryDequeue(out T result))
                    {
                        overflowCount++;
                        action(result, state);
                    }

                    int newSize = Math.Max(_buffer.Length * 2, _buffer.Length + overflowCount + 1024);
                    _buffer = new T[newSize];
                    _overflowed = false;
                    return;
                }

                Array.Clear(_buffer, 0, count);
            }
            finally
            {
                Volatile.Write(ref _draining, false);
            }
        }
    }
}
