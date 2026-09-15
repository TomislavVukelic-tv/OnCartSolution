using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthService
{
    public class Program
    {

        static readonly Dictionary<string, (string Password, string CustomerId, string[] Roles)> _users = new Dictionary<string, (string Password, string CustomerId, string[] Roles)>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = ("admin123", "customer-admin-9999", new[] { "customer", "admin" }),
            ["user"] = ("User1234", "customer-user-0002", new[] { "customer" }),
        };

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            var jwt = app.Configuration.GetSection("Jwt");

            app.MapPost("/login", Login);

            app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

            app.Run();

        }

        static IResult Login(LoginRequest req, IConfiguration config)
        {
            var jwt = config.GetSection("Jwt") ?? throw new InvalidOperationException("Jwt configuration section is missing.");
            var signingKey = jwt["SigningKey"] ?? throw new InvalidOperationException("SigningKey is not set.");
            var issuer = jwt["Issuer"];
            var audience = jwt["Audience"];

            if (req is null || string.IsNullOrWhiteSpace(req.Username) || !_users.TryGetValue(req.Username, out var user) || user.Password != req.Password)
            {
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var now = DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, req.Username),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("customerId", user.CustomerId),
            };
            claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: now,
                expires: now.AddHours(1),
                signingCredentials: creds);

            return Results.Ok(new
            {
                access_token = new JwtSecurityTokenHandler().WriteToken(token),
                token_type = "Bearer",
                expires_in = 3600,
                customerId = user.CustomerId,
            });
        }

        record LoginRequest(string Username, string Password);
    }
}
