using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
namespace FinanceSystem.Models
{
    [Table("financial_days")]
    public class Financial_Day :BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("activity_date")]
        public DateTime Activity_Date { get; set; }

        [Column("is_sunday")]
        public bool Is_Sunday { get; set; }

        [Column("notes")]
        public string Notes { get; set; } = string.Empty;

        [Column("owner_id")]
        public Guid Owner_Id { get; set; }

        [Column("flag_comment")]
        public string? FlagComment { get; set; }

        [Column("flag_response")]
        public string? FlagResponse { get; set; }
    }
}
