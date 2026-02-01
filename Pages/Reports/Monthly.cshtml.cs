using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using ClosedXML.Excel;

namespace FinanceSystem.Pages.Reports
{
    public class MonthlyModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public MonthlyModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public MonthlyReportViewModel Report { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Year { get; set; }

        public decimal BeginningBalance { get; set; }
        
        [BindProperty(SupportsGet = true)]
        public int SelectedMonth { get; set; }

        [BindProperty(SupportsGet = true)]
        public int SelectedYear { get; set; }

        public async Task OnGetAsync()
        {
            // 1. Setup Dates
            if (Month == 0) Month = DateTime.Now.Month;
            if (Year == 0) Year = DateTime.Now.Year;

            SelectedMonth = Month;
            SelectedYear = Year;

            await _supabase.InitializeAsync(true);

            Report.MonthName = new DateTime(Year, Month, 1).ToString("MMMM");
            Report.Year = Year;
            var firstDayOfMonth = new DateTime(Year, Month, 1);



            // 2. Fetch ALL Data (We fetch everything to ensure easy linking)
            // In a small-to-medium church app, this is perfectly fine and fast.
            var allDays = (await _supabase.Client.From<Financial_Day>().Get()).Models;
            var allB = (await _supabase.Client.From<Budget>().Get()).Models;
            var allE = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allO = (await _supabase.Client.From<Offering>().Get()).Models;
            var allS = (await _supabase.Client.From<Source>().Get()).Models;

            // 3. Calculate Beginning Balance (Everything BEFORE this month)
            var pastDayIds = allDays.Where(d => d.Activity_Date < firstDayOfMonth).Select(d => d.Id).ToList();

            decimal pastIncome = allB.Where(b => pastDayIds.Contains(b.Financial_Day_Id)).Sum(b => b.Amount)
                               + allO.Where(o => pastDayIds.Contains(o.Financial_Day_Id)).Sum(o => o.Total);

            decimal pastExpense = allE.Where(e => pastDayIds.Contains(e.Financial_Day_Id)).Sum(e => e.Amount);

            BeginningBalance = pastIncome - pastExpense;


            // 4. Process CURRENT Month Data
            var currentDayIds = allDays.Where(d => d.Activity_Date.Month == Month && d.Activity_Date.Year == Year).Select(d => d.Id).ToList();
            var currentDays = allDays.Where(d => currentDayIds.Contains(d.Id)).OrderBy(d => d.Activity_Date).ToList();

            Report.NetMovement_COH = 0;
            Report.NetMovement_Pledge = 0;

            foreach (var day in currentDays)
            {
                var item = new DailyReportItem
                {
                    Id = day.Id,
                    Date = day.Activity_Date,
                    Notes = day.Notes,
                    IsFlagged = !string.IsNullOrEmpty(day.FlagComment) && string.IsNullOrEmpty(day.FlagResponse)
                };

                // --- A. INCOME (Budgets/Support) ---
                var dayBudgets = allB.Where(b => b.Financial_Day_Id == day.Id);

                foreach (var b in dayBudgets)
                {
                    var sName = allS.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "General";

                    // 1. Add to the Display List (New Feature)
                    item.IncomeList.Add(new IncomeItem
                    {
                        Amount = b.Amount,
                        SourceName = sName,
                        NoteDetail = b.Notes
                    });

                    // 2. Add to Footer Splits (Calculated HERE only)
                    if (sName.ToLower().Contains("pledge"))
                        Report.NetMovement_Pledge += b.Amount;
                    else
                        Report.NetMovement_COH += b.Amount;
                }

                // --- B. OFFERINGS ---
                var dayOfferings = allO.Where(o => o.Financial_Day_Id == day.Id);
                item.OfferingTotal = dayOfferings.Sum(o => o.Total);

                // Offerings always go to Cash On Hand
                Report.NetMovement_COH += item.OfferingTotal;

                // --- C. EXPENSES ---
                var dayExpenses = allE.Where(e => e.Financial_Day_Id == day.Id);
                foreach (var exp in dayExpenses)
                {
                    var sourceName = allS.FirstOrDefault(s => s.Id == exp.Source_Id)?.Name ?? "Cash On Hand";

                    item.Expenses.Add(new ExpenseItem
                    {
                        Description = !string.IsNullOrEmpty(exp.Notes) ? exp.Notes : "Expense",
                        Amount = exp.Amount,
                        PaidFrom = sourceName
                    });

                    // Deduct from the correct "Wallet"
                    if (sourceName.ToLower().Contains("pledge"))
                        Report.NetMovement_Pledge -= exp.Amount;
                    else
                        Report.NetMovement_COH -= exp.Amount;
                }

                // --- D. OFFERING BREAKDOWN (The Table) ---
                foreach (var off in dayOfferings.OrderByDescending(o => o.Denomination))
                {
                    if (off.Quantity > 0)
                    {
                        item.OfferingBreakdown.Add(new OfferingBreakdownItem
                        {
                            Denomination = off.Denomination,
                            Quantity = off.Quantity,
                            Total = off.Total,
                            Notes = off.Notes
                        });
                    }
                }

                Report.Days.Add(item);
            }

            // 5. Final Totals
            Report.TotalIncome = Report.Days.Sum(d => d.TotalIncomeForDay);
            Report.TotalExpenses = Report.Days.Sum(d => d.TotalExpenseForDay);
            Report.NetCashFlow = Report.TotalIncome - Report.TotalExpenses;
        }

        public async Task<IActionResult> OnGetExportAsync(int month, int year)
        {
            // 1. Security Check
            if (!User.IsInRole("Admin") && !User.IsInRole("Auditor")) return Forbid();

            await _supabase.InitializeAsync(true);

            // 2. Fetch Data
            var allDays = (await _supabase.Client.From<Financial_Day>()
                .Order("activity_date", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get()).Models;

            var allBudgets = (await _supabase.Client.From<Budget>().Get()).Models;
            var allExpenses = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allOfferings = (await _supabase.Client.From<Offering>().Get()).Models;
            var allSources = (await _supabase.Client.From<Source>().Get()).Models;
            var categories = (await _supabase.Client.From<Category>().Get()).Models;

            // 3. Filter Data
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddSeconds(-1);

            // Calculate Beginning Balance
            var pastDays = allDays.Where(d => d.Activity_Date < startDate).ToList();
            var pastDayIds = pastDays.Select(d => d.Id).ToList();

            decimal pastIncome = allBudgets.Where(b => pastDayIds.Contains(b.Financial_Day_Id)).Sum(b => b.Amount)
                               + allOfferings.Where(o => pastDayIds.Contains(o.Financial_Day_Id)).Sum(o => o.Total);

            decimal pastExpense = allExpenses.Where(e => pastDayIds.Contains(e.Financial_Day_Id)).Sum(e => e.Amount);

            decimal beginningBalance = pastIncome - pastExpense;

            var currentDays = allDays.Where(d => d.Activity_Date >= startDate && d.Activity_Date <= endDate).ToList();

            // 4. Build Excel
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Financial Report");

                // --- TITLE ---
                // Merged 1-4 now (since we removed Ref column)
                ws.Cell(1, 1).Value = "PINAGPALA FAMILY CENTER - OLIVAREZ";
                ws.Range(1, 1, 1, 4).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                ws.Cell(2, 1).Value = $"FINANCIAL REPORT - {startDate:MMMM yyyy}";
                ws.Range(2, 1, 2, 4).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // --- SUMMARY BOX (Top Right - Moved to Col 6/F to give space) ---
                int sumRow = 4;
                int sumCol = 6; // Column F

                ws.Cell(sumRow, sumCol).Value = "CASH FLOW SUMMARY";
                var summaryHeader = ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Merge();
                summaryHeader.Style.Font.SetBold();
                summaryHeader.Style.Fill.BackgroundColor = XLColor.DodgerBlue;
                summaryHeader.Style.Font.FontColor = XLColor.White;

                sumRow++;
                ws.Cell(sumRow, sumCol).Value = "Beginning Balance";
                ws.Cell(sumRow, sumCol + 1).Value = beginningBalance;
                ws.Cell(sumRow, sumCol + 1).Style.Font.SetItalic();

                sumRow++;
                decimal currentIncome = 0;
                decimal currentExpense = 0;

                var incomeCell = ws.Cell(sumRow, sumCol + 1);
                ws.Cell(sumRow, sumCol).Value = "Total Income (Add)";

                sumRow++;
                var expenseCell = ws.Cell(sumRow, sumCol + 1);
                ws.Cell(sumRow, sumCol).Value = "Total Expenses (Less)";

                sumRow++;
                var endBalCell = ws.Cell(sumRow, sumCol + 1);
                ws.Cell(sumRow, sumCol).Value = "ENDING BALANCE";
                ws.Cell(sumRow, sumCol).Style.Font.SetBold();
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Style.Border.TopBorder = XLBorderStyleValues.Double;

                // --- DETAILED TRANSACTIONS ---
                int row = 8;
                ws.Cell(row, 1).Value = "Date / Description";
                ws.Cell(row, 2).Value = "Category / Source";
                // Removed Ref# Column Here
                ws.Cell(row, 3).Value = "Income";
                ws.Cell(row, 4).Value = "Expense";

                // Style Table Header
                var tableHeader = ws.Range(row, 1, row, 4);
                tableHeader.Style.Font.SetBold();
                tableHeader.Style.Fill.BackgroundColor = XLColor.LightGray;
                tableHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;

                foreach (var day in currentDays)
                {
                    // 1. DATE HEADER
                    ws.Cell(row, 1).Value = day.Activity_Date.ToString("MMMM dd, yyyy dddd").ToUpper();
                    var dateRow = ws.Range(row, 1, row, 4).Merge(); // Merge 4 cols
                    dateRow.Style.Font.SetBold();
                    dateRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F7FA"); // Light Cyan
                    row++;

                    // 2. OFFERINGS
                    var dayOfferings = allOfferings.Where(o => o.Financial_Day_Id == day.Id).ToList();
                    if (dayOfferings.Any())
                    {
                        // Main Total
                        decimal dayTotal = dayOfferings.Sum(o => o.Total);
                        ws.Cell(row, 1).Value = "   Sunday Collection (Total)";
                        ws.Cell(row, 1).Style.Font.SetBold();
                        ws.Cell(row, 2).Value = "Tithes & Offering";
                        // Col 3 is now Income
                        ws.Cell(row, 3).Value = dayTotal;
                        ws.Cell(row, 3).Style.Font.SetBold();
                        currentIncome += dayTotal;
                        row++;

                        // Breakdown
                        foreach (var o in dayOfferings.OrderByDescending(x => x.Denomination))
                        {
                            if (o.Quantity > 0)
                            {
                                ws.Cell(row, 1).Value = $"      - {o.Denomination:N0} x {o.Quantity} pcs";
                                ws.Cell(row, 1).Style.Font.SetItalic();
                                ws.Cell(row, 1).Style.Font.FontColor = XLColor.DimGray;

                                // Show breakdown amount in grey
                                ws.Cell(row, 3).Value = o.Total;
                                ws.Cell(row, 3).Style.Font.FontColor = XLColor.DimGray;
                                row++;
                            }
                        }
                    }

                    // 3. BUDGETS
                    var dayBudgets = allBudgets.Where(b => b.Financial_Day_Id == day.Id);
                    foreach (var b in dayBudgets)
                    {
                        var src = allSources.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "Income";
                        ws.Cell(row, 1).Value = $"   {b.Notes}";
                        ws.Cell(row, 2).Value = src;
                        ws.Cell(row, 3).Value = b.Amount; // Income is Col 3
                        currentIncome += b.Amount;
                        row++;
                    }

                    // 4. EXPENSES
                    var dayExpenses = allExpenses.Where(e => e.Financial_Day_Id == day.Id);
                    foreach (var e in dayExpenses)
                    {
                        var cat = categories.FirstOrDefault(c => c.Id == e.Category_Id)?.Name ?? "Expense";
                        ws.Cell(row, 1).Value = $"   {e.Notes}";
                        ws.Cell(row, 2).Value = cat;
                        ws.Cell(row, 4).Value = e.Amount; // Expense is Col 4
                        currentExpense += e.Amount;
                        row++;
                    }

                    // --- SPACER ROW (Requested in Video) ---
                    row++;
                }

                // --- UPDATE SUMMARY VALUES ---
                incomeCell.Value = currentIncome;
                incomeCell.Style.Font.FontColor = XLColor.Green;
                expenseCell.Value = currentExpense;
                expenseCell.Style.Font.FontColor = XLColor.Red;

                decimal endingBalance = beginningBalance + currentIncome - currentExpense;
                endBalCell.Value = endingBalance;
                endBalCell.Style.Font.SetBold();

                // --- FINAL FORMATTING ---
                // Currency Format for Columns 3 (Income), 4 (Expense) and Summary (Col 7/G)
                ws.Columns(3, 4).Style.NumberFormat.Format = "₱#,##0.00";
                ws.Columns(sumCol + 1, sumCol + 1).Style.NumberFormat.Format = "₱#,##0.00";

                // Widths
                ws.Column(1).Width = 50; // Description wider
                ws.Column(2).Width = 25;
                ws.Columns(3, 4).AdjustToContents();
                ws.Columns(sumCol, sumCol + 1).AdjustToContents();

                // 5. Return File
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PFC_Oliv_Financial_Report_{startDate:yyyy_MM}.xlsx");
                }
            }
        }




    }
}
