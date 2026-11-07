using Microsoft.EntityFrameworkCore;

namespace Platform.Web.Data;

public static class DbContextExtensions
{
    /// <summary>
    /// Membuang salinan lama entity yang masih dilacak oleh konteks berumur panjang (test, seeder) agar query berikutnya
    /// membaca data terbaru dari database. Pada aplikasi web setiap request memakai konteks baru sehingga ini tidak berpengaruh.
    /// Hanya entity yang tidak punya perubahan tertunda (Unchanged) yang dibuang.
    /// </summary>
    public static void ForgetTracked<T>(this DbContext db, Func<T, bool> match) where T : class
    {
        foreach (var entry in db.ChangeTracker.Entries<T>().Where(e => e.State == EntityState.Unchanged && match(e.Entity)).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
