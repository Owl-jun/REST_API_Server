using REST_API.DBModel;

namespace REST_API.DTO
{
    public class UserDTO
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<InventoryDTO> Inventory { get; set; } = new();

    }
}
