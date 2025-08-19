using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
// OnlineAssessment.Web.Data namespace removed as it's no longer needed
using System.Text;
using System.Text.Json.Serialization;
using System.Net;
using Microsoft.Extensions.FileProviders;
using OnlineAssessment.Web.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use only port 5058
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    try
    {
        // Use the port from configuration or default to 5058
        var kestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
        if (!string.IsNullOrEmpty(kestrelUrl))
        {
            // Extract port from URL if available
            var uri = new Uri(kestrelUrl);
            serverOptions.ListenLocalhost(uri.Port);
            Console.WriteLine($"Server bound to localhost:{uri.Port} from configuration");
        }
        else
        {
            // Listen on all IP addresses for EC2 deployment
            serverOptions.Listen(IPAddress.Any, 5058);
            Console.WriteLine("Server bound to all IP addresses on port 5058");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error binding to port: {ex.Message}");
        Console.WriteLine("Please make sure port 5058 is available or update the configuration.");
        throw;
    }
});

// ✅ Load Configuration explicitly
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
var configuration = builder.Configuration;

// Configure EPPlus license
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

// ✅ Ensure JWT Secret is Valid
var jwtSecret = configuration["JWT:Secret"];
if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 16)
{
    throw new Exception("JWT Secret Key is invalid! Ensure it is at least 16 characters long.");
}

// ✅ Add Database Context (MySQL) with performance optimizations
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 32)),
        mySqlOptions => {
            // PERFORMANCE OPTIMIZATION: Increase command timeout for long-running queries
            mySqlOptions.CommandTimeout(120);

            // PERFORMANCE OPTIMIZATION: Enable connection resiliency
            mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);

            // PERFORMANCE OPTIMIZATION: Enable batch operations for better throughput
            mySqlOptions.MaxBatchSize(100);

            // PERFORMANCE OPTIMIZATION: Use minimal logging in production
            if (!builder.Environment.IsDevelopment())
            {
                // MySQL provider doesn't support EnableSensitiveDataLogging directly
                // Use query splitting behavior for better performance with complex queries
                mySqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }
        }
    ));

// PERFORMANCE OPTIMIZATION: Add memory cache for frequently accessed data
builder.Services.AddMemoryCache();

// PERFORMANCE OPTIMIZATION: Add response compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[] {
        "text/plain",
        "text/css",
        "application/javascript",
        "text/html",
        "application/json",
        "application/xml",
        "text/xml"
    };
});

// ✅ Configure CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// ✅ Add Authentication with JWT and Cookies
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";

    // Read cookie settings from configuration if available
    var sameSiteSetting = builder.Configuration["Cookie:SameSite"];
    if (!string.IsNullOrEmpty(sameSiteSetting) && Enum.TryParse<SameSiteMode>(sameSiteSetting, true, out var sameSiteMode))
    {
        options.Cookie.SameSite = sameSiteMode;
    }
    else
    {
        // Default to Lax SameSite mode to allow redirects from payment gateway
        options.Cookie.SameSite = SameSiteMode.Lax;
    }

    // Read secure policy from configuration if available
    var securePolicy = builder.Configuration["Cookie:SecurePolicy"];
    if (!string.IsNullOrEmpty(securePolicy))
    {
        if (securePolicy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
        else if (securePolicy.Equals("Always", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else if (securePolicy.Equals("SameAsRequest", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
    }
    else
    {
        // Only use secure cookies in production with HTTPS
        if (builder.Environment.IsProduction() && builder.Configuration["UseHttps"] == "true")
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
    }
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = configuration["JWT:Issuer"],
        ValidAudience = configuration["JWT:Audience"]
    };
});

// ✅ Add Authorization
builder.Services.AddAuthorization();

// ✅ Add Controllers with JSON options
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
        options.JsonSerializerOptions.MaxDepth = 64;
    });

// Configure file upload limits
builder.Services.Configure<FormOptions>(options =>
{
    // Set the limit to 10 MB
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

// ✅ Configure Data Protection with a consistent application name
// This helps with session cookie decryption across app restarts
builder.Services.AddDataProtection()
    .SetApplicationName("OnlineAssessment");

// ✅ Add Session support with extended timeout and relaxed cookie settings
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromHours(24); // Extended timeout for payment flow and development
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".OnlineAssessment.Session"; // Set a specific name for the session cookie

    // For all environments, use relaxed cookie settings to ensure payment gateway redirects work
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
    options.Cookie.Path = "/";

    // Clear any existing session cookies to avoid decryption errors
    options.Cookie.MaxAge = TimeSpan.FromDays(1);

    Console.WriteLine("Session configured with relaxed cookie settings for payment gateway compatibility");
});

// Register OTP, Email, Password Reset, Rate Limiting, and Payment services
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddSingleton<IRateLimitingService, RateLimitingService>();
builder.Services.AddScoped<ISapIdGeneratorService, SapIdGeneratorService>();
builder.Services.AddScoped<RazorpayService>(); // <--- Register RazorpayService for DI

// Register Special User and Certificate services
builder.Services.AddScoped<ISpecialUserService, SpecialUserService>();
builder.Services.AddScoped<ICertificateService, CertificateService>();

// Register Organization Token service
builder.Services.AddScoped<IOrganizationTokenService, OrganizationTokenService>();

// Register IRazorpayService interface for dependency injection
builder.Services.AddScoped<IRazorpayService>(provider => provider.GetRequiredService<RazorpayService>());

// Register HttpClient for Razorpay API calls
builder.Services.AddHttpClient("Razorpay", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Initialize RazorpayHelper with configuration from appsettings.json
RazorpayHelper.Initialize(builder.Configuration);

// Configure Swagger with JWT Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OnlineAssessment API", Version = "v1" });

    // Add JWT Authorization to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by a space and your JWT token."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// Configure Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OnlineAssessment API v1"));
}

// PERFORMANCE OPTIMIZATION: Use response compression
app.UseResponseCompression();

// Only use HTTPS redirection if UseHttps is true
if (builder.Configuration["UseHttps"] == "true")
{
    app.UseHttpsRedirection();
}

// PERFORMANCE OPTIMIZATION: Add cache control headers for static files
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 7 days
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800");
    }
});

app.UseCors("AllowAll");  // Enable CORS globally
app.UseSession();         // Enable Session Middleware

// Add middleware to handle session errors after session middleware
app.Use(async (context, next) =>
{
    // Check if we have a session cookie but it's invalid
    var sessionCookie = context.Request.Cookies[".OnlineAssessment.Session"];
    if (!string.IsNullOrEmpty(sessionCookie))
    {
        try
        {
            // Try to access the session to see if it's valid
            _ = context.Session.Id;
        }
        catch (Exception ex)
        {
            // If there's an error with the session, clear the cookie and create a new session
            context.Response.Cookies.Delete(".OnlineAssessment.Session");
            Console.WriteLine($"Cleared invalid session cookie: {ex.Message}");

            // Create a new session
            context.Session.SetString("SessionReset", DateTime.UtcNow.ToString());
        }
    }

    await next();
});

app.UseAuthentication();  // Enable Authentication Middleware
app.UseAuthorization();   // Enable Authorization Middleware

// Configure routes
app.MapControllerRoute(
    name: "test",
    pattern: "Test/{action=Index}/{id?}",
    defaults: new { controller = "Test" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Register}/{id?}");

app.MapControllerRoute(
    name: "payment",
    pattern: "Payment/{action}/{id?}",
    defaults: new { controller = "Payment" });

app.MapControllerRoute(
    name: "categoryQuestions",
    pattern: "CategoryQuestions/{action=Index}/{id?}",
    defaults: new { controller = "CategoryQuestions" });

app.MapControllerRoute(
    name: "certificate",
    pattern: "Certificate/{action}/{id?}",
    defaults: new { controller = "Certificate" });

app.MapControllers();

// Ensure uploads directory exists
var uploadsPath = Path.Combine(builder.Environment.WebRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Ensure profiles directory exists
var profilesPath = Path.Combine(uploadsPath, "profiles");
if (!Directory.Exists(profilesPath))
{
    Directory.CreateDirectory(profilesPath);
    Console.WriteLine("Created profiles directory: " + profilesPath);
}

// PERFORMANCE OPTIMIZATION: Add static file serving for uploads with caching
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        // Cache uploaded files for 1 day
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=86400");
    }
});

app.Run();
