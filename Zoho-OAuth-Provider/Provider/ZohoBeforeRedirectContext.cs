//  Copyright 2017 Stefan Negritoiu. See LICENSE file for more information.

using System;
using Microsoft.Owin;
using Microsoft.Owin.Security.Provider;

namespace Owin.Security.Providers.Zoho
{
    /// <summary>
    /// Context passed when a Challenge causes a redirect to authorize endpoint in the Zoho OAuth 2.0 middleware
    /// </summary>
    public class ZohoBeforeRedirectContext : BaseContext<ZohoAuthenticationOptions>
    {
        /// <summary>
        /// Creates a new context object.
        /// </summary>
        /// <param name="context">The OWIN request context</param>
        /// <param name="options">The Zoho middleware options</param>
        public ZohoBeforeRedirectContext(IOwinContext context, ZohoAuthenticationOptions options)
            : base(context, options) 
        {
        }
    }
}
