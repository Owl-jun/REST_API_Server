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
// 환경설정
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
#region JWT 관련
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
#region 포멜로 DB연동 옵션
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
#region Swagger 옵션
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
        Description = "JWT 토큰을 아래 입력창에 입력하세요. (예: Bearer eyJhbGciOiJIUzI1...)"
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
// API 구현부
#region TEST
app.MapGet("/", async (RedisService _redis) =>
{
    await _redis.DeleteAsync($"user:token:admin");
    return Results.Ok(" GO TO /Swagger");
})
.WithSummary("서버 개발자 단위 테스트용 API");
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
.WithSummary("회원가입요청 API")
.WithDescription("중복 가입 체크 후 성공 여부를 반환합니다.");

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
        return Results.Problem("서버 오류" + ex.Message, statusCode: 500);
    }
})
.WithName("Login")
.WithTags("Users")
.WithSummary("로그인 요청 API")
.WithDescription("실패시 존재 여부, 비밀번호 불일치, 중복 로그인 여부, 서버 오류 구분하여 리턴합니다.");

USER.MapPost("/logout", async (HttpContext ctx, UserService userService) =>
{
    var (success, message) = await userService.LogoutAsync(ctx);
    if (!success) return Results.Problem(message, statusCode: 401);
    return Results.Ok("로그아웃 성공");
})
.WithName("LOGOUT")
.WithTags("Users")
.WithSummary("로그아웃 요청 API")
.WithDescription("클라이언트 종료시 필수적으로 호출해야하는 API 입니다. (클라이언트 동기화)");
#endregion

var INV = app.MapGroup("/inventory");

//INV.MapGet("/", async (InvGetRequest UserInfo) =>
//{
    
//})
//.WithName("GETINVENTORY")
//.WithTags("Inventory")
//.WithSummary("인벤토리 정보 요청 API")
//.WithDescription("인벤토리 정보를 요청합니다, Body에 UserId , Token을 첨부 해야합니다.");


app.Run();


