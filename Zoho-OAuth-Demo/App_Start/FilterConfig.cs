﻿using System.Web;
using System.Web.Mvc;

namespace Zoho_OAuth_Demo
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters) {
            filters.Add(new HandleErrorAttribute());
        }
    }
}
