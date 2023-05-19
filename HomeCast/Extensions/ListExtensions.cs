namespace HomeCast.Extensions
{
    public static class ListExtensions
    {
        public static List<T> MoveUp<T>(this List<T> array, int indexToMove)
        {
            if (!array.Any() || indexToMove == 0)
                return array;

            (array[indexToMove], array[indexToMove - 1]) = (array[indexToMove - 1], array[indexToMove]);
            return array;
        }

        public static List<T> MoveDown<T>(this List<T> array, int indexToMove)
        {
            if (!array.Any() || indexToMove == array.IndexOf(array.Last()))
                return array;

            (array[indexToMove], array[indexToMove + 1]) = (array[indexToMove + 1], array[indexToMove]);
            return array;
        }
    }
}
