using System.Runtime.Serialization;

namespace HomeHook.Common.Exceptions
{
    /// <summary>
    /// An exception to be used when there is an issue with an unprocessable entity during communication.
    /// </summary>
    [Serializable]
    public class UnprocessableEntityException : CommunicationException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public UnprocessableEntityException()
        {
        }

        /// <summary>
        /// Default constructor with message.
        /// </summary>
        /// <param name="message">The message to set in the exception.</param>
        public UnprocessableEntityException(string message) : base(message)
        {
        }

        /// <summary>
        /// Default constructor with message and inner exception.
        /// </summary>
        /// <param name="message">The message to set in the exception.</param>
        /// <param name="innerException">The inner exception to set in the exception.</param>
        public UnprocessableEntityException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Default constructor with which to serialize.
        /// </summary>
        /// <param name="info">The serialization info to use.</param>
        /// <param name="context">The streaming context to use.</param>
        protected UnprocessableEntityException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
