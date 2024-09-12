
using RestApiClient;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.RestApiClientGetewayServices();


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// Add NSwag services
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "HR.Employee.Api";
    config.Version = "v1";
});

var app = builder.Build();

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
