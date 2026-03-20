using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Dispatches work from background threads to Unity's main thread.
    /// Uses EditorApplication.update to pump a work queue.
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private struct WorkItem
        {
            public Action Work;
            public ManualResetEventSlim Done;
        }

        private static readonly ConcurrentQueue<WorkItem> s_queue = new();

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        /// <summary>
        /// Execute a function on the main thread, block until complete,
        /// and return the result.
        /// Call this from a background thread only.
        /// </summary>
        public static T Invoke<T>(Func<T> func)
        {
            T result = default;
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            s_queue.Enqueue(new WorkItem
            {
                Work = () =>
                {
                    try { result = func(); }
                    catch (Exception ex) { caught = ex; }
                },
                Done = done
            });

            done.Wait();
            done.Dispose();

            if (caught != null)
                throw caught;
            return result;
        }

        /// <summary>
        /// Execute an action on the main thread and block until complete.
        /// Call this from a background thread only.
        /// </summary>
        public static void Invoke(Action action)
        {
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            s_queue.Enqueue(new WorkItem
            {
                Work = () =>
                {
                    try { action(); }
                    catch (Exception ex) { caught = ex; }
                },
                Done = done
            });

            done.Wait();
            done.Dispose();

            if (caught != null)
                throw caught;
        }

        private static void ProcessQueue()
        {
            while (s_queue.TryDequeue(out var item))
            {
                item.Work();
                item.Done.Set();
            }
        }
    }
}
