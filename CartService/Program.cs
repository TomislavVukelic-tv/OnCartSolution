using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Rhetos;
using System.Text;

namespace CartService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        ConfigurePipeline(app);

        app.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureServices((context, services) =>
                {
                    ConfigureServices(
                        services,
                        context.Configuration);
                });
            });
    }

    private static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {

        services
            .AddRhetosHost((serviceProvider, rhetosHostBuilder) =>
            {
                rhetosHostBuilder
                    .ConfigureRhetosAppDefaults()
                    .UseBuilderLogProviderFromHost(serviceProvider)
                    .ConfigureConfiguration(cfg =>
                        cfg
                            .MapNetCoreConfiguration(configuration)
                            .AddJsonFile(
                                "rhetos-app.local.settings.json",
                                optional: true));
            })
            .AddAspNetCoreIdentityUser()
            .AddHostLogging()
            .AddDashboard()
            .AddRestApi(options =>
            {
                options.BaseRoute = "rest";

                options.GroupNameMapper =
                    (conceptInfo, controller, oldName) => "v1";
            });

        services.AddControllers();

        services.AddSwaggerGen(options =>
        {
            options.CustomSchemaIds(type => type.ToString());

            options.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = "CartService",
                    Version = "v1"
                });

            options.AddSecurityDefinition(
                JwtBearerDefaults.AuthenticationScheme,
                new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Description = "Paste the JWT from the auth service (no \"Bearer \" prefix).",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    JwtBearerDefaults.AuthenticationScheme,
                    document)] = []
            });
        });

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var signingKey = configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("SigningKey is not set.");

                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = configuration["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = configuration["Jwt:Audience"],
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),

                        RoleClaimType =
                            System.Security.Claims.ClaimTypes.Role,
                        NameClaimType =
                            System.Security.Claims.ClaimTypes.NameIdentifier
                    };
            })
            .AddCookie(options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status401Unauthorized;

                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();


        var connectionString =
            configuration.GetConnectionString("RhetosConnectionString")!;

        services
            .AddHealthChecks()
            .AddSqlServer(
                connectionString,
                name: "sql-server",
                tags: ["ready"]);

        foreach (var name in new[] { "events", "inventory" })
        {
            services
                .AddHttpClient(name)
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    // DEV hack to allow self-signed certificates to not cause issues.
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });
        }

        services.AddHostedService<OutboxPublisher>();
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/swagger/v1/swagger.json",
                    "CartService v1");
            });
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseRhetosRestApi();

        app.MapControllers();

        app.MapRhetosDashboard();

        app.MapHealthChecks("/health");
    }
}
