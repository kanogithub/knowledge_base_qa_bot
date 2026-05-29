using System.Text;
using CloudKB.Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for HTTP/2 support (needed for SSE multiplexing)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

// Add Service Defaults
builder.AddServiceDefaults();

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var secretKey = builder.Configuration["Auth:SecretKey"];
        if (!string.IsNullOrEmpty(secretKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Auth:Issuer"] ?? "cloudkb-auth",
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Auth:Audience"] ?? "cloudkb-api",
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuerSigningKey = true
            };
        }
        else
        {
            options.Authority = builder.Configuration["Auth:Authority"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidAudience = builder.Configuration["Auth:Audience"]
            };
        }
    });

builder.Services.AddAuthorization();

// Configure Yarp with custom Tenant ID transform provider
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms<TenantIdTransformProvider>();



var app = builder.Build();

// Map default endpoints
app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// Health Check Endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Route through Yarp
app.MapReverseProxy();

app.Run();

namespace CloudKB.Gateway
{
    public partial class Program { }
}
