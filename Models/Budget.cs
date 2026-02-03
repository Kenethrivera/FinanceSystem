using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
namespace FinanceSystem.Models
{
    [Table("budgets")]
    public class Budget : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("financial_day_id")]
        public int Financial_Day_Id { get; set; }

        [Column("source_id")]
        public int Source_Id { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("notes")]
        public string Notes { get; set; } = string.Empty;

    }
}
