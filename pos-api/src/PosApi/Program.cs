using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using PosApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── DB ──
var conn = builder.Configuration.GetConnectionString("PosDb")
    ?? throw new InvalidOperationException("ConnectionStrings:PosDb missing");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    o.UseNpgsql(conn, npgsql => npgsql.EnableRetryOnFailure(3));
});

// ── JWT ──
var jwtCfg = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Section Jwt missing");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<ITicketService, TicketService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtCfg.Issuer,
            ValidAudience = jwtCfg.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtCfg.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("ManagerOnly", p => p.RequireRole("Manager"));
    o.AddPolicy("AnyRole", p => p.RequireRole("Cashier", "Manager"));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Swagger ──
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DigiSME POS API",
        Version = "v1",
        Description = "Backend POS Bán Lẻ SME — Phase 0 (Auth, Health, Product lookup mẫu)."
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập access token (không cần 'Bearer ') — lấy từ POST /api/v1/auth/login",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "DigiSME POS API v1");
    });
}

app.UseMiddleware<GlobalExceptionHandler>();

app.UseCors(o => o.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("x-ratelimit-remaining"));
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── Migrate + Seed ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
        await DbInitializer.SeedAsync(scope.ServiceProvider);
        app.Logger.LogInformation("Database migrated & seeded.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "DB migration/seed failed — API sẽ chạy nhưng các endpoint phụ thuộc DB sẽ lỗi.");
    }
}

app.Run();

public partial class Program { }
