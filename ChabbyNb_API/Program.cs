using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ChabbyNb_API.Data;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using ChabbyNb_API.Services;
using ChabbyNb_API.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using ChabbyNb_API.Authorization;
using ChabbyNb_API.Services.Iterfaces;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Configure MVC/API controllers more explicitly
builder.Services.AddControllers(options => {
    options.SuppressAsyncSuffixInActionNames = false;
})
.AddJsonOptions(options => {
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Add API explorer for Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChabbyNb API",
        Version = "v1",
        Description = "Apartment rental API for ChabbyNb"
    });

    // Define the JWT security scheme
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Ensure WebRootPath is set
if (string.IsNullOrEmpty(builder.Environment.WebRootPath))
{
    builder.Environment.WebRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    Directory.CreateDirectory(builder.Environment.WebRootPath);
}

// Add database context
builder.Services.AddDbContext<ChabbyNbDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add session services (we'll keep these for backward compatibility during transition)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IPaymentService, StripePaymentService>();

// Add our new authentication services
builder.Services.AddHttpContextAccessor(); // Add this line to register IHttpContextAccessor
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountLockoutService, AccountLockoutService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAccountLockoutService, AccountLockoutService>();


// Register authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, HousekeepingAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ReadOnlyAuthorizationHandler>();

// Add background services
builder.Services.AddHostedService<BookingExpirationService>();

// Add session config
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ClockSkew = TimeSpan.Zero  // Remove clock skew to ensure tokens expire exactly at expiry time
    };

    // Enable using the token from the query string for SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    // Legacy admin role policy
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c =>
                (c.Type == "IsAdmin" && c.Value == "True") ||
                (c.Type == ClaimTypes.Role && c.Value == UserRole.Admin.ToString()))));

    // New role-based policies
    options.AddPolicy("RequireHousekeepingRole", policy =>
        policy.AddRequirements(new HousekeepingRequirement()));

    options.AddPolicy("RequireReadOnlyRole", policy =>
        policy.AddRequirements(new ReadOnlyRequirement()));

    // Minimum role level policies
    options.AddPolicy("RequireAdminRoleNew", policy =>
        policy.AddRequirements(new RoleAuthorizationRequirement(UserRole.Admin)));

    options.AddPolicy("RequireHousekeepingRoleMin", policy =>
        policy.AddRequirements(new RoleAuthorizationRequirement(UserRole.CleaningStaff)));

    options.AddPolicy("RequireReadOnlyRoleMin", policy =>
        policy.AddRequirements(new RoleAuthorizationRequirement(UserRole.CleaningStaff)));
});

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/api/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseSession(); // Keep for backward compatibility during transition

// Map controllers
app.MapControllers();
Console.WriteLine("Controllers mapped successfully");

app.Run();