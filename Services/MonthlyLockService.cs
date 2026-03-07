using FinanceSystem.Models;

namespace FinanceSystem.Services
{
    public class MonthLockService
    {
        private readonly SupabaseService _supabase;

        public MonthLockService(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public async Task<MonthlyClosure?> GetMonthClosureAsync(int month, int year)
        {
            await _supabase.InitializeAsync(true);

            var result = await _supabase.Client
                .From<MonthlyClosure>()
                .Filter("month", Supabase.Postgrest.Constants.Operator.Equals, month)
                .Filter("year", Supabase.Postgrest.Constants.Operator.Equals, year)
                .Get();

            return result.Models.FirstOrDefault();
        }

        public async Task<bool> IsMonthLockedAsync(int month, int year)
        {
            var closure = await GetMonthClosureAsync(month, year);
            return closure != null && closure.IsLocked;
        }

        public async Task<bool> IsMonthLockedAsync(DateTime activityDate)
        {
            return await IsMonthLockedAsync(activityDate.Month, activityDate.Year);
        }

        public async Task LockMonthAsync(int month, int year, string lockedBy, string? remarks)
        {
            await _supabase.InitializeAsync(true);

            var existing = await GetMonthClosureAsync(month, year);

            if (existing == null)
            {
                var newClosure = new MonthlyClosure
                {
                    Month = month,
                    Year = year,
                    IsLocked = true,
                    IsAudited = true,
                    LockedBy = lockedBy,
                    LockedAt = DateTime.Now,
                    AuditRemarks = remarks
                };

                await _supabase.Client.From<MonthlyClosure>().Insert(newClosure);
            }
            else
            {
                existing.IsLocked = true;
                existing.IsAudited = true;
                existing.LockedBy = lockedBy;
                existing.LockedAt = DateTime.Now;
                existing.AuditRemarks = remarks;

                await _supabase.Client.From<MonthlyClosure>().Update(existing);
            }
        }

        public async Task UnlockMonthAsync(int month, int year)
        {
            await _supabase.InitializeAsync(true);

            var existing = await GetMonthClosureAsync(month, year);

            if (existing != null)
            {
                existing.IsLocked = false;
                existing.IsAudited = false;
                await _supabase.Client.From<MonthlyClosure>().Update(existing);
            }
        }
    }
}