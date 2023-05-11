namespace HomeCast.Extensions
{
    public static class ListExtensions
    {
        public static List<T> MoveUp<T>(this List<T> array, int indexToMove)
        {
            (array[indexToMove], array[indexToMove - 1]) = (array[indexToMove - 1], array[indexToMove]);
            return array;
        }

        public static List<T> MoveDown<T>(this List<T> array, int indexToMove)
        {
            (array[indexToMove], array[indexToMove + 1]) = (array[indexToMove + 1], array[indexToMove]);
            return array;
        }
    }
}
