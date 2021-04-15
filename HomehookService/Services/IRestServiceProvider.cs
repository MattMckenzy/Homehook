using System;

namespace Homehook.Services
{
    /// <summary>
    /// An interface that defines a means to access a rest service's configuration.
    /// </summary>
    public interface IRestServiceProvider
    {
        /// <summary>
        /// Retrieves the scope needed to access the service.
        /// </summary>
        /// <returns>The scope.</returns>
        string GetScope();

        /// <summary>
        /// Retrieves the header needed to be authorized on the service.
        /// </summary>
        /// <returns>The header.</returns>
        string GetHeader();

        /// <summary>
        /// Retrieves the token needed to be authorized on the service.
        /// </summary>
        /// <returns>The token.</returns>
        string GetToken();

        /// <summary>
        /// Retrieves the service uri configuration value.
        /// </summary>
        /// <returns>The service uri.</returns>
        Uri GetServiceUri();
    }
}