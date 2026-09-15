using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Transforms;

namespace Gateway
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
                .AddTransforms(context =>
                {
                    context.AddRequestTransform(transform =>
                    {
                        transform.ProxyRequest.Headers.Remove("X-User-Id");
                        transform.ProxyRequest.Headers.Remove("X-User-Roles");

                        var user = transform.HttpContext.User;
                        if (user?.Identity?.IsAuthenticated == true)
                        {
                            var sub = GetUserClaim(user);
                            if (!string.IsNullOrEmpty(sub))
                                transform.ProxyRequest.Headers.Add("X-User-Id", sub);

                            var roles = GetRoleClaims(user);
                            if (!string.IsNullOrEmpty(roles))
                                transform.ProxyRequest.Headers.Add("X-User-Roles", roles);
                        }

                        return ValueTask.CompletedTask;
                    });
                });

            var jwt = builder.Configuration.GetSection("Jwt");
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    //options.Authority = jwt["Authority"]; //not needed for demo
                    options.Audience = jwt["Audience"];
                    var signingKey = jwt["SigningKey"];
                    if (!string.IsNullOrEmpty(signingKey))
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = jwt["Issuer"],
                            ValidateAudience = true,
                            ValidAudience = jwt["Audience"],
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromSeconds(30),
                        };
                        options.RequireHttpsMetadata = false;
                    }
                });

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
            });

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    return RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                    });
                });
            });

            builder.Services.AddHealthChecks();

            var app = builder.Build();

            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapHealthChecks("/health");

            app.MapReverseProxy();

            app.Run();
        }


        private static string? GetUserClaim(ClaimsPrincipal user)
        {
            return user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static string? GetRoleClaims(ClaimsPrincipal user)
        {
            return string.Join(',', user.FindAll(ClaimTypes.Role).Select(r => r.Value));
        }

        private static string GetPartitionKey(HttpContext httpContext)
        {
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                return httpContext.User.FindFirst("sub")?.Value ?? "authenticated";
            }
            else
            {
                return httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
            }
        }
    }
}
