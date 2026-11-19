using System.Text.Json.Serialization;
using ClaimsModule.API.Errors;
using ClaimsModule.API.OpenApi;
using ClaimsModule.API.Security;
using ClaimsModule.Application;
using ClaimsModule.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(OpenApiConfiguration.AddBearerSupport);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ClaimExceptionHandler>();
builder.Services.AddClaimsApplication();
builder.Services.AddClaimsPersistence(builder.Configuration.GetConnectionString("Claims"));
builder.AddClaimsAuthentication();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
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
