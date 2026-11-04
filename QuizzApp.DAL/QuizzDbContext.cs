using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using MySql.Data.EntityFramework;
using MySql.Data.MySqlClient;
using QuizzApp.DAL.Entities;

namespace QuizzApp.DAL
{
    // 1. Bộ khởi tạo kiểm tra trực tiếp bảng Users: nếu chưa có bảng sẽ tự động tạo đủ 8 bảng và nạp Seed Data
    public class QuizzMySqlInitializer : IDatabaseInitializer<QuizzDbContext>
    {
        public void InitializeDatabase(QuizzDbContext context)
        {
            // Kiểm tra xem bảng Users đã tồn tại trong QuizzAppDB chưa
            int tableCount = context.Database.SqlQuery<int>(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND LOWER(table_name) = 'users';"
            ).FirstOrDefault();

            if (tableCount == 0)
            {
                // Tự động lấy kịch bản SQL từ 8 class Models và tạo bảng trên MySQL
                string createScript = ((IObjectContextAdapter)context).ObjectContext.CreateDatabaseScript();
                context.Database.ExecuteSqlCommand(createScript);

                // Nạp dữ liệu mẫu ban đầu
                Seed(context);
            }
        }

        private void Seed(QuizzDbContext context)
        {
            // Tạo 3 tài khoản mẫu (Mật khẩu: 123456)
            context.Users.Add(new User { Username = "admin", PasswordHash = "123456", FullName = "Quản trị viên", Role = "Admin" });
            context.Users.Add(new User { Username = "gv1", PasswordHash = "123456", FullName = "Giảng viên .NET", Role = "Teacher" });
            context.Users.Add(new User { Username = "sv1", PasswordHash = "123456", FullName = "Nguyễn Văn A", Role = "Student" });

            // Tạo môn học mẫu
            var subject = new Subject { SubjectCode = "NET101", SubjectName = "Công nghệ .NET" };
            context.Subjects.Add(subject);
            context.SaveChanges();

            // Tạo câu hỏi mẫu kèm 4 đáp án A, B, C, D
            var q1 = new Question
            {
                Content = "Trong WPF, điều khiển nào giúp sắp xếp các phần tử theo dạng bảng gồm các dòng và cột?",
                DifficultyLevel = 1,
                SubjectId = subject.Id,
                QuestionOptions = new List<QuestionOption>
                {
                    new QuestionOption { OptionContent = "StackPanel", IsCorrect = false },
                    new QuestionOption { OptionContent = "Grid", IsCorrect = true },
                    new QuestionOption { OptionContent = "WrapPanel", IsCorrect = false },
                    new QuestionOption { OptionContent = "Canvas", IsCorrect = false }
                }
            };
            context.Questions.Add(q1);
            context.SaveChanges();
        }
    }

    // 2. Cấu hình DbContext cho MySQL
    [DbConfigurationType(typeof(MySqlEFConfiguration))]
    public class QuizzDbContext : DbContext
    {
        private static readonly string ServerHost = "127.0.0.1";
        private static readonly string Port = "3306";
        private static readonly string DbName = "QuizzAppDB";
        private static readonly string DbUser = "root";
        private static readonly string DbPass = "12345678"; // Điền mật khẩu root nếu máy bạn có đặt mật khẩu

        private static string GetConnectionString()
        {
            string serverConn = $"Server={ServerHost};Port={Port};Uid={DbUser};Pwd={DbPass};Charset=utf8mb4;AllowBatch=True;";
            using (var conn = new MySqlConnection(serverConn))
            {
                conn.Open();
                using (var cmd = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{DbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            return $"Server={ServerHost};Port={Port};Database={DbName};Uid={DbUser};Pwd={DbPass};Charset=utf8mb4;AllowBatch=True;";
        }

        public QuizzDbContext()
            : base(new MySqlConnection(GetConnectionString()), true)
        {
            Database.SetInitializer(new QuizzMySqlInitializer());
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<QuestionOption> QuestionOptions { get; set; }
        public DbSet<Exam> Exams { get; set; }
        public DbSet<ExamQuestion> ExamQuestions { get; set; }
        public DbSet<ExamAttempt> ExamAttempts { get; set; }
        public DbSet<AttemptDetail> AttemptDetails { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExamQuestion>()
                .HasRequired(eq => eq.Question)
                .WithMany(q => q.ExamQuestions)
                .HasForeignKey(eq => eq.QuestionId)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<AttemptDetail>()
                .HasRequired(ad => ad.Question)
                .WithMany()
                .HasForeignKey(ad => ad.QuestionId)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Exam>()
                .HasRequired(e => e.Subject)
                .WithMany(s => s.Exams)
                .HasForeignKey(e => e.SubjectId)
                .WillCascadeOnDelete(false);

            base.OnModelCreating(modelBuilder);
        }
    }
}