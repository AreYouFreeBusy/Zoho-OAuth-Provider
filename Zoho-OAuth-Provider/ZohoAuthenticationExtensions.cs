//  Copyright 2021 Stefan Negritoiu (FreeBusy). See LICENSE file for more information.

using System;

namespace Owin.Security.Providers.Zoho
{
    public static class ZohoAuthenticationExtensions
    {
        public static IAppBuilder UseZohoAuthentication(this IAppBuilder app, ZohoAuthenticationOptions options)
        {
            if (app == null)
                throw new ArgumentNullException("app");
            if (options == null)
                throw new ArgumentNullException("options");

            app.Use(typeof(ZohoAuthenticationMiddleware), app, options);

            return app;
        }

        public static IAppBuilder UseZohoAuthentication(this IAppBuilder app, string clientId, string clientSecret)
        {
            return app.UseZohoAuthentication(new ZohoAuthenticationOptions
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            });
        }
    }
}