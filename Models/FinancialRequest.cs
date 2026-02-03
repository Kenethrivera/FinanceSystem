using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("financial_requests")]
    public class FinancialRequest : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("requestor_name")]
        public string RequestorName { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("reason")]
        public string Reason { get; set; }

        [Column("status")]
        public string Status { get; set; } = "Pending";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("date_needed")]
        public DateTime? DateNeeded { get; set; }

        [Column("admin_comment")]
        public string AdminComment { get; set; }
    }
}