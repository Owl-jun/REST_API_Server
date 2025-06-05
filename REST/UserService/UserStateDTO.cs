namespace REST_API.UserService
{
    public class UserStateDTO
    {
        public int UserId { get; set; }

        public string Token { get; set; } = "";

        public string UserName { get; set; } = "";
    }
}
