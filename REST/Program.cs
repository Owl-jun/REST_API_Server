using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using REST_API;
using REST_API.DBModel;
using REST_API.DTO;
using REST_API.Common;
using REST_API.LocalCache;
using StackExchange.Redis;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using REST_API.UserService;
// ---------------------------------------------------------
// ȯ�漳��
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<UserService>();
#region REDIS
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse("localhost:6379", true);
    configuration.ResolveDns = true;
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddSingleton<RedisService>();

#endregion
#region JWT ����
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var config = builder.Configuration;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization();

#endregion
#region ����� DB���� �ɼ�
builder.Services.AddDbContext<Dbconn>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    ));

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
#endregion
#region Swagger �ɼ�
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT ��ū�� �Ʒ� �Է�â�� �Է��ϼ���. (��: Bearer eyJhbGciOiJIUzI1...)"
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
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
#endregion
// ---------------------------------------------------------
// ---------------------------------------------------------
// API ������
#region TEST
app.MapGet("/", async (RedisService _redis) =>
{
    await _redis.DeleteAsync($"user:token:admin");
    return Results.Ok(" GO TO /Swagger");
})
.WithSummary("���� ������ ���� �׽�Ʈ�� API");
#endregion

#region UserAPI
var USER = app.MapGroup("/user");

USER.MapPost("/register", async (User user, UserService userService) =>
{
    var (success, message) = await userService.RegisterAsync(user);
    if (!success) return Results.Problem(message, statusCode:401);
    return Results.Ok(message);
})
.WithName("RegisterUser")
.WithTags("Users")
.WithSummary("ȸ�����Կ�û API")
.WithDescription("�ߺ� ���� üũ �� ���� ���θ� ��ȯ�մϴ�.");

USER.MapPost("/login", async (LoginRequest req, UserService userService) =>
{
    try
    {
        var (success, message, token, userDto) = await userService.LoginAsync(req);
        if (!success) return Results.Problem(message, statusCode: 401);
        return Results.Ok(new { message, token, user = userDto });
    }
    catch (Exception ex)
    {
        return Results.Problem("���� ����" + ex.Message, statusCode: 500);
    }
})
.WithName("Login")
.WithTags("Users")
.WithSummary("�α��� ��û API")
.WithDescription("���н� ���� ����, ��й�ȣ ����ġ, �ߺ� �α��� ����, ���� ���� �����Ͽ� �����մϴ�.");

USER.MapPost("/logout", async (HttpContext ctx, UserService userService) =>
{
    var (success, message) = await userService.LogoutAsync(ctx);
    if (!success) return Results.Problem(message, statusCode: 401);
    return Results.Ok("�α׾ƿ� ����");
})
.WithName("LOGOUT")
.WithTags("Users")
.WithSummary("�α׾ƿ� ��û API")
.WithDescription("Ŭ���̾�Ʈ ����� �ʼ������� ȣ���ؾ��ϴ� API �Դϴ�. (Ŭ���̾�Ʈ ����ȭ)");
#endregion

var INV = app.MapGroup("/inventory");

//INV.MapGet("/", async (InvGetRequest UserInfo) =>
//{
    
//})
//.WithName("GETINVENTORY")
//.WithTags("Inventory")
//.WithSummary("�κ��丮 ���� ��û API")
//.WithDescription("�κ��丮 ������ ��û�մϴ�, Body�� UserId , Token�� ÷�� �ؾ��մϴ�.");


app.Run();


