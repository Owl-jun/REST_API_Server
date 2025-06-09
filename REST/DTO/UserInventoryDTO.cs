using REST_API.Entity;

namespace REST_API.DTO
{
    public class UserInventoryDTO
    {
        public int UserId { get; set; }
        public Dictionary<int,List<Inventory>> Inventory { get; set; } = new();

    }
}
