using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using REST_API;
using REST_API.DBModel;
using REST_API.DTO;
using REST_API.Helpers;
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
// ---------------------------------------------------------
// 환경설정
var builder = WebApplication.CreateBuilder(args);
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

USER.MapPost("/register", async (User user, Dbconn db) =>
{
    try
    {
        // 비밀번호 해싱
        string hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
        user.Password = hashed;

        // DB 저장
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Ok("회원가입 완료");
    }
    catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx &&
                                   mysqlEx.Number == 1062) // Duplicate entry
    {
        return Results.Conflict("이미 존재하는 사용자 ID입니다.");
    }
    catch (DbUpdateException ex)
    {
        return Results.Problem("가입 실패" + ex.Message, statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Problem("서버 오류", statusCode: 500);
    }
})
.WithName("RegisterUser")
.WithTags("Users")
.WithSummary("회원가입요청 API")
.WithDescription("중복 가입 체크 후 성공 여부를 반환합니다.");

// LOGIN
USER.MapPost("/login", async (LoginRequest req, Dbconn db, IConfiguration config, RedisService _redis) =>
{
    try
    {
        // Check Redis Cache
        if (await _redis.ExistsAsync($"user:state:{req.Username}"))
            return Results.Problem("이미 접속 중인 유저입니다.", statusCode: 401);

        var user = await db.Users.Include(u => u.Inventory)
                                 .ThenInclude(inv => inv.Item)
                                 .FirstOrDefaultAsync(u => u.Username == req.Username);
        // DB Query
        if (user is null)
            return Results.Problem("존재하지 않는 사용자입니다.", statusCode: 401);

        // 비밀번호 검증
        bool isValid = BCrypt.Net.BCrypt.Verify(req.Password, user.Password);
        if (!isValid)
            return Results.Problem("비밀번호가 일치하지 않습니다.", statusCode: 401);

        // JWT 토큰 발급
        string token = JwtHelper.GenerateJwtToken(user, config);

        // REDIS 캐싱
        var RedisValue = new UserStateDTO { Token = token, UserId = user.Id, UserName = user.Username };
        string value = JsonSerializer.Serialize(RedisValue);
        await _redis.SetAsync($"user:state:{user.Username}", value, TimeSpan.FromHours(1));

        // 동기화 메시지 전송
        var msg = new MessageLogDTO { oper = 0, UserState = RedisValue };   // 0 : Login , 1 : Logout
        string message = JsonSerializer.Serialize(msg);
        await _redis.Publish("user:state:update", message);

        return Results.Ok(RedisValue);
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

// LOGOUT
USER.MapPost("/logout", async (HttpContext ctx, RedisService _redis) =>
{
    var auth = ctx.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
        return Results.Problem("유효하지 않은 토큰 형식입니다.", statusCode: 401);

    var token = auth["Bearer ".Length..].Trim();
    var username = JwtHelper.ExtractUsernameFromJwt(token);
    if (username == null)
        return Results.Problem("토큰 파싱 실패", statusCode: 401);

    var cached = await _redis.GetAsync($"user:token:{username}");
    if (cached != token)
        return Results.Problem("이미 만료되었거나 유효하지 않은 세션 입니다.", statusCode: 401);

    await _redis.DeleteAsync($"user:state:{username}");

    // 동기화 메시지 전송
    var msg = new MessageLogDTO { oper = 1, UserState = new UserStateDTO { UserName = username } };   // 0 : Login , 1 : Logout
    string message = JsonSerializer.Serialize(msg);
    await _redis.Publish("user:state:update", message);

    return Results.Ok("로그아웃 성공");
})
.WithName("LOGOUT")
.WithTags("Users")
.WithSummary("로그아웃 요청 API")
.WithDescription("클라이언트 종료시 필수적으로 호출해야하는 API 입니다. (클라이언트 동기화)");
#endregion

app.Run();


// ---------------------------------------------------------
// DTO
record LoginRequest(string Username, string Password);
// ---------------------------------------------------------
