using System.Text.Json.Serialization;

namespace REST_API.Entity
{
    public class User
    {
        [JsonIgnore] public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Phone { get; set; } = "";
        [JsonIgnore] public DateTime Created_At { get; set; } = DateTime.Now;
        [JsonIgnore] public double posx { get; set; }
        [JsonIgnore] public double posy { get; set; }
        [JsonIgnore] public List<Inventory> Inventory { get; set; } = new();
    }
}
