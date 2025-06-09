using REST_API.Entity;

namespace REST_API.DTO
{
    public class MessageInvDTO
    {
        public int userid {  get; set; }
        required public Item item { get; set; }
    }
}
