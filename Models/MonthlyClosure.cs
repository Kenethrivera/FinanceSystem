using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FinanceSystem.Models
{
    [Table("monthly_closures")]
    public class MonthlyClosure : BaseModel
    {
        [PrimaryKey("id", false)]
        public long Id { get; set; }

        [Column("month")]
        public int Month { get; set; }

        [Column("year")]
        public int Year { get; set; }

        [Column("is_locked")]
        public bool IsLocked { get; set; }

        [Column("is_audited")]
        public bool IsAudited { get; set; }

        [Column("locked_by")]
        public string? LockedBy { get; set; }

        [Column("locked_at")]
        public DateTime? LockedAt { get; set; }

        [Column("audit_remarks")]
        public string? AuditRemarks { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}