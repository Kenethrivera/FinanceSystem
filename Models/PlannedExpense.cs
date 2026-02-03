using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("planned_expenses")]
    public class PlannedExpense : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("is_monthly")]
        public bool IsMonthly { get; set; }

        [Column("due_day")]
        public int? DueDay { get; set; }

        [Column("target_date")]
        public DateTime? TargetDate { get; set; }
    }
}