using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
namespace FinanceSystem.Models
{
    [Table("categories")]
    public class Category : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("type")]
        public string Type { get; set; } = string.Empty;
    }
}
