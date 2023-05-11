using System.Net;

namespace HomeHook.Common.Models
{
    /// <summary>
    /// A rest service call result.
    /// </summary>
    /// <typeparam name="T">The type of call result content, where it's type can be string or byte[]</typeparam>
    public class CallResult<T>
    {
        /// <summary>
        /// The body content of the result.
        /// </summary>
        public T? Content { get; set; }

        /// <summary>
        /// The returned HTTP status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The reason phrase, often sent with the given status code.
        /// </summary>
        public string? ReasonPhrase { get; set; }

        /// <summary>
        /// A possibly related location to the call's result.
        /// </summary>
        /// <remarks>
        /// This is typically populated after receiving a 201 created status or a 202 accepted status with the location of the affected resource.
        /// </remarks>
        public Uri? Location { get; set; }
    }
}