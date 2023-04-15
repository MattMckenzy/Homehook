namespace WonkCast.Extensions
{
    public static class IEnumerableExtensions
    {

        public static T[] MoveUp<T>(this T[] array, int indexToMove)
        {
            (array[indexToMove], array[indexToMove - 1]) = (array[indexToMove - 1], array[indexToMove]);
            return array;
        }

        public static T[] MoveDown<T>(this T[] array, int indexToMove)
        {
            (array[indexToMove], array[indexToMove + 1]) = (array[indexToMove + 1], array[indexToMove]);
            return array;
        }
    }
}
