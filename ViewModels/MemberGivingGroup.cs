using FinanceSystem.Models;

public class MemberGivingGroup
{
    public DateTime Date { get; set; }
    public string EventName { get; set; }
    public List<MemberGiving> Givings { get; set; } = new();

    public decimal TotalTithes => Givings.Sum(g => g.Tithes_Amount);
    public decimal TotalOfferings => Givings.Sum(g => g.Offering_Amount);
    public decimal TotalSolomon => Givings.Sum(g => g.Solomon_Amount);
    public decimal TotalGroup => Givings.Sum(g => g.Total_Amount);
}