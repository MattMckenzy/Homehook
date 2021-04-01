using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Homehook.Extensions
{
    public static class IEnumerableExtensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> list)
        {
            T[] workingList = list.ToArray();

            int n = workingList.Length;
            while (n > 1)
            {
                n--;
                int k = ThreadSafeRandom.ThisThreadsRandom.Next(n + 1);
                T value = workingList[k];
                workingList[k] = workingList[n];
                workingList[n] = value;
            }

            return workingList;
        }

        public static class ThreadSafeRandom
        {
            [ThreadStatic] private static Random Local;

            public static Random ThisThreadsRandom
            {
                get { return Local ?? (Local = new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId))); }
            }
        }
    }
}
