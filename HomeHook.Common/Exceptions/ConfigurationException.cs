using System.Runtime.Serialization;

namespace HomeHook.Common.Exceptions
{
    /// <summary>
    /// Exception to be used when an entity could not be found during communication.
    /// </summary>
    [Serializable]
    public class ConfigurationException : CommunicationException
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ConfigurationException()
        {
        }

        /// <summary>
        /// Constructor with exception message.
        /// </summary>
        public ConfigurationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with exception message and inner exception.
        /// </summary>
        public ConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with serialization info and streaming context.
        /// </summary>
        protected ConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}