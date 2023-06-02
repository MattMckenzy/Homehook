namespace HomeCast.Extensions
{
    public static class LongExtensions
    {
        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.### XB", e.g. "4.2 KB" or "1.434 GB"
        public static string GetBytesReadable(this long bytesLength)
        {
            // Get absolute value
            long absoluteBytesLength = (bytesLength < 0 ? -bytesLength : bytesLength);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absoluteBytesLength >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (bytesLength >> 50);
            }
            else if (absoluteBytesLength >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (bytesLength >> 40);
            }
            else if (absoluteBytesLength >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (bytesLength >> 30);
            }
            else if (absoluteBytesLength >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (bytesLength >> 20);
            }
            else if (absoluteBytesLength >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (bytesLength >> 10);
            }
            else if (absoluteBytesLength >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = bytesLength;
            }
            else
            {
                return bytesLength.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable /= 1024;
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }
    }
}
