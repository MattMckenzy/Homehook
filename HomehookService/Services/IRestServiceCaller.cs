﻿using Homehook.Exceptions;
using Homehook.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Homehook.Services
{
    /// <summary>
    /// An interface that defines default methods to communicate to a restful API and returns a call result.
    /// </summary>
    public interface IRestServiceCaller
    {
        /// <summary>
        /// Creates and returns a base request message for the implemented type of service caller.
        /// </summary>
        /// <param name="credential">Optional credential to use.</param>
        /// <param name="accessTokenDelegate">Function used to retrieve access token with credential and code.</param>
        /// <returns>A base request message.</returns>
        Task<HttpRequestMessage> GetBaseRequestMessage(string credential = null, Func<string, string, Task<string>> accessTokenDelegate = null);

        /// <summary>
        /// Sends the given http request message.
        /// </summary>
        /// <returns>The response message.</returns>
        Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage);

        /// <summary>
        /// Sends an HTTP GET request with the given route segments.
        /// </summary>
        /// <typeparam name="T">The type of call result content, where it's type can be string or byte[].</typeparam>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <returns>The GET request call result.</returns>
        public async Task<CallResult<T>> GetRequestAsync<T>(string route, Dictionary<string, string> queryParameters = null, string credential = null, Dictionary<string,string> headerReplacements = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            return await SendAsync<T>(route, queryParameters, HttpMethod.Get, credential: credential, headerReplacements: headerReplacements, accessTokenDelegate: accessTokenDelegate).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an HTTP POST request with the given route segments, content and optional content type.
        /// </summary>
        /// <typeparam name="T">The type of call result content, where it's type can be string or byte[].</typeparam>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <param name="content">The content to POST.</param>
        /// <param name="contentType">The type of content to POST.</param>
        /// <returns>The POST request call result.</returns>
        public async Task<CallResult<T>> PostRequestAsync<T>(string route, Dictionary<string, string> queryParameters = null, string content = null, string contentType = "application/json", string credential = null, Dictionary<string, string> headerReplacements = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            return await SendAsync<T>(route, queryParameters, HttpMethod.Post, content, contentType, credential, headerReplacements, accessTokenDelegate).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an HTTP PUT request with the given route segments, content and optional content type.
        /// </summary>
        /// <typeparam name="T">The type of call result content, where it's type can be string or byte[].</typeparam>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <param name="content">The content to PUT.</param>
        /// <param name="contentType">The type of content to PUT.</param>
        /// <returns>The PUT request call result.</returns>
        public async Task<CallResult<T>> PutRequestAsync<T>(string route, Dictionary<string, string> queryParameters = null, string content = null, string contentType = "application/json", string credential = null, Dictionary<string, string> headerReplacements = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            return await SendAsync<T>(route, queryParameters, HttpMethod.Put, content, contentType, credential, headerReplacements, accessTokenDelegate).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an HTTP DELETE request with the given route segments.
        /// </summary>
        /// <typeparam name="T">The type of call result content, where it's type can be string or byte[].</typeparam>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <returns>The DELETE request call result.</returns>
        public async Task<CallResult<T>> DeleteRequestAsync<T>(string route, Dictionary<string, string> queryParameters = null, string credential = null, Dictionary<string, string> headerReplacements = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            return await SendAsync<T>(route, queryParameters, HttpMethod.Delete, credential: credential, headerReplacements: headerReplacements, accessTokenDelegate: accessTokenDelegate).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves an endpoint uri built with the given route segments.
        /// </summary>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <returns>The DELETE request call result.</returns>
        public async Task<Uri> GetEndpoint(string route, Dictionary<string, string> queryParameters = null, string credential = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            Contract.Requires(route != null);

            // Get base authenticated HttpRequestMessage from un-abstracted class
            using HttpRequestMessage httpRequestMessage = await GetBaseRequestMessage(credential, accessTokenDelegate).ConfigureAwait(false);

            // Build the new uri with the un-abstracted base uri and all newly given uri segments
            return new Uri(GetRequestUri(httpRequestMessage.RequestUri, route, queryParameters));
        }

        /// <summary>
        /// Builds a request uri with the given base uri and route segments.
        /// </summary>
        /// <param name="baseUri">The base of uri to build on.</param>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <returns>The request uri.</returns>
        private static string GetRequestUri(Uri baseUri, string route, Dictionary<string, string> queryParameters = null)
        {
            NameValueCollection queryParameterCollection = HttpUtility.ParseQueryString(string.Empty);
            foreach (KeyValuePair<string, string> keyValuePair in queryParameters ?? new Dictionary<string, string>())
                queryParameterCollection[keyValuePair.Key] = keyValuePair.Value;

            UriBuilder uriBuilder = new(new Uri(baseUri, route))
            {
                Query = queryParameterCollection.ToString()
            };

            return uriBuilder.ToString();
        }

        /// <summary>
        /// Builds and sends a request with the given parameters.
        /// </summary>
        /// <typeparam name="T">The type of call result content, where it's type can be string or byte[].</typeparam>
        /// <param name="route">The route string.</param>
        /// <param name="queryParameters">The route's query parameters.</param>
        /// <param name="httpMethod">The HTTP method of the request.</param>
        /// <param name="postContent">The optional content to post.</param>
        /// <param name="contentType">The optional content type to use. The default is application/json.</param>
        /// <returns>The call result.</returns>
        private async Task<CallResult<T>> SendAsync<T>(string route, Dictionary<string, string> queryParameters, HttpMethod httpMethod, string postContent = null, string contentType = "application/json", string credential = null, Dictionary<string, string> headerReplacements = null, Func<string, string, Task<string>> accessTokenDelegate = null)
        {
            CallResult<T> returningCallResult = null;

            // Get base authenticated HttpRequestMessage from un-abstracted class
            using HttpRequestMessage httpRequestMessage = await GetBaseRequestMessage(credential, accessTokenDelegate).ConfigureAwait(false);
            
            // Build the new uri with the un-abstracted base uri and all newly given uri segments
            httpRequestMessage.RequestUri = new Uri(GetRequestUri(httpRequestMessage.RequestUri, route, queryParameters));

            // Adds the content if given.
            httpRequestMessage.Method = httpMethod;
            if(!string.IsNullOrWhiteSpace(postContent))
            {
                httpRequestMessage.Content = new StringContent(postContent, Encoding.UTF8, contentType);
            }

            // Replace header values if necessary
            if (headerReplacements != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> httpRequestHeader in httpRequestMessage.Headers.ToArray())
                {
                    httpRequestMessage.Headers.Remove(httpRequestHeader.Key);
                    httpRequestMessage.Headers.Add(
                        httpRequestHeader.Key, 
                        headerReplacements.Aggregate(string.Join(' ', httpRequestHeader.Value), (accumulate, headerReplacement) => accumulate.Replace(headerReplacement.Key, headerReplacement.Value), result => result));
                }

            }

            HttpResponseMessage response = await SendRequest(httpRequestMessage).ConfigureAwait(false);
            using HttpContent httpContent = response.Content;

            if(!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && accessTokenDelegate != null)
                {
                    await GetBaseRequestMessage(credential, accessTokenDelegate);
                    return await SendAsync<T>(route, queryParameters, httpMethod, postContent, contentType, credential);
                }
                else
                    HandleError(httpRequestMessage, response);
            }
            else
            {
                if(typeof(T) == typeof(string))
                {
                    returningCallResult = (CallResult<T>)Convert.ChangeType(new CallResult<string>
                    {
                        Content = await httpContent.ReadAsStringAsync().ConfigureAwait(false),
                        StatusCode = response.StatusCode,
                        ReasonPhrase = response.ReasonPhrase,
                        Location = response.Headers.Location
                    }, typeof(CallResult<T>), CultureInfo.InvariantCulture);
                }
                else if(typeof(T) == typeof(IEnumerable<byte>))
                {
                    returningCallResult = (CallResult<T>)Convert.ChangeType(new CallResult<System.Collections.Generic.IEnumerable<byte>>
                    {
                        Content = await httpContent.ReadAsByteArrayAsync().ConfigureAwait(false),
                        StatusCode = response.StatusCode,
                        ReasonPhrase = response.ReasonPhrase,
                        Location = response.Headers.Location
                    }, typeof(CallResult<T>), CultureInfo.InvariantCulture);
                }
                else
                {
                    returningCallResult = new CallResult<T>
                    {
                        Content = default,
                        StatusCode = response.StatusCode,
                        ReasonPhrase = response.ReasonPhrase,
                        Location = response.Headers.Location
                    };
                }
            }            

            return returningCallResult;
        }

        /// <summary>
        /// Handles the given error status code by throwing the appropriate exception.
        /// </summary>
        /// <param name="httpRequestMessage">The HttpRequestMessage used to construct the exception.</param>
        /// <param name="response">The HttpResponseMessage used to construct the exception.</param>
        private static void HandleError(HttpRequestMessage httpRequestMessage, HttpResponseMessage response)
        {
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new UnauthorizedException($"There was a downstream unauthorized communication error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                HttpStatusCode.Forbidden => new ForbiddenException($"There was a downstream forbidden communication error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                HttpStatusCode.NotFound => new NotFoundException($"There was an error finding the downstream entity with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                HttpStatusCode.Conflict => new ConflictException($"There was a downstream entity conflict error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                HttpStatusCode.UnprocessableEntity => new UnprocessableEntityException($"There was a downstream unprocessable entity error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                HttpStatusCode.BadRequest => new BadRequestException($"There was a downstream bad request error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
                _ => new CommunicationException($"There was a downstream communication error with status code {response.StatusCode}: {response.ReasonPhrase}")
                {
                    Request = httpRequestMessage.RequestUri.ToString(),
                    StatusCode = response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase
                },
            };
        }
    }
}
