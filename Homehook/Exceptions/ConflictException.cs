using System.Runtime.Serialization;

namespace Homehook.Exceptions
{
    /// <summary>
    /// Exception to be used when an entity could not be inserted in the database because it conflicted with existing entity during communication.
    /// </summary>
    [Serializable]
    public class ConflictException : CommunicationException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ConflictException()
        {
        }

        /// <summary>
        /// Constructor with exception message.
        /// </summary>
        public ConflictException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with exception message and inner exception.
        /// </summary>
        public ConflictException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with serialization info and streaming context.
        /// </summary>
        protected ConflictException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}