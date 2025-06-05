using REST_API.DBModel;

namespace REST_API.DTO
{
    public class MessageInvDTO
    {
        public int userid {  get; set; }
        required public Item item { get; set; }
    }
}
