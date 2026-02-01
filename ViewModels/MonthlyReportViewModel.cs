
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
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Notes { get; set; } = string.Empty;
        public bool IsFlagged { get; set; }
        public List<IncomeItem> IncomeList { get; set; } = new();
        public decimal OfferingTotal { get; set; }
        public List<ExpenseItem> Expenses { get; set; } = new();
        public List<OfferingBreakdownItem> OfferingBreakdown { get; set; } = new();

        public decimal TotalIncomeForDay => IncomeList.Sum(i => i.Amount) + OfferingTotal;
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
