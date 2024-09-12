using HR.LeaveManagement.Api.MIddleware;
using HR.LeaveManagement.Application;
using HR.LeaveManagement.Infrastructure;
using HR.LeaveManagement.Persistence;
using Microsoft.OpenApi.Models;
using RestApiClient;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.RestApiClientGetewayServices();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.RestApiClientGetewayServices();

builder.Services.AddControllers();

builder.Services.AddCors(e => e.AddPolicy("all", builder =>
{
    builder.AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader();
    //.WithOrigins("https://localhost:4201", "http://localhost:4202"));
}));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(
    options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "HR.LeaveManagement.Api", Version = "v1" });

        // Add custom document filter to set the host
        options.DocumentFilter<HostDocumentFilter>();
    });

// Add NSwag services
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "HR.LeaveManagement.Api";
    config.Version = "v1";
    // Other configurations
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Add NSwag middleware
app.UseOpenApi(); // Serve the OpenAPI/Swagger document

app.Run();
