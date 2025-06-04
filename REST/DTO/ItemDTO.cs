using REST_API.DBModel;

namespace REST_API.DTO
{
    public class ItemDTO
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public string Rarity { get; set; } = "";

    }
}
