using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using REST_API.DBModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using REST_API.Common;
using REST_API.DTO;
using MySqlConnector;

namespace REST_API.UserService
{

    public class UserService
    {
        private readonly Dbconn _db;
        private readonly RedisService _redis;
        private readonly IConfiguration _config;
        public UserService(Dbconn db, RedisService redis, IConfiguration config)
        {
            _db = db;
            _redis = redis;
            _config = config;
        }

        public async Task<(bool Success, string Message)> RegisterAsync(User user)
        {
            try
            {
                string hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
                user.Password = hashed;

                // DB 저장
                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                return (true, "회원가입 완료");
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx &&
                                           mysqlEx.Number == 1062) // Duplicate entry
            {
                return (false,"이미 존재하는 사용자 ID입니다.");
            }
            catch (DbUpdateException ex)
            {
                return (false,"가입 실패" + ex.Message);
            }
            catch (Exception ex)
            {
                return (false,"서버 오류");
            }
        }

        public async Task<(bool Success, string Message, string? Token, UserStateDTO? User)> LoginAsync(LoginRequest req)
        {
            if (await _redis.ExistsAsync($"user:token:{req.Username}"))
                return (false, "이미 접속 중인 유저입니다.", null, null);

            var user = await _db.Users.Include(u => u.Inventory)
                                      .ThenInclude(inv => inv.Item)
                                      .FirstOrDefaultAsync(u => u.Username == req.Username);

            if (user == null)
                return (false, "존재하지 않는 사용자입니다.", null, null);

            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
                return (false, "비밀번호가 일치하지 않습니다.", null, null);

            string token = JwtHelper.GenerateJwtToken(user, _config);

            var value = new UserStateDTO { Token = token, UserId = user.Id, UserName = user.Username };
            await _redis.SetAsync($"user:state:{user.Username}", JsonSerializer.Serialize(value), TimeSpan.FromHours(1));

            // 동기화 메시지 전송
            var msg = new MessageLogDTO { oper = 0, UserState = value };   // 0 : Login , 1 : Logout
            await _redis.Publish("user:state:update", JsonSerializer.Serialize(msg));

            return (true, "로그인 성공", token, value);
        }

        public async Task<(bool Success, string Message)> LogoutAsync(HttpContext ctx)
        {
            var auth = ctx.Request.Headers["Authorization"].ToString();
            var token = auth["Bearer ".Length..].Trim();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return (false,"유효하지 않은 토큰 형식입니다.");

            var username = JwtHelper.ExtractUsernameFromJwt(token);
            if (username == null)
                return (false, "토큰 파싱 실패.");

            var cached = await _redis.GetAsync($"user:token:{username}");
            if (cached != token)
                return (false,"이미 만료되었거나 유효하지 않은 세션 입니다.");

            await _redis.DeleteAsync($"user:state:{username}");

            // 동기화 메시지 전송
            var msg = new MessageLogDTO { oper = 1, UserState = new UserStateDTO { UserName = username } };   // 0 : Login , 1 : Logout
            string message = JsonSerializer.Serialize(msg);
            await _redis.Publish("user:state:update", message);

            return (true, "로그아웃 완료");

        }

    }
}

