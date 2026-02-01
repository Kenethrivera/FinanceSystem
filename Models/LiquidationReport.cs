using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("liquidation_reports")]
    public class LiquidationReport : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("financial_day_id")]
        public long Financial_Day_Id { get; set; }

        [Column("image_path")]
        public string ImagePath { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}