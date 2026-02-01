using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
namespace FinanceSystem.Models
{
    [Table("sources")]
    public class Source : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;
        [Column("description")]
        public string Description { get; set; } = string.Empty;
    }
}
