namespace FinanceSystem.Models
{
    public class EnvelopeScanResult
    {
        public string RawText { get; set; } = "";
        public string Name { get; set; } = "";
        public string DateText { get; set; } = "";
        public decimal Tithes { get; set; }
        public decimal Offering { get; set; }
        public decimal Solomon { get; set; }

        public decimal Total => Tithes + Offering + Solomon;
        public bool HasAnyAmount => Tithes > 0 || Offering > 0 || Solomon > 0;
    }
}
