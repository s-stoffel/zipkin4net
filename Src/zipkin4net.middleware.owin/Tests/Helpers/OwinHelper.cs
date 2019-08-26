using Microsoft.Owin;
using Microsoft.Owin.Testing;
using Owin;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace zipkin4net.Middleware.Tests.Helpers
{
    static class OwinHelper
    {
        internal static async Task<string> Call(Action<IAppBuilder> startup, Func<HttpClient, Task<string>> clientCall, Action<HttpMessageHandler> rememberTestHttpMessageHandler = null)
        {
            using (var server = TestServer.Create(startup))
            {
                rememberTestHttpMessageHandler?.Invoke(server.Handler);
                using (var client = new HttpClient(server.Handler))
                {
                    return await clientCall(client);
                }
            }
        }
        internal static Action<IAppBuilder> DefaultStartup(string serviceName, Func<IOwinContext, string> getRpc = null, Func<PathString, bool> routeFilter = null, Func<IOwinContext, Task> runHandler = null)
        {
            return
                app =>
                {
                    app.UseZipkinTracer(serviceName, getRpc, routeFilter);

                    if (runHandler == null)
                    {
                        runHandler = async context =>
                        {
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync(DateTime.Now.ToString());
                        };
                    }
                    app.Run(runHandler);
                };
        }

        internal static Func<HttpClient, Task<string>> ClientCall(Uri urlToCall)
        {
            return async client =>
            {
                var response = await client.GetAsync(urlToCall);
                return await response.Content.ReadAsStringAsync();
            };
        }
    }
}
