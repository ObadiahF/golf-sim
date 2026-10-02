using System;
using System.Collections;
using System.Collections.Generic;

namespace GolfSim.Net
{
    /// <summary>
    /// Runs a coroutine (nested IEnumerators included) and reports an exception instead of letting it kill the
    /// coroutine silently, e.g. UnityWebRequest.SendWebRequest throwing "Insecure connection not allowed" or a bad
    /// URL. Run the result with StartCoroutine.
    /// </summary>
    public static class SafeCoroutine
    {
        public static IEnumerator Run(IEnumerator routine, Action<Exception> failed)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    // The failing routine has already run its finally blocks; the ones waiting on it run theirs here.
                    foreach (var waiting in stack) (waiting as IDisposable)?.Dispose();
                    failed(e);
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
        }
    }
}
