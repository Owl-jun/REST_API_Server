using REST_API.DTO;

namespace REST_API.LocalCache
{
    public class UserData
    {
        public int Oper {  get; set; }
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public List<InventoryDTO> Inventory { get; set; } = new();
    }
}
