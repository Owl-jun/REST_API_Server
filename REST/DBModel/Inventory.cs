using System.Text.Json.Serialization;

namespace REST_API.DBModel
{
    public class Inventory
    {
        public int User_Id { get; set; } 
        public int Item_Id { get; set; }
        public int Quantity { get; set; }
        public DateTime acquired_at { get; set; }

        // Navigation property
        [JsonIgnore] public User? User { get; set; }
        public Item? Item { get; set; }
    }
}
