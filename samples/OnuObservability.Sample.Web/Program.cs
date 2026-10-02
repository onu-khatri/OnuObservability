using System.Diagnostics;
using OnuObservability.AspNetCore;
using OnuObservability.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOnuObservability(builder.Configuration)
    .AddOnuAspNetCore();
var app = builder.Build();

app.UseOnuObservability();
app.MapGet("/", () => "Hello World!");
app.MapGet("/correlation", () => Results.Ok(new
{
    TraceId = Activity.Current?.TraceId.ToHexString(),
    SpanId = Activity.Current?.SpanId.ToHexString(),
}));
app.MapGet("/users/{id}", (string id) => Results.Ok(new { Id = id, Found = true }));
app.MapGet("/validation", () => Results.BadRequest());
app.MapGet("/failure", () => Results.Problem(statusCode: StatusCodes.Status500InternalServerError));
app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
{
    lifetime.StopApplication();
    return Results.Accepted();
});

app.Run();
