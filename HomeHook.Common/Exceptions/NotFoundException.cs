using System.Runtime.Serialization;

namespace HomeHook.Common.Exceptions
{
    /// <summary>
    /// Exception to be used when an entity could not be found during communication.
    /// </summary>
    [Serializable]
    public class NotFoundException : CommunicationException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public NotFoundException()
        {
        }

        /// <summary>
        /// Constructor with exception message.
        /// </summary>
        public NotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with exception message and inner exception.
        /// </summary>
        public NotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with serialization info and streaming context.
        /// </summary>
        protected NotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}