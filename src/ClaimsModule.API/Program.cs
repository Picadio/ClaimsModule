using ClaimsModule.API.OpenApi;
using ClaimsModule.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(OpenApiConfiguration.AddBearerSupport);
builder.AddClaimsAuthentication();

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "ClaimsModule API";
        options.SwaggerEndpoint("../openapi/v1.json", "ClaimsModule API v1");
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
