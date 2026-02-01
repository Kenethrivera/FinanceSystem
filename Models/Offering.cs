using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

namespace FinanceSystem.Models
{
    [Table("offerings")]
    public class Offering : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("financial_day_id")]
        public int Financial_Day_Id { get; set; }

        [Column("denomination")]
        public int Denomination { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("total")]
        public int Total { get; set; }

        [Column("notes")]
        public string Notes { get; set; } = string.Empty;
    }
}
