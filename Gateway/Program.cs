using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
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


            var jwt = builder.Configuration.GetSection("Jwt");
            var jwtIssuer = jwt["Issuer"];
            var jwtAudience = jwt["Audience"];
            var jwtSigningKey = jwt["SigningKey"];

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
                        else if (!string.IsNullOrEmpty(jwtSigningKey))
                        {
                            // Anonymous caller: mint a short-lived read-only 'guest'
                            // token so downstream services still receive a valid,
                            // least-privilege identity (guest -> customer role).
                            var guestToken = GenerateGuestToken(jwtIssuer, jwtAudience, jwtSigningKey);
                            transform.ProxyRequest.Headers.Remove("Authorization");
                            transform.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {guestToken}");

                            transform.ProxyRequest.Headers.Add("X-User-Id", "guest");
                            transform.ProxyRequest.Headers.Add("X-User-Roles", "customer");
                        }

                        return ValueTask.CompletedTask;
                    });
                });

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    //options.Authority = jwt["Authority"]; //not needed for demo
                    options.Audience = jwtAudience;
                    if (!string.IsNullOrEmpty(jwtSigningKey))
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = jwtIssuer,
                            ValidateAudience = true,
                            ValidAudience = jwtAudience,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
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

        private static string GenerateGuestToken(string? issuer, string? audience, string signingKey)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "guest"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "customer"),
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
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
