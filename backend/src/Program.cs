using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Entity Framework and PostgreSQL
builder.Services.AddDbContext<TimeTrackingDbContext>(options =>
{
    // Check for DATABASE_URL environment variable first (Docker/production)
    var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
    
    // If DATABASE_URL is in postgres:// format, convert it to EF Core format
    if (!string.IsNullOrEmpty(connectionString) && connectionString.StartsWith("postgres://"))
    {
        var uri = new Uri(connectionString);
        var host = uri.Host;
        var port = uri.Port;
        var database = uri.AbsolutePath.TrimStart('/');
        var username = uri.UserInfo.Split(':')[0];
        var password = uri.UserInfo.Split(':')[1];
        
        connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
    else
    {
        // Fallback to appsettings.json (local development)
        connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    }
    
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Database connection string not found. Set DATABASE_URL or ConnectionStrings:DefaultConnection.");
    }
    
    options.UseNpgsql(connectionString);
});

// Add CORS for frontend communication
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

// Enable CORS
app.UseCors("AllowOrigins");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "dotnet-time-tracking-backend"
}));

app.Run();
