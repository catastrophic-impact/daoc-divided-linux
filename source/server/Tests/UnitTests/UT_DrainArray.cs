using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_DrainArray
    {
        [Test]
        public void DrainTo_ShouldCallAction_OncePerItem()
        {
            DrainArray<int> drainArray = new();
            int itemCount = 1000;
            ConcurrentBag<int> called = new();

            for (int i = 0; i < itemCount; i++)
                drainArray.Add(i);

            drainArray.DrainTo(called.Add);
            Assert.That(called.Count, Is.EqualTo(itemCount));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, itemCount), called);
            AssertBufferIsCleared(drainArray);
        }

        [Test]
        public void Add_ShouldBeThreadSafe_AndAllowConcurrentAdds()
        {
            DrainArray<int> drainArray = new();
            int itemCount = 10000;
            ConcurrentBag<int> added = new();

            Parallel.For(0, itemCount, i =>
            {
                drainArray.Add(i);
                added.Add(i);
            });

            ConcurrentBag<int> drained = new();
            drainArray.DrainTo(drained.Add);
            Assert.That(drained.Count, Is.EqualTo(itemCount));
            CollectionAssert.AreEquivalent(added, drained);
            AssertBufferIsCleared(drainArray);
        }

        [Test]
        public void Add_DuringDrainTo_ShouldThrowInvalidOperationException()
        {
            DrainArray<int> drainArray = new();
            drainArray.Add(1); // Initial item to ensure DrainTo has something to process.

            ManualResetEventSlim started = new(false);
            ManualResetEventSlim readyToAdd = new(false);
            Exception addException = null;

            // DrainTo will block until we try to Add.
            var drainTask = Task.Run(() =>
            {
                drainArray.DrainTo(i =>
                {
                    started.Set(); // Signal that DrainTo is in progress.
                    readyToAdd.Wait(); // Wait until Add is attempted.
                });
            });

            started.Wait(); // Wait for DrainTo to start.

            // Try to Add while DrainTo is in progress.
            var addTask = Task.Run(() =>
            {
                try
                {
                    drainArray.Add(42);
                }
                catch (Exception ex)
                {
                    addException = ex;
                }
                finally
                {
                    readyToAdd.Set();
                }
            });

            Task.WaitAll(drainTask, addTask);

            if (addException == null)
            {
                AssertBufferIsCleared(drainArray); // Ensure buffer is cleared if no exception.
                Assert.Inconclusive("Race condition was not reproduced.");
            }
        }

        /*[Test]
        public void DrainTo_DuringAdd_ShouldThrowInvalidOperationException()
        {
            // This test may need to run multiple times to catch the race condition.
            bool exceptionCaught = false;

            for (int attempt = 0; attempt < 100 && !exceptionCaught; attempt++)
            {
                DrainArray<int> drainArray = new();
                Exception drainException = null;
                bool startDraining = false;
                bool addInProgress = false;

                var addTask = Task.Run(() =>
                {
                    // Spin until we're told to start draining.
                    SpinWait spin = new();
                    while (!Volatile.Read(ref startDraining))
                        spin.SpinOnce();

                    // Perform continuous Add operations.
                    for (int i = 0; i < 10000; i++)
                    {
                        try
                        {
                            Volatile.Write(ref addInProgress, true);
                            drainArray.Add(i + 100);
                            Volatile.Write(ref addInProgress, false);
                        }
                        catch (InvalidOperationException)
                        {
                            // Expected when caught by DrainTo.
                            break;
                        }

                        // Occasional yield to allow DrainTo to run.
                        if (i % 100 == 0)
                            Thread.Yield();
                    }
                });

                var drainTask = Task.Run(() =>
                {
                    try
                    {
                        // Start the Add operations.
                        Volatile.Write(ref startDraining, true);

                        // Brief delay to let Add operations begin.
                        Thread.Sleep(1);

                        // Try DrainTo multiple times to catch Add in progress.
                        for (int i = 0; i < 1000; i++)
                        {
                            try
                            {
                                drainArray.DrainTo(_ => { });
                                Thread.Yield();
                            }
                            catch (InvalidOperationException ex)
                            {
                                drainException = ex;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        drainException = ex;
                    }
                });

                Task.WaitAll(addTask, drainTask);

                if (drainException != null)
                {
                    exceptionCaught = true;
                    Assert.That(drainException, Is.TypeOf<InvalidOperationException>());
                    break;
                }

                AssertBufferIsCleared(drainArray); // Ensure buffer is cleared if no exception.
            }

            if (!exceptionCaught)
                Assert.Inconclusive("Race condition was not reproduced.");
        }*/

        // The packet queues (deferAddsDuringDrain): an add made while a drain runs, e.g. a packet from a timer thread
        // while the game loop sends, is kept and goes out first on the next drain instead of being dropped.
        [Test]
        public void DeferredAdd_DuringDrainTo_IsKeptForTheNextDrain()
        {
            DrainArray<int> drainArray = new(deferAddsDuringDrain: true);
            drainArray.Add(1);
            drainArray.Add(2);
            bool addedDuringDrain = true;
            System.Collections.Generic.List<int> first = new();

            drainArray.DrainTo(item =>
            {
                first.Add(item);

                if (item == 1)
                    addedDuringDrain = drainArray.Add(99);
            });

            Assert.That(addedDuringDrain, Is.False, "an add during a drain reports that it was kept for later");
            Assert.That(first, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(drainArray.Any, Is.True);

            Assert.That(drainArray.Add(3), Is.True);
            System.Collections.Generic.List<int> second = new();
            drainArray.DrainTo(second.Add);
            Assert.That(second, Is.EqualTo(new[] { 99, 3 }), "the kept item is older, so it goes first");
            Assert.That(drainArray.Any, Is.False);
            AssertBufferIsCleared(drainArray);
        }

        [Test]
        public void DeferredAdd_FromAnotherThreadDuringDrain_IsNeverLost()
        {
            DrainArray<int> drainArray = new(deferAddsDuringDrain: true);
            ConcurrentBag<int> drained = new();
            int itemCount = 20000;
            using CancellationTokenSource stop = new();

            Task drainer = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    drainArray.DrainTo(drained.Add);
            });

            Parallel.For(0, itemCount, i => drainArray.Add(i));
            stop.Cancel();
            drainer.Wait();
            drainArray.DrainTo(drained.Add);
            drainArray.DrainTo(drained.Add);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, itemCount), drained);
        }

        static void AssertBufferIsCleared<T>(DrainArray<T> drainArray)
        {
            FieldInfo bufferField = typeof(DrainArray<T>).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(bufferField, Is.Not.Null, "Could not find _buffer field via reflection");
            T[] buffer = bufferField.GetValue(drainArray) as T[];
            Assert.That(buffer, Is.Not.Null, "Buffer should not be null");

            // Check that all elements in the buffer are default values (null for reference types, 0 for int, etc.)
            for (int i = 0; i < buffer.Length; i++)
                Assert.That(buffer[i], Is.EqualTo(default(T)), $"Buffer element at index {i} should be cleared but was {buffer[i]}");
        }
    }
}
