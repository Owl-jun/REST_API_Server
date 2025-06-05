using REST_API.DTO;
using REST_API.UserService;

namespace REST_API.LocalCache
{
    public static class LocalCache
    {
        public static Dictionary<int, UserStateDTO> UserState = new();
        public static Dictionary<int, UserInventoryDTO> UserInventory = new();
    }
}
