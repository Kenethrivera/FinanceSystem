using FinanceSystem.Models;

namespace FinanceSystem.ViewModels
{
    public class FinancialEntryViewModel
    {
        public Financial_Day Day { get; set; } = new();

        public Budget BudgetEntry { get; set; } = new();

        public List<Expenses> ExpensesList { get; set; } = new List<Expenses>();

        public List<Offering> OfferingList { get; set; } = new List<Offering>();
    }
}
