using REST_API.UserService;

namespace REST_API.DTO
{
    public class MessageLogDTO
    {
        public int oper {  get; set; }
        public UserStateDTO UserState { get; set; }
    }
}
