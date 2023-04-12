using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Homehook.Extensions
{
    public static class IEnumerableExtensions
    {

        public static T[] MoveUp<T>(this T[] array, int indexToMove)
        {
            T old = array[indexToMove - 1];
            array[indexToMove - 1] = array[indexToMove];
            array[indexToMove] = old;

            return array;
        }

        public static T[] MoveDown<T>(this T[] array, int indexToMove)
        {
            T old = array[indexToMove + 1];
            array[indexToMove + 1] = array[indexToMove];
            array[indexToMove] = old;            

            return array;
        }
    }
}
