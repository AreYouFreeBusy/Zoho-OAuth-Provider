﻿//  Copyright 2021 Stefan Negritoiu (FreeBusy). See LICENSE file for more information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using Microsoft.Owin.Infrastructure;
using Microsoft.Owin.Logging;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Owin.Security.Providers.Zoho
{
    public class ZohoAuthenticationHandler : AuthenticationHandler<ZohoAuthenticationOptions>
    {
        // see https://www.zoho.com/accounts/protocol/oauth/web-apps/authorization.html for docs 
        private const string AuthorizeEndpoint = "https://accounts.zoho.com/oauth/v2/auth";
        private const string TokenEndpoint = "https://accounts.zoho.com/oauth/v2/token";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public ZohoAuthenticationHandler(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }


        protected override async Task<AuthenticationTicket> AuthenticateCoreAsync()
        {
            AuthenticationProperties properties = null;

            try
            {
                string state = null;
                string code = null;

                IReadableStringCollection query = Request.Query;
                IList<string> values;
                
                values = query.GetValues("state");
                if (values != null && values.Count == 1) 
                {
                    state = values[0];
                }
                properties = Options.StateDataFormat.Unprotect(state);
                if (properties == null) 
                {
                    return null;
                }

                values = query.GetValues("error");
                if (values != null && values.Count == 1) 
                {
                    return new AuthenticationTicket(null, properties);
                }
                
                values = query.GetValues("code");
                if (values != null && values.Count == 1) 
                {
                    code = values[0];
                }

                // OAuth2 10.12 CSRF
                if (!ValidateCorrelationId(properties, _logger))
                {
                    return new AuthenticationTicket(null, properties);
                }

                string requestPrefix = Request.Scheme + "://" + Request.Host;
                string redirectUri = requestPrefix + Request.PathBase + Options.CallbackPath;

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", ToBase64($"{Options.ClientId}:{Options.ClientSecret}"));

                // Build up the body for the token request
                var body = new List<KeyValuePair<string, string>>();
                body.Add(new KeyValuePair<string, string>("grant_type", "authorization_code"));
                body.Add(new KeyValuePair<string, string>("code", code));
                body.Add(new KeyValuePair<string, string>("redirect_uri", redirectUri));

                // Request the token
                var tokenResponse = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(body));
                tokenResponse.EnsureSuccessStatusCode();
                string content = await tokenResponse.Content.ReadAsStringAsync();

                var response = JsonConvert.DeserializeObject<JObject>(content);
                string accessToken = response.Value<string>("access_token");
                string expires = response.Value<string>("expires_in");
                string refreshToken = response.Value<string>("refresh_token");

                var context = new ZohoAuthenticatedContext(Context, accessToken, expires, refreshToken);
                context.Identity = new ClaimsIdentity(
                    Options.AuthenticationType,
                    ClaimsIdentity.DefaultNameClaimType,
                    ClaimsIdentity.DefaultRoleClaimType);

                context.Properties = properties;

                await Options.Provider.Authenticated(context);

                return new AuthenticationTicket(context.Identity, context.Properties);
            }
            catch (Exception ex)
            {
                _logger.WriteError("Authentication failed", ex);
                return new AuthenticationTicket(null, properties);
            }
        }


        protected override Task ApplyResponseChallengeAsync()
        {
            if (Response.StatusCode != 401)
            {
                return Task.FromResult<object>(null);
            }

            AuthenticationResponseChallenge challenge = 
                Helper.LookupChallenge(Options.AuthenticationType, Options.AuthenticationMode);

            if (challenge != null)
            {
                var beforeRedirectContext = new ZohoBeforeRedirectContext(Context, Options);
                Options.Provider.BeforeRedirect(beforeRedirectContext);

                string baseUri =
                    Request.Scheme +
                    Uri.SchemeDelimiter +
                    Request.Host +
                    Request.PathBase;

                string currentUri =
                    baseUri +
                    Request.Path +
                    Request.QueryString;

                string redirectUri =
                    baseUri +
                    Options.CallbackPath;

                AuthenticationProperties properties = challenge.Properties;
                if (string.IsNullOrEmpty(properties.RedirectUri))
                {
                    properties.RedirectUri = currentUri;
                }

                // OAuth2 10.12 CSRF
                GenerateCorrelationId(properties);

                var queryStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                queryStrings.Add("response_type", "code");
                queryStrings.Add("client_id", Options.ClientId);
                queryStrings.Add("redirect_uri", redirectUri);

                string scope = string.Join(" ", Options.Scope);
                if (string.IsNullOrEmpty(scope)) {
                    // default scope if app hasn't requested one
                    scope = "AaaServer.profile.Read";
                }
                queryStrings.Add("scope", scope);

                if (!string.IsNullOrEmpty(Options.AccessType)) {
                    queryStrings.Add("access_type", Options.AccessType);
                }

                string state = Options.StateDataFormat.Protect(properties);
                queryStrings.Add("state", state);

                string authorizationEndpoint = WebUtilities.AddQueryString(AuthorizeEndpoint, queryStrings);

                var redirectContext = new ZohoApplyRedirectContext(Context, Options, properties, authorizationEndpoint);
                Options.Provider.ApplyRedirect(redirectContext);
            }

            return Task.FromResult<object>(null);
        }


        public override async Task<bool> InvokeAsync()
        {
            return await InvokeReplyPathAsync();
        }


        private async Task<bool> InvokeReplyPathAsync()
        {
            if (Options.CallbackPath.HasValue && Options.CallbackPath == Request.Path)
            {
                AuthenticationTicket ticket = await AuthenticateAsync();
                if (ticket == null)
                {
                    _logger.WriteWarning("Invalid return state, unable to redirect.");
                    Response.StatusCode = 500;
                    return true;
                }

                var context = new ZohoReturnEndpointContext(Context, ticket);
                context.SignInAsAuthenticationType = Options.SignInAsAuthenticationType;
                context.RedirectUri = ticket.Properties.RedirectUri;

                await Options.Provider.ReturnEndpoint(context);

                if (context.SignInAsAuthenticationType != null && context.Identity != null)
                {
                    ClaimsIdentity grantIdentity = context.Identity;
                    if (!string.Equals(
                        grantIdentity.AuthenticationType, context.SignInAsAuthenticationType, StringComparison.Ordinal))
                    {
                        grantIdentity = new ClaimsIdentity(
                            grantIdentity.Claims, 
                            context.SignInAsAuthenticationType, 
                            grantIdentity.NameClaimType, 
                            grantIdentity.RoleClaimType);
                    }
                    Context.Authentication.SignIn(context.Properties, grantIdentity);
                }

                if (!context.IsRequestCompleted && context.RedirectUri != null)
                {
                    string redirectUri = context.RedirectUri;
                    if (context.Identity == null)
                    {
                        // add a redirect hint that sign-in failed in some way
                        redirectUri = WebUtilities.AddQueryString(redirectUri, "error", "access_denied");
                    }
                    Response.Redirect(redirectUri);
                    context.RequestCompleted();
                }

                return context.IsRequestCompleted;
            }
            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        private static string ToBase64(string text) 
        {
            var btByteArray = Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(btByteArray, 0, btByteArray.Length);
        }
    }
}