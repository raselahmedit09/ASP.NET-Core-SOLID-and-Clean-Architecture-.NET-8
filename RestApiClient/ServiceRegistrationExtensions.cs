using APIRestClient.HR.LeaveManagementModule;
using Microsoft.Extensions.DependencyInjection;

namespace RestApiClient
{
    public static class ServiceRegistrationExtensions
    {
        public static IServiceCollection RestApiClientGetewayServices(this IServiceCollection services)
        {
            services.AddHttpClient<LeaveManagementModuleAPI>((serviceProvider, client) =>
            {
                client.BaseAddress = new Uri("https://localhost:7090"); // Set the base URL for the HttpClient
            });

            services.AddScoped<LeaveManagementModuleAPI>(serviceProvider =>
            {
                var httpClient = serviceProvider.GetRequiredService<HttpClient>();
                var baseUrl = "https://localhost:7090"; // Provide the base URL
                return new LeaveManagementModuleAPI(baseUrl, httpClient);
            });

            return services;
        }
    }
}
