using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
using System;

namespace FinanceSystem.Models
{
    [Table("transactions")]
    public class Transaction : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("sunday_id")]
        public long SundayId { get; set; }

        [Column("category_id")]
        public long CategoryId { get; set; }

        [Column("source_id")]
        public long SourceId { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("notes")]
        public string? Notes { get; set; }
    }
}
