using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("users")]
    public class User : BaseModel
    {
        [PrimaryKey("id")]
        public int Id { get; set; }

        [Column("username")]
        public string Username { get; set; }

        [Column("password")]
        public string Password { get; set; }

        [Column("role")]
        public string Role { get; set; }

        [Column("full_name")]
        public string Full_Name { get; set; }


    }
}
