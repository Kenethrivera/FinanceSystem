using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("member_givings")]
    public class MemberGiving : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("financial_day_id")]
        public int Financial_Day_Id { get; set; }

        [Column("member_name")]
        public string Member_Name { get; set; }

        [Column("tithes_amount")]
        public decimal Tithes_Amount { get; set; }

        [Column("offering_amount")]
        public decimal Offering_Amount { get; set; }

        [Column("solomon_amount")]
        public decimal Solomon_Amount { get; set; }

        // This is auto-calculated by Database, but we keep it here for display if needed
        [Column("total_amount", ignoreOnInsert: true)]
        public decimal Total_Amount { get; set; }
    }
}