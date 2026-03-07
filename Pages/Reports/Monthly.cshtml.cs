using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using ClosedXML.Excel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FinanceSystem.Pages.Reports
{
    public class MonthlyModel : PageModel
    {
        private readonly SupabaseService _supabase;
        private readonly MonthLockService _monthLockService;
        private const string DownloadSecret = "PFC_Secure_Export_Key_99283"; // Secure key for token generation

        public MonthlyModel(SupabaseService supabase, MonthLockService monthLockService)
        {
            _supabase = supabase;
            _monthLockService = monthLockService;
        }

        public MonthlyReportViewModel Report { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Year { get; set; }

        [BindProperty(SupportsGet = true)]
        public int SelectedMonth { get; set; }

        [BindProperty(SupportsGet = true)]
        public int SelectedYear { get; set; }

        public decimal BeginningBalance { get; set; }

        public string DownloadToken { get; set; }
        public bool IsLockedMonth { get; set; }
        public MonthlyClosure? MonthClosure { get; set; }

        public async Task OnGetAsync()
        {
            if (Month == 0) Month = DateTime.Now.Month;
            if (Year == 0) Year = DateTime.Now.Year;

            SelectedMonth = Month;
            SelectedYear = Year;

            DownloadToken = GenerateSecureToken(Month, Year);

            MonthClosure = await _monthLockService.GetMonthClosureAsync(Month, Year);
            IsLockedMonth = MonthClosure != null && MonthClosure.IsLocked;

            await _supabase.InitializeAsync(true);

            Report.MonthName = new DateTime(Year, Month, 1).ToString("MMMM");
            Report.Year = Year;

            var firstDayOfMonth = new DateTime(Year, Month, 1);

            // Fetch Data
            var allDays = (await _supabase.Client.From<Financial_Day>().Get()).Models;
            var allB = (await _supabase.Client.From<Budget>().Get()).Models;
            var allE = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allO = (await _supabase.Client.From<Offering>().Get()).Models;
            var allS = (await _supabase.Client.From<Source>().Get()).Models;
            var allGivings = (await _supabase.Client.From<MemberGiving>().Get()).Models;

            // Helpers
            bool IsPledgeSource(string name) =>
                !string.IsNullOrEmpty(name) && name.ToLower().Contains("solomon");

            bool IsPledgeExpense(Expenses e)
            {
                if (e.Source_Id.HasValue)
                {
                    var srcName = allS.FirstOrDefault(s => s.Id == e.Source_Id)?.Name;
                    return IsPledgeSource(srcName);
                }

                if (!string.IsNullOrEmpty(e.Notes))
                {
                    var n = e.Notes.ToLower();
                    if (n.Contains("pledge") || n.Contains("solomon"))
                        return true;
                }

                return false;
            }

            var pledgeSourceIds = allS
                .Where(s => IsPledgeSource(s.Name))
                .Select(s => s.Id)
                .ToList();

            var pastDayIds = allDays
                .Where(d => d.Activity_Date < firstDayOfMonth)
                .Select(d => d.Id)
                .ToList();

            // ==========================================================
            // 1. HISTORICAL BALANCE COMPUTATION
            // ==========================================================

            // CASH ON HAND HISTORY
            decimal pastCashIncome = allB
                .Where(b => pastDayIds.Contains(b.Financial_Day_Id) && !pledgeSourceIds.Contains(b.Source_Id))
                .Sum(b => b.Amount);

            // FIX: Only Tithes + Offering go to COH
            decimal pastEnvelopeIncome = allGivings
                .Where(g => pastDayIds.Contains(g.Financial_Day_Id))
                .Sum(g => g.Tithes_Amount + g.Offering_Amount);

            decimal pastCashExpense = allE
                .Where(e => pastDayIds.Contains(e.Financial_Day_Id) && !IsPledgeExpense(e))
                .Sum(e => e.Amount);

            BeginningBalance = (pastCashIncome + pastEnvelopeIncome) - pastCashExpense;

            // SOLOMON / PLEDGE HISTORY
            decimal pastPledgeIncome = allB
                .Where(b => pastDayIds.Contains(b.Financial_Day_Id) && pledgeSourceIds.Contains(b.Source_Id))
                .Sum(b => b.Amount);

            decimal pastSolomonEnvelope = allGivings
                .Where(g => pastDayIds.Contains(g.Financial_Day_Id))
                .Sum(g => g.Solomon_Amount);

            decimal pastPledgeExpense = allE
                .Where(e => pastDayIds.Contains(e.Financial_Day_Id) && IsPledgeExpense(e))
                .Sum(e => e.Amount);

            decimal pledgeBeginningBalance = (pastPledgeIncome + pastSolomonEnvelope) - pastPledgeExpense;

            // ==========================================================
            // 2. CURRENT MONTH PROCESSING
            // ==========================================================
            var currentDays = allDays
                .Where(d => d.Activity_Date.Month == Month && d.Activity_Date.Year == Year)
                .OrderBy(d => d.Activity_Date)
                .ToList();

            Report.NetMovement_COH = 0;
            Report.NetMovement_Pledge = 0;
            Report.Days.Clear();

            foreach (var day in currentDays)
            {
                var item = new DailyReportItem
                {
                    Id = day.Id,
                    Date = day.Activity_Date,
                    Notes = day.Notes,
                    IsFlagged = !string.IsNullOrEmpty(day.FlagComment) && string.IsNullOrEmpty(day.FlagResponse)
                };

                // --------------------------
                // A. BUDGETS
                // --------------------------
                var dayBudgets = allB.Where(b => b.Financial_Day_Id == day.Id).ToList();

                foreach (var b in dayBudgets)
                {
                    var sName = allS.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "General";

                    if (IsPledgeSource(sName))
                    {
                        Report.NetMovement_Pledge += b.Amount;
                    }
                    else
                    {
                        item.IncomeList.Add(new IncomeItem
                        {
                            Amount = b.Amount,
                            SourceName = sName,
                            NoteDetail = b.Notes
                        });

                        Report.NetMovement_COH += b.Amount;
                    }
                }

                // --------------------------
                // B. MEMBER GIVINGS / ENVELOPES
                // --------------------------
                var dayGivings = allGivings
                    .Where(g => g.Financial_Day_Id == day.Id)
                    .ToList();

                item.TithesTotal = dayGivings.Sum(g => g.Tithes_Amount);
                item.OfferingOnlyTotal = dayGivings.Sum(g => g.Offering_Amount);
                item.SolomonTotal = dayGivings.Sum(g => g.Solomon_Amount);

                // FIX: Only COH envelope parts go into IncomeList and COH movement
                decimal cashEnvelopeTotal = item.TithesTotal + item.OfferingOnlyTotal;

                if (cashEnvelopeTotal > 0)
                {
                    item.IncomeList.Add(new IncomeItem
                    {
                        Amount = cashEnvelopeTotal,
                        SourceName = "Sunday Envelopes",
                        NoteDetail = $"{dayGivings.Count} givers"
                    });
                }

                Report.NetMovement_COH += cashEnvelopeTotal;
                Report.NetMovement_Pledge += item.SolomonTotal;

                // --------------------------
                // C. EXPENSES
                // --------------------------
                var dayExpenses = allE.Where(e => e.Financial_Day_Id == day.Id).ToList();

                foreach (var exp in dayExpenses)
                {
                    if (IsPledgeExpense(exp))
                    {
                        Report.NetMovement_Pledge -= exp.Amount;
                    }
                    else
                    {
                        item.Expenses.Add(new ExpenseItem
                        {
                            Description = !string.IsNullOrEmpty(exp.Notes) ? exp.Notes : "Expense",
                            Amount = exp.Amount,
                            PaidFrom = "Cash On Hand"
                        });

                        Report.NetMovement_COH -= exp.Amount;
                    }
                }

                // --------------------------
                // D. OFFERING BREAKDOWN (visual only)
                // --------------------------
                var dayOfferings = allO.Where(o => o.Financial_Day_Id == day.Id).ToList();
                item.OfferingTotal = dayOfferings.Sum(o => o.Total);

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

            Report.TotalIncome = Report.Days.Sum(d => d.TotalIncomeForDay);
            Report.TotalExpenses = Report.Days.Sum(d => d.TotalExpenseForDay);
            Report.NetCashFlow = Report.TotalIncome - Report.TotalExpenses;

            // ViewData for pledge dashboard
            ViewData["SolomonTotal"] = pledgeBeginningBalance + Report.NetMovement_Pledge;
        }

        public async Task<IActionResult> OnPostLockMonthAsync(int month, int year, string? remarks)
        {
            if (!(User.IsInRole("Auditor") || User.IsInRole("Admin")))
                return Forbid();

            await _monthLockService.LockMonthAsync(month, year, User.Identity?.Name ?? "Unknown", remarks);
            await _supabase.LogActvity(User.Identity?.Name, "UPDATE", $"Locked month {month}/{year} as Final and Audited.");

            return RedirectToPage(new { Month = month, Year = year });
        }

        public async Task<IActionResult> OnPostUnlockMonthAsync(int month, int year)
        {
            if (!(User.IsInRole("Auditor") || User.IsInRole("Admin")))
                return Forbid();

            await _monthLockService.UnlockMonthAsync(month, year);
            await _supabase.LogActvity(User.Identity?.Name, "UPDATE", $"Reopened month {month}/{year}.");

            return RedirectToPage(new { Month = month, Year = year });
        }

        // ==========================================================
        // EXPORT FUNCTION
        // ==========================================================
        public async Task<IActionResult> OnGetExportAsync(int month, int year, string token = null)
        {
            bool isAuthorized = (User.IsInRole("Admin") || User.IsInRole("Auditor"));
            bool isTokenValid = ValidateSecureToken(token, month, year);

            if (!isAuthorized && !isTokenValid)
                return Forbid();

            await _supabase.InitializeAsync(true);

            var allDays = (await _supabase.Client.From<Financial_Day>()
                .Order("activity_date", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get()).Models;

            var allBudgets = (await _supabase.Client.From<Budget>().Get()).Models;
            var allExpenses = (await _supabase.Client.From<Expenses>().Get()).Models;
            var allOfferings = (await _supabase.Client.From<Offering>().Get()).Models;
            var allGivings = (await _supabase.Client.From<MemberGiving>().Get()).Models;
            var allSources = (await _supabase.Client.From<Source>().Get()).Models;
            var categories = (await _supabase.Client.From<Category>().Get()).Models;

            bool IsPledgeSource(string name) =>
                !string.IsNullOrEmpty(name) && name.ToLower().Contains("solomon");

            bool IsPledgeExpense(Expenses e)
            {
                if (e.Source_Id.HasValue)
                {
                    var srcName = allSources.FirstOrDefault(s => s.Id == e.Source_Id)?.Name;
                    return IsPledgeSource(srcName);
                }

                if (!string.IsNullOrEmpty(e.Notes))
                {
                    var n = e.Notes.ToLower();
                    if (n.Contains("pledge") || n.Contains("solomon"))
                        return true;
                }

                return false;
            }

            var pledgeSourceIds = allSources
                .Where(s => IsPledgeSource(s.Name))
                .Select(s => s.Id)
                .ToList();

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddSeconds(-1);

            var pastDays = allDays
                .Where(d => d.Activity_Date < startDate)
                .Select(d => d.Id)
                .ToList();

            // FIX: beginning balance for export should only use COH
            decimal pastIncome = allBudgets
                .Where(b => pastDays.Contains(b.Financial_Day_Id) && !pledgeSourceIds.Contains(b.Source_Id))
                .Sum(b => b.Amount)
                + allGivings
                    .Where(g => pastDays.Contains(g.Financial_Day_Id))
                    .Sum(g => g.Tithes_Amount + g.Offering_Amount);

            decimal pastExpense = allExpenses
                .Where(e => pastDays.Contains(e.Financial_Day_Id) && !IsPledgeExpense(e))
                .Sum(e => e.Amount);

            decimal beginningBalance = pastIncome - pastExpense;

            var currentDays = allDays
                .Where(d => d.Activity_Date >= startDate && d.Activity_Date <= endDate)
                .ToList();

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Financial Report");

                ws.Cell(1, 1).Value = "PINAGPALA FAMILY CENTER - OLIVAREZ";
                ws.Range(1, 1, 1, 4).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                ws.Cell(2, 1).Value = $"FINANCIAL REPORT - {startDate:MMMM yyyy}";
                ws.Range(2, 1, 2, 4).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                int sumRow = 4, sumCol = 6;
                ws.Cell(sumRow, sumCol).Value = "CASH FLOW SUMMARY";
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Merge().Style.Font.SetBold().Fill.BackgroundColor = XLColor.DodgerBlue;
                ws.Range(sumRow, sumCol, sumRow, sumCol + 1).Style.Font.FontColor = XLColor.White;

                var begBalCell = ws.Cell(sumRow + 1, sumCol + 1);
                ws.Cell(sumRow + 1, sumCol).Value = "Beginning Balance";

                var incomeCell = ws.Cell(sumRow + 2, sumCol + 1);
                ws.Cell(sumRow + 2, sumCol).Value = "Total Income (Add)";

                var expenseCell = ws.Cell(sumRow + 3, sumCol + 1);
                ws.Cell(sumRow + 3, sumCol).Value = "Total Expenses (Less)";

                var endBalCell = ws.Cell(sumRow + 4, sumCol + 1);
                ws.Cell(sumRow + 4, sumCol).Value = "ENDING BALANCE";
                ws.Cell(sumRow + 4, sumCol).Style.Font.SetBold();
                ws.Range(sumRow + 4, sumCol, sumRow + 4, sumCol + 1).Style.Border.TopBorder = XLBorderStyleValues.Double;

                decimal currentIncome = 0;
                decimal currentExpense = 0;

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
                    bool dayHeaderPrinted = false;

                    // 1. BUDGETS
                    var dayBudgets = allBudgets.Where(b => b.Financial_Day_Id == day.Id).ToList();
                    foreach (var b in dayBudgets)
                    {
                        var src = allSources.FirstOrDefault(s => s.Id == b.Source_Id)?.Name ?? "Income";

                        if (!IsPledgeSource(src))
                        {
                            if (!dayHeaderPrinted)
                            {
                                PrintDateHeader(ws, ref row, day);
                                dayHeaderPrinted = true;
                            }

                            ws.Cell(row, 1).Value = $"   {b.Notes}";
                            ws.Cell(row, 2).Value = src;
                            ws.Cell(row, 3).Value = b.Amount;
                            currentIncome += b.Amount;
                            row++;
                        }
                    }

                    // 2. EXPENSES
                    var dayExpenses = allExpenses.Where(e => e.Financial_Day_Id == day.Id).ToList();
                    foreach (var e in dayExpenses)
                    {
                        if (!IsPledgeExpense(e))
                        {
                            if (!dayHeaderPrinted)
                            {
                                PrintDateHeader(ws, ref row, day);
                                dayHeaderPrinted = true;
                            }

                            var cat = categories.FirstOrDefault(c => c.Id == e.Category_Id)?.Name ?? "Expense";
                            ws.Cell(row, 1).Value = $"   {e.Notes}";
                            ws.Cell(row, 2).Value = cat;
                            ws.Cell(row, 4).Value = e.Amount;
                            currentExpense += e.Amount;
                            row++;
                        }
                    }

                    // 3. ENVELOPES
                    var dayGivings = allGivings.Where(g => g.Financial_Day_Id == day.Id).ToList();
                    var dayOfferings = allOfferings.Where(o => o.Financial_Day_Id == day.Id).ToList();

                    // FIX: export only COH part, not Solomon
                    decimal dayEnvelopeCash = dayGivings.Sum(g => g.Tithes_Amount + g.Offering_Amount);

                    if (dayEnvelopeCash > 0)
                    {
                        if (!dayHeaderPrinted)
                        {
                            PrintDateHeader(ws, ref row, day);
                            dayHeaderPrinted = true;
                        }

                        ws.Cell(row, 1).Value = "   Sunday Collection (Envelopes)";
                        ws.Cell(row, 1).Style.Font.SetBold();
                        ws.Cell(row, 2).Value = "Member Giving";
                        ws.Cell(row, 3).Value = dayEnvelopeCash;
                        ws.Cell(row, 3).Style.Font.SetBold();
                        currentIncome += dayEnvelopeCash;
                        row++;

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

                    if (dayHeaderPrinted)
                        row++;
                }

                begBalCell.Value = beginningBalance;
                incomeCell.Value = currentIncome;
                expenseCell.Value = currentExpense;
                endBalCell.Value = beginningBalance + currentIncome - currentExpense;

                ws.Columns(3, 4).Style.NumberFormat.Format = "₱#,##0.00";
                ws.Columns(sumCol + 1, sumCol + 1).Style.NumberFormat.Format = "₱#,##0.00";
                ws.Column(1).Width = 50;
                ws.Column(2).Width = 25;
                ws.Columns(3, 4).AdjustToContents();
                ws.Columns(sumCol, sumCol + 1).AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    stream.Position = 0;

                    return File(
                        stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"PFC_Olivarez_Financial_{startDate:MMM_yyyy}.xlsx"
                    );
                }
            }
        }

        private void PrintDateHeader(IXLWorksheet ws, ref int row, Financial_Day day)
        {
            ws.Cell(row, 1).Value = day.Activity_Date.ToString("MMMM dd, yyyy dddd").ToUpper();
            ws.Range(row, 1, row, 4).Merge().Style.Font.SetBold().Fill.BackgroundColor = XLColor.FromHtml("#E0F7FA");
            row++;
        }

        private string GenerateSecureToken(int m, int y)
        {
            try
            {
                long expiry = DateTime.UtcNow.AddMinutes(20).Ticks;
                string payload = $"{m}|{y}|{expiry}";

                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(DownloadSecret)))
                {
                    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    string signature = Convert.ToBase64String(hash);
                    string fullToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{signature}"));
                    return fullToken.Replace("+", "-").Replace("/", "_");
                }
            }
            catch
            {
                return "";
            }
        }

        private bool ValidateSecureToken(string token, int m, int y)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            try
            {
                token = token.Replace("-", "+").Replace("_", "/");
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split('|');

                if (parts.Length != 4)
                    return false;

                int tokenMonth = int.Parse(parts[0]);
                int tokenYear = int.Parse(parts[1]);
                long expiry = long.Parse(parts[2]);
                string providedSig = parts[3];

                if (tokenMonth != m || tokenYear != y)
                    return false;

                if (DateTime.UtcNow.Ticks > expiry)
                    return false;

                string payload = $"{tokenMonth}|{tokenYear}|{expiry}";

                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(DownloadSecret)))
                {
                    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    string expectedSig = Convert.ToBase64String(hash);
                    return providedSig == expectedSig;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}