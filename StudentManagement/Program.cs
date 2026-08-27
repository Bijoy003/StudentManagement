using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Evaluation;
using StudentManagement.Configuration;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add in-memory caching
builder.Services.AddMemoryCache();

builder.Configuration
    .AddJsonFile(builder.Configuration["AdditionalConfig:Path"], optional: false, reloadOnChange: true);

// Load configuration
builder.Services.Configure<IpRateLimitOptions>(
    builder.Configuration.GetSection("IpRateLimiting"));

// Required services
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("DatabaseConnection")
    ));
// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password settings
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireDigit = true;               // Require at least one number
    options.Password.RequireLowercase = true;           // Require at least one lowercase letter
    options.Password.RequireUppercase = true;           // Require at least one uppercase letter
    options.Password.RequiredLength = 4;                // Minimum password length
    options.SignIn.RequireConfirmedEmail = false;       // Set it to false to enable login with google without confirmation
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    });

// Mailgun email service (IMPORTANT)
builder.Services.AddHttpClient<IMailgunEmailService, MailgunEmailService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Services
builder.Services.AddScoped<IStudentRepository, StudentRepository>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<ICourseRepository, CourseRepository>();
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IPrimeNumberService, PrimeNumberService>();

builder.Services.AddAppChatClient(builder.Configuration);

builder.Services.AddEvaluationServices(builder.Configuration);

builder.Services.AddSingleton<IAppKnowledgeService, AppKnowledgeService>();
builder.Services.AddScoped<ChatTools>();
builder.Services.AddScoped<IChatService, ChatService>();
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Global exception handler
    app.UseExceptionHandler("/Home/Error");

    // Handles status code pages (e.g., 404)
    app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage(); // Shows detailed errors in dev
}

// --- MIGRATION AND SEEDING BLOCKS ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<ApplicationDbContext>();

    int retryCount = 0;
    int maxRetries = 10;

    while (retryCount < maxRetries)
    {
        try
        {
            logger.LogInformation("Connecting to database (Attempt {Attempt}/{MaxRetries})...", retryCount + 1, maxRetries);

            // 1. Run Migrations
            await context.Database.MigrateAsync();

            // 2. Run Seeding
            await DbInitializer.SeedAdmin(services);

            logger.LogInformation("Database migration and seeding successful.");
            break; // Success! Exit the loop.
        }
        catch (Exception ex)
        {
            retryCount++;
            if (retryCount >= maxRetries)
            {
                logger.LogCritical(ex, "Could not connect to database after {MaxRetries} attempts. Application is shutting down.", maxRetries);
                throw;
            }

            logger.LogWarning("Database not ready yet (Name or service not known). Retrying in 5 seconds...");
            await Task.Delay(5000); // Wait 5 seconds before trying again
        }
    }
}

app.UseIpRateLimiting();
app.UseHttpsRedirection();
app.UseStaticFiles();

//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(
//        Path.Combine(builder.Environment.ContentRootPath, "wwwroot/images")),
//    RequestPath = "/images",
//    OnPrepareResponse = ctx =>
//    {
//        ctx.Context.Response.Headers["Cache-Control"] =
//            "public,max-age=604800"; // 7 days
//    }
//});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "wwwroot/css")),
    RequestPath = "/css",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] =
            "public,max-age=86400"; // 1 day
    }
});


app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
