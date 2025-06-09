using Microsoft.EntityFrameworkCore;

namespace REST_API.Entity
{
    public class Dbconn : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Inventory> Inventory => Set<Inventory>();
        public DbSet<Item> Item => Set<Item>();

        public Dbconn(DbContextOptions<Dbconn> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("users");
            modelBuilder.Entity<Inventory>().ToTable("inventory");
            modelBuilder.Entity<Item>().ToTable("items");

            modelBuilder.Entity<Item>()
                .HasKey(it => it.Item_Id);

            modelBuilder.Entity<Inventory>()
                .HasKey(i => new {i.User_Id, i.Item_Id});

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.User)
                .WithMany(u => u.Inventory)
                .HasForeignKey(i => i.User_Id);
            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.Item)
                .WithMany(it => it.Inventory)
                .HasForeignKey(i => i.Item_Id);
        }
    }
}
