// Build trigger tag: 20260629.v2
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using InventoryManagement.Client.Services;

namespace InventoryManagement.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<Microsoft.AspNetCore.Components.Web.HeadOutlet>("head::after");

            // Register MudBlazor Services
            builder.Services.AddMudServices();

            // Register Authentication Message Handler
            builder.Services.AddTransient<AuthenticationHeaderHandler>();

            // Configure HttpClient to talk to ASP.NET Core API
            // Fallback to launchSettings default port, or dynamic domain if self-hosted
            var apiBaseUrl = "https://webinventory-api.onrender.com/"; 
            
            builder.Services.AddHttpClient("InventoryAPI", client =>
            {
                client.BaseAddress = new Uri(apiBaseUrl);
            })
            .AddHttpMessageHandler<AuthenticationHeaderHandler>();

            // Register HttpClient instance for direct injection
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("InventoryAPI"));            // Register Authentication Services
            builder.Services.AddAuthorizationCore();
            builder.Services.AddScoped<ApiAuthenticationStateProvider>();
            builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());
            // Register Barcode SVG Generator Service
            builder.Services.AddScoped<InventoryManagement.Client.Services.BarcodeGeneratorService>();

            await builder.Build().RunAsync();
        }
    }
}
