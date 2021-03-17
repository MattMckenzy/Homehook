using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Retrieves the credentials that can be used to be authorized on the service.
        /// </summary>
        /// <returns>A dictionary of credentials token.</returns>
        Dictionary<string,string> GetCredentials();

    }
}