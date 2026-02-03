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

            // 2. Fetch Data
            var allDays = (await _supabase.Client.From<Financial_Day>().Get()).Models;
            var allB = (await _supabase.Client.From<Budget>().Get()).Models;
            var allE = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allO = (await _supabase.Client.From<Offering>().Get()).Models;
            var allS = (await _supabase.Client.From<Source>().Get()).Models;
            var allGivings = (await _supabase.Client.From<MemberGiving>().Get()).Models;

            // 3. Calculate Beginning Balance
            var pastDayIds = allDays.Where(d => d.Activity_Date < firstDayOfMonth).Select(d => d.Id).ToList();

            decimal pastBudgetIncome = allB.Where(b => pastDayIds.Contains(b.Financial_Day_Id)).Sum(b => b.Amount);
            // Only use Envelopes for past income history to avoid double counting
            decimal pastEnvelopeIncome = allGivings.Where(g => pastDayIds.Contains(g.Financial_Day_Id)).Sum(g => g.Total_Amount);
            decimal pastExpense = allE.Where(e => pastDayIds.Contains(e.Financial_Day_Id)).Sum(e => e.Amount);

            BeginningBalance = (pastBudgetIncome + pastEnvelopeIncome) - pastExpense;

            // 4. Process Current Month
            var currentDayIds = allDays.Where(d => d.Activity_Date.Month == Month && d.Activity_Date.Year == Year).Select(d => d.Id).ToList();
            var currentDays = allDays.Where(d => currentDayIds.Contains(d.Id)).OrderBy(d => d.Activity_Date).ToList();

            Report.NetMovement_COH = 0;
            Report.NetMovement_Pledge = 0;
            decimal TotalSolomonPledges = 0;

            foreach (var day in currentDays)
            {
                var item = new DailyReportItem
                {
                    Id = day.Id,
                    Date = day.Activity_Date,
                    Notes = day.Notes,
                    IsFlagged = !string.IsNullOrEmpty(day.FlagComment) && string.IsNullOrEmpty(day.FlagResponse)
                };

                // --- A. INCOME 1: BUDGETS ---
                var dayBudgets = allB.Where(b => b.Financial_Day_Id == day.Id);
                foreach (var b in dayBudgets)
                {
                    var sName = allS.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "General";
                    item.IncomeList.Add(new IncomeItem
                    {
                        Amount = b.Amount,
                        SourceName = sName,
                        NoteDetail = b.Notes
                    });

                    if (sName.ToLower().Contains("pledge"))
                        Report.NetMovement_Pledge += b.Amount;
                    else
                        Report.NetMovement_COH += b.Amount;
                }

                // --- B. INCOME 2: ENVELOPES (The Accounting Truth) ---
                var dayGivings = allGivings.Where(g => g.Financial_Day_Id == day.Id).ToList();

                item.TithesTotal = dayGivings.Sum(g => g.Tithes_Amount);
                item.OfferingOnlyTotal = dayGivings.Sum(g => g.Offering_Amount);
                item.SolomonTotal = dayGivings.Sum(g => g.Solomon_Amount);

                decimal grandEnvelopeTotal = item.TithesTotal + item.OfferingOnlyTotal + item.SolomonTotal;

                if (grandEnvelopeTotal > 0)
                {
                    // Add to the visual list
                    item.IncomeList.Add(new IncomeItem
                    {
                        Amount = grandEnvelopeTotal,
                        SourceName = "Sunday Envelopes",
                        NoteDetail = $"{dayGivings.Count} givers"
                    });

                    // Add to the Wallet Balances (Logic: Solomon -> Pledge, Rest -> COH)
                    Report.NetMovement_Pledge += item.SolomonTotal;
                    TotalSolomonPledges += item.SolomonTotal;
                    Report.NetMovement_COH += (item.TithesTotal + item.OfferingOnlyTotal);
                }

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

                    if (sourceName.ToLower().Contains("pledge"))
                        Report.NetMovement_Pledge -= exp.Amount;
                    else
                        Report.NetMovement_COH -= exp.Amount;
                }

                // --- D. PHYSICAL CASH (Visual Only - DO NOT ADD TO INCOME) ---
                var dayOfferings = allO.Where(o => o.Financial_Day_Id == day.Id);
                item.OfferingTotal = dayOfferings.Sum(o => o.Total); // Just for the breakdown table footer

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

            ViewData["SolomonTotal"] = TotalSolomonPledges;

            // Recalculate Final Totals based on the corrected IncomeList
            Report.TotalIncome = Report.Days.Sum(d => d.TotalIncomeForDay);
            Report.TotalExpenses = Report.Days.Sum(d => d.TotalExpenseForDay);
            Report.NetCashFlow = Report.TotalIncome - Report.TotalExpenses;
        }


        public async Task<IActionResult> OnGetExportAsync(int month, int year)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Auditor")) return Forbid();

            await _supabase.InitializeAsync(true);

            // Fetch Data
            var allDays = (await _supabase.Client.From<Financial_Day>().Order("activity_date", Supabase.Postgrest.Constants.Ordering.Ascending).Get()).Models;
            var allBudgets = (await _supabase.Client.From<Budget>().Get()).Models;
            var allExpenses = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allOfferings = (await _supabase.Client.From<Offering>().Get()).Models;
            var allGivings = (await _supabase.Client.From<MemberGiving>().Get()).Models;
            var allSources = (await _supabase.Client.From<Source>().Get()).Models;
            var categories = (await _supabase.Client.From<Category>().Get()).Models;

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddSeconds(-1);

            // Calculate Beginning Balance
            var pastDays = allDays.Where(d => d.Activity_Date < startDate).Select(d => d.Id).ToList();
            decimal pastIncome = allBudgets.Where(b => pastDays.Contains(b.Financial_Day_Id)).Sum(b => b.Amount)
                               + allGivings.Where(g => pastDays.Contains(g.Financial_Day_Id)).Sum(g => g.Total_Amount);
            decimal pastExpense = allExpenses.Where(e => pastDays.Contains(e.Financial_Day_Id)).Sum(e => e.Amount);
            decimal beginningBalance = pastIncome - pastExpense;

            var currentDays = allDays.Where(d => d.Activity_Date >= startDate && d.Activity_Date <= endDate).ToList();

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Financial Report");

                // Headers & Summary Box (Unchanged)
                ws.Cell(1, 1).Value = "PINAGPALA FAMILY CENTER - OLIVAREZ";
                ws.Range(1, 1, 1, 4).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Cell(2, 1).Value = $"FINANCIAL REPORT - {startDate:MMMM yyyy}";
                ws.Range(2, 1, 2, 4).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                int sumRow = 4, sumCol = 6;
                ws.Cell(sumRow, sumCol).Value = "CASH FLOW SUMMARY";
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Merge().Style.Font.SetBold().Fill.BackgroundColor = XLColor.DodgerBlue;
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Style.Font.FontColor = XLColor.White;

                sumRow++; ws.Cell(sumRow, sumCol).Value = "Beginning Balance"; ws.Cell(sumRow, sumCol + 1).Value = beginningBalance;
                sumRow++; var incomeCell = ws.Cell(sumRow, sumCol + 1); ws.Cell(sumRow, sumCol).Value = "Total Income (Add)";
                sumRow++; var expenseCell = ws.Cell(sumRow, sumCol + 1); ws.Cell(sumRow, sumCol).Value = "Total Expenses (Less)";
                sumRow++; var endBalCell = ws.Cell(sumRow, sumCol + 1); ws.Cell(sumRow, sumCol).Value = "ENDING BALANCE"; ws.Cell(sumRow, sumCol).Style.Font.SetBold();
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Style.Border.TopBorder = XLBorderStyleValues.Double;

                decimal currentIncome = 0;
                decimal currentExpense = 0;

                // Detailed Table Header
                int row = 8;
                ws.Cell(row, 1).Value = "Date / Description";
                ws.Cell(row, 2).Value = "Category / Source";
                ws.Cell(row, 3).Value = "Income";
                ws.Cell(row, 4).Value = "Expense";
                ws.Range(row, 1, row, 4).Style.Font.SetBold().Fill.BackgroundColor = XLColor.LightGray;
                ws.Range(row, 1, row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;

                foreach (var day in currentDays)
                {
                    // Date Header
                    ws.Cell(row, 1).Value = day.Activity_Date.ToString("MMMM dd, yyyy dddd").ToUpper();
                    ws.Range(row, 1, row, 4).Merge().Style.Font.SetBold().Fill.BackgroundColor = XLColor.FromHtml("#E0F7FA");
                    row++;

                    // 1. BUDGETS (First)
                    var dayBudgets = allBudgets.Where(b => b.Financial_Day_Id == day.Id);
                    foreach (var b in dayBudgets)
                    {
                        var src = allSources.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "Income";
                        ws.Cell(row, 1).Value = $"   {b.Notes}";
                        ws.Cell(row, 2).Value = src;
                        ws.Cell(row, 3).Value = b.Amount;
                        currentIncome += b.Amount;
                        row++;
                    }

                    // 2. EXPENSES (Second)
                    var dayExpenses = allExpenses.Where(e => e.Financial_Day_Id == day.Id);
                    foreach (var e in dayExpenses)
                    {
                        var cat = categories.FirstOrDefault(c => c.Id == e.Category_Id)?.Name ?? "Expense";
                        ws.Cell(row, 1).Value = $"   {e.Notes}";
                        ws.Cell(row, 2).Value = cat;
                        ws.Cell(row, 4).Value = e.Amount;
                        currentExpense += e.Amount;
                        row++;
                    }

                    // 3. TITHES & OFFERINGS (Third)
                    var dayGivings = allGivings.Where(g => g.Financial_Day_Id == day.Id).ToList();
                    var dayOfferings = allOfferings.Where(o => o.Financial_Day_Id == day.Id).ToList();

                    if (dayGivings.Any())
                    {
                        decimal dayTotal = dayGivings.Sum(g => g.Total_Amount);
                        ws.Cell(row, 1).Value = "   Sunday Collection (Envelopes)";
                        ws.Cell(row, 1).Style.Font.SetBold();
                        ws.Cell(row, 2).Value = "Member Giving";
                        ws.Cell(row, 3).Value = dayTotal;
                        ws.Cell(row, 3).Style.Font.SetBold();
                        currentIncome += dayTotal;
                        row++;

                        // Denomination Breakdown (Visuals)
                        foreach (var o in dayOfferings.OrderByDescending(x => x.Denomination))
                        {
                            if (o.Quantity > 0)
                            {
                                ws.Cell(row, 1).Value = $"      - {o.Denomination:N0} x {o.Quantity} pcs {o.Notes}";
                                ws.Cell(row, 1).Style.Font.SetItalic().Font.FontColor = XLColor.DimGray;
                                ws.Cell(row, 3).Value = o.Total;
                                ws.Cell(row, 3).Style.Font.FontColor = XLColor.DimGray;
                                row++;
                            }
                        }
                    }
                    row++; // Spacer
                }

                // Finalize Summary
                incomeCell.Value = currentIncome;
                expenseCell.Value = currentExpense;
                endBalCell.Value = beginningBalance + currentIncome - currentExpense;

                ws.Columns(3, 4).Style.NumberFormat.Format = "₱#,##0.00";
                ws.Columns(sumCol + 1, sumCol + 1).Style.NumberFormat.Format = "₱#,##0.00";
                ws.Column(1).Width = 50; ws.Column(2).Width = 25;
                ws.Columns(3, 4).AdjustToContents(); ws.Columns(sumCol, sumCol + 1).AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PFC_Financial_{startDate:MMM_yyyy}.xlsx");
                }
            }
        }
    }
}