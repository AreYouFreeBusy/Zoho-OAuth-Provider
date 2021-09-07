using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(Zoho_OAuth_Demo.Startup))]
namespace Zoho_OAuth_Demo
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
