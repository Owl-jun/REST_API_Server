namespace REST_API.Entity
{
    public class Item
    {
        public int Item_Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public string Rarity { get; set; } = "";

        public List<Inventory> Inventory { get; set; } = new();
    }
}
