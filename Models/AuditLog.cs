using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

namespace FinanceSystem.Models
{
    [Table("audit_logs")]
    public class AuditLog : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("username")]
        public string Username { get; set; }

        [Column("action")]
        public string Action { get; set; }

        [Column("details")]
        public string Details { get; set; }

        [Column("timestamp")]
        public DateTime Timestamp { get; set; }

    }
}
