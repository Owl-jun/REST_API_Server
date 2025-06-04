namespace REST_API.DTO
{
    public class InventoryDTO
    {
        public int InventoryId { get; set; }
        public int Quantity { get; set; }

        public DateTime AcquiredAt { get; set; }
        public ItemDTO Item { get; set; }
    }
}
