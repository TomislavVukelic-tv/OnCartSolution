using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rhetos;

namespace CatalogService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello World!");

            app.Run();

        }
    }
}
