using System;
using System.Runtime.Serialization;

namespace Homehook.Exceptions
{
    /// <summary>
    /// Exception to be used when there was a bad request during communication.
    /// </summary>
    [Serializable]
    public class BadRequestException : CommunicationException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public BadRequestException()
        {
        }

        /// <summary>
        /// Constructor with exception message.
        /// </summary>
        public BadRequestException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with exception message and inner exception.
        /// </summary>
        public BadRequestException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with serialization info and streaming context.
        /// </summary>
        protected BadRequestException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}