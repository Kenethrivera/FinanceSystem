
using FinanceSystem.Models;
using Supabase.Postgrest.Models;

namespace FinanceSystem.ViewModels
{
    public class MonthlyReportViewModel
    {
        public string MonthName { get; set; } = string.Empty;

        public int Year { get; set; }

        public List<DailyReportItem> Days { get; set; } = new();

        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetCashFlow { get; set; }

        public decimal NetMovement_COH { get; set; }
        public decimal NetMovement_Pledge { get; set; }
    }

    public class DailyReportItem
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public string Notes { get; set; }
        public bool IsFlagged { get; set; }
        public string FlagComment { get; set; }
        public string FlagResponse { get; set; }

        public List<IncomeItem> IncomeList { get; set; } = new();
        public List<ExpenseItem> Expenses { get; set; } = new();

        // New Breakdown Properties
        public decimal TithesTotal { get; set; }
        public decimal SolomonTotal { get; set; }
        public decimal OfferingOnlyTotal { get; set; }

        // Physical Cash
        public decimal OfferingTotal { get; set; }
        public List<OfferingBreakdownItem> OfferingBreakdown { get; set; } = new();

        // Only sum the IncomeList. Do NOT add OfferingTotal.
        public decimal TotalIncomeForDay => IncomeList.Sum(i => i.Amount);

        public decimal TotalExpenseForDay => Expenses.Sum(e => e.Amount);
        public decimal NetChange => TotalIncomeForDay - TotalExpenseForDay;
    }

    public class ExpenseItem
    {
        public string Description { get; set; } 
        public decimal Amount { get; set; }
        public string PaidFrom { get; set; } = string.Empty;
    }
    public class OfferingBreakdownItem
    {
        public decimal Denomination { get; set; }
        public int Quantity { get; set; }
        public decimal Total { get; set; }
        public string Notes { get; set; } 
    }

    public class IncomeItem
    {
        public decimal Amount { get; set; }
        public string SourceName { get; set; }
        public string NoteDetail { get; set; }
    }
}
