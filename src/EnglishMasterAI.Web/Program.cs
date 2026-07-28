using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EnglishMasterAI.Web.Components;
using EnglishMasterAI.Web.Components.Account;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Api;
using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}
else
{
    builder.Logging.AddJsonConsole();
}

builder.Services.Configure<AiOptions>(
    builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<SecurityOptions>(
    builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(
    builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<AlertingOptions>(
    builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<PronunciationOptions>(
    builder.Configuration.GetSection(PronunciationOptions.SectionName));

// Add services to the container.
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "Data", "ProtectionKeys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("EnglishMasterAI");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();
var connectionString = builder.Configuration.GetConnectionString(
    databaseOptions.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{databaseOptions.ConnectionStringName}' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (databaseOptions.IsPostgreSql)
    {
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsAssembly(
                "EnglishMasterAI.Migrations.PostgreSql"));
    }
    else
    {
        options.UseSqlite(
            connectionString,
            sqlite => sqlite.MigrationsAssembly(
                typeof(Program).Assembly.FullName));
    }
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var requireConfirmedAccount = builder.Configuration.GetValue<bool?>(
    $"{SecurityOptions.SectionName}:RequireConfirmedAccount")
    ?? !builder.Environment.IsDevelopment();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = requireConfirmedAccount;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender<ApplicationUser>, SmtpEmailSender>();
}

builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<LearningEngagementService>();
builder.Services.AddScoped<AssessmentService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<WritingFeedbackService>();
builder.Services.AddScoped<SpeakingAnalysisService>();
builder.Services.AddScoped<PronunciationAssessmentService>();
builder.Services.AddScoped<ReferenceAudioService>();
builder.Services.AddScoped<AiUsageService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ContentReviewService>();
builder.Services.AddScoped<PersonalDataService>();
builder.Services.AddScoped<OperationsDashboardService>();
builder.Services.AddSingleton<AiPracticeLimiter>();
builder.Services.AddSingleton<LearningTelemetry>();
builder.Services.AddHttpClient<OpenAiGateway>((services, client) =>
{
    var ai = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ai.TimeoutSeconds, 10, 120));
});
builder.Services.AddHttpClient<OperationalAlertService>();
builder.Services.AddHostedService<OperationalMonitorService>();

var observability = builder.Configuration
    .GetSection(ObservabilityOptions.SectionName)
    .Get<ObservabilityOptions>() ?? new ObservabilityOptions();
var openTelemetry = builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        observability.ServiceName,
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(LearningTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health/live");
            })
            .AddHttpClientInstrumentation(options => options.RecordException = true);

        if (Uri.TryCreate(
            observability.OtlpEndpoint,
            UriKind.Absolute,
            out var endpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(LearningTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (Uri.TryCreate(
            observability.OtlpEndpoint,
            UriKind.Absolute,
            out var endpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    });

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSingleton<StartupSecurityValidator>();

var securityOptions = builder.Configuration
    .GetSection(SecurityOptions.SectionName)
    .Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Please wait before trying again."
        }, cancellationToken);
    };

    options.AddPolicy("learning-api", context => CreateFixedWindowLimiter(
        PartitionKey(context),
        Math.Clamp(securityOptions.ApiRequestsPerMinute, 10, 600)));
    options.AddPolicy("ai-practice", context => CreateFixedWindowLimiter(
        PartitionKey(context),
        Math.Clamp(securityOptions.AiRequestsPerMinute, 2, 60)));
    options.AddPolicy("account-actions", context => CreateFixedWindowLimiter(
        PartitionKey(context),
        Math.Clamp(securityOptions.LoginRequestsPerMinute, 3, 60)));
});

var app = builder.Build();

app.Services.GetRequiredService<StartupSecurityValidator>().Validate();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler();
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints()
    .RequireRateLimiting("account-actions");
app.MapLearningApi();
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                    description = entry.Value.Description
                }),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            timestamp = DateTimeOffset.UtcNow
        }));
    }
}).AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration, app.Environment);
}

await app.RunAsync();

static RateLimitPartition<string> CreateFixedWindowLimiter(string partitionKey, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

static string PartitionKey(HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? "authenticated"
        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
