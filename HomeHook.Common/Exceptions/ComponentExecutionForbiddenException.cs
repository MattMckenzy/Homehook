using System.Runtime.Serialization;

namespace HomeHook.Common.Exceptions
{
    /// <summary>
    /// Exception to be used when the request component is not able to be executed.
    /// </summary>
    [Serializable]
    public class ComponentExecutionForbiddenException : Exception
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ComponentExecutionForbiddenException()
        {
        }

        /// <summary>
        /// Constructor with exception message.
        /// </summary>
        public ComponentExecutionForbiddenException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with exception message and inner exception.
        /// </summary>
        public ComponentExecutionForbiddenException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with serialization info and streaming context.
        /// </summary>
        protected ComponentExecutionForbiddenException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}