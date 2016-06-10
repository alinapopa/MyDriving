﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using Microsoft.WindowsAzure.MobileServices;
using System.Threading;
using MyDriving.Utils;
using System.Text;
using Newtonsoft.Json.Linq;
using MyDriving.Utils.Interfaces;

namespace MyDriving.AzureClient
{
    class AuthHandler : DelegatingHandler
    {
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private static bool isRefreshingToken = false;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Cloning the request, in case we need to send it again
            var clonedRequest = await CloneRequest(request);
            var response = await base.SendAsync(clonedRequest, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (isRefreshingToken)
                    return response;

                await semaphore.WaitAsync();

                isRefreshingToken = true;
                try
                {
                    if (!await RefreshToken())
                    {
                        //refresh not successful
                        Settings.Current.TokenExpired = true;
                    }
                }
                catch (System.Exception e)
                {
                    Logger.Instance.Report(e);
                }
                finally
                {
                    isRefreshingToken = false;
                    semaphore.Release();
                }

                //resend the request if the refresh was successful
                if (!Settings.Current.TokenExpired)
                {
                    clonedRequest = await CloneRequest(request);
                    clonedRequest.Headers.Remove("X-ZUMO-AUTH");
                    // Set the authentication header with the new token
                    clonedRequest.Headers.Add("X-ZUMO-AUTH", Settings.Current.AuthToken);
                    // Resend the request
                    response = await base.SendAsync(clonedRequest, cancellationToken);
                }
            }

            return response;
        }

        private async Task<HttpRequestMessage> CloneRequest(HttpRequestMessage request)
        {
            var result = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                result.Headers.Add(header.Key, header.Value);
            }

            if (request.Content != null && request.Content.Headers.ContentType != null)
            {
                var requestBody = await request.Content.ReadAsStringAsync();
                var mediaType = request.Content.Headers.ContentType.MediaType;
                result.Content = new StringContent(requestBody, Encoding.UTF8, mediaType);
                foreach (var header in request.Content.Headers)
                {
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Content.Headers.Add(header.Key, header.Value);
                    }
                }
            }

            return result;
        }

        private async Task<bool> RefreshToken()
        {
            if (Settings.Current.LoginAccount != LoginAccount.Microsoft)
                return false;

            var client = ServiceLocator.Instance.Resolve<IAzureClient>()?.Client as MobileServiceClient;
            if (client == null)
            {
                throw new InvalidOperationException(
                    "Make sure to set the ServiceLocator has an instance of IAzureClient");
            }

            try
            {
                JObject refreshJson = (JObject)await client.InvokeApiAsync("/.auth/refresh", HttpMethod.Get, null);

                if (refreshJson != null)
                {
                    string newToken = refreshJson["authenticationToken"].Value<string>();
                    client.CurrentUser.MobileServiceAuthenticationToken = newToken;
                    Settings.Current.AuthToken = newToken;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Logger.Instance.Report(e);
            }

            return false;
        }


    }
}