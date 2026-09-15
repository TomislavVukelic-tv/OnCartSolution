using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Rhetos;

namespace CatalogService;

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
        return Microsoft.Extensions.Hosting.Host
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
                    Title = "CatalogService",
                    Version = "v1"
                });
        });

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                //options.Authority = configuration["Jwt:Authority"]; not needed in demo
                options.Audience = configuration["Jwt:Audience"];
                options.RequireHttpsMetadata = true;
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
                    "CatalogService v1");
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
