using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
// Custom document filter to set the host
public class HostDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Set the host property
        swaggerDoc.Servers = new List<OpenApiServer>
        {
            new OpenApiServer { Url = "https://localhost:7091" }
        };
    }
}
