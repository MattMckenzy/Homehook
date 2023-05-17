using Newtonsoft.Json;

namespace HomeCast.Extensions
{
    public static class StringExtensions
    {
        public static bool TryParseJson<T>(this string @this, out T? result)
        {
            bool success = true;

            JsonSerializerSettings settings = new()
            {
                Error = (sender, args) => { success = false; args.ErrorContext.Handled = true; },
                MissingMemberHandling = MissingMemberHandling.Error
            };

            result = JsonConvert.DeserializeObject<T>(@this, settings);
            return success;
        }
    }
}
