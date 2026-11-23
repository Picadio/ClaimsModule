using System.Text.Json.Serialization;
using ClaimsModule.API.Correlation;
using ClaimsModule.API.Errors;
using ClaimsModule.API.OpenApi;
using ClaimsModule.API.Security;
using ClaimsModule.Application;
using ClaimsModule.Application.Claims;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure.Storage;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.InvalidModelState);
builder.Services.AddOpenApi(OpenApiConfiguration.AddBearerSupport);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    ApiProblems.AddCorrelation(context.HttpContext, context.ProblemDetails));
builder.Services.AddExceptionHandler<ClaimExceptionHandler>();
builder.Services.AddClaimsApplication();
var blobConnection = builder.Configuration.GetConnectionString("Blob");
var storageProvider = builder.Configuration.GetValue("StorageProvider",
    !string.IsNullOrWhiteSpace(blobConnection) ? DocumentStorageProvider.AzureBlob
        : builder.Environment.IsDevelopment() ? DocumentStorageProvider.LocalFileSystem : DocumentStorageProvider.Unavailable);
var localRoot = Path.GetFullPath(builder.Configuration["Storage:LocalRoot"] ?? "uploads", builder.Environment.ContentRootPath);
builder.Services.AddClaimDocumentStorage(storageProvider, blobConnection, localRoot);
var claimsConnection = builder.Configuration.GetConnectionString("Claims");
builder.Services.AddClaimsPersistence(claimsConnection);
if (builder.Configuration.GetValue("Jobs:Enabled", !string.IsNullOrWhiteSpace(claimsConnection)))
{
    if (string.IsNullOrWhiteSpace(claimsConnection))
    {
        throw new InvalidOperationException("GL posting jobs require a claims SQL connection.");
    }

    var organizations = builder.Configuration.GetSection("Jobs:OrganizationEntityIds").Get<Guid[]>()
        ?? [builder.Configuration.GetValue<Guid>("Authentication:Demo:OrganizationEntityId")];
    builder.Services.AddReservePostingJobs(builder.Configuration.GetConnectionString("Hangfire") ?? claimsConnection, organizations);
}
builder.Services.AddSingleton(new ClaimLifecycleOptions(builder.Configuration.GetValue<bool>("Claims:AllowDirectClosure")));
builder.AddClaimsAuthentication();

var app = builder.Build();

app.UseMiddleware<RequestCorrelationMiddleware>();
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
