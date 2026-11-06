using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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

            // Đảm bảo bảng đồng bộ Metadata dùng chung trên mạng LAN luôn tồn tại
            context.Database.ExecuteSqlCommand(
                "CREATE TABLE IF NOT EXISTS `QuizzAppMetaItems` (" +
                "`Category` VARCHAR(32) NOT NULL, " +
                "`ItemKey` VARCHAR(64) NOT NULL, " +
                "`XmlData` LONGTEXT NOT NULL, " +
                "`UpdatedAt` DATETIME NOT NULL, " +
                "PRIMARY KEY (`Category`, `ItemKey`)" +
                ") CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
            );
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

    // 2. Cấu hình DbContext cho MySQL (Hỗ trợ cấu hình IP mạng LAN & nhiều người dùng cùng lúc)
    [DbConfigurationType(typeof(MySqlEFConfiguration))]
    public class QuizzDbContext : DbContext
    {
        private static readonly object SyncLock = new object();
        private static readonly string ConfigFilePath = Path.Combine(GetBaseDirectory(), "db_config.xml");

        private static string GetBaseDirectory()
        {
            string asmDir = Path.GetDirectoryName(typeof(QuizzDbContext).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir) && File.Exists(Path.Combine(asmDir, "QuizzApp.DAL.exe")))
            {
                return asmDir;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string ServerHost { get; private set; } = "127.0.0.1";
        public static string Port { get; private set; } = "3306";
        public static string DbName { get; private set; } = "QuizzAppDB";
        public static string DbUser { get; private set; } = "root";
        public static string DbPass { get; private set; } = "12345678";

        private static bool _configLoaded = false;
        private static bool _databaseEnsured = false;

        static QuizzDbContext()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new System.Reflection.AssemblyName(args.Name).Name;
                    string candidate1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, asmName + ".dll");
                    if (File.Exists(candidate1))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidate1);
                    }
                    string asmDir = Path.GetDirectoryName(typeof(QuizzDbContext).Assembly.Location);
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        string candidate2 = Path.Combine(asmDir, asmName + ".dll");
                        if (File.Exists(candidate2))
                        {
                            return System.Reflection.Assembly.LoadFrom(candidate2);
                        }
                    }
                }
                catch
                {
                }
                return null;
            };
            LoadConfiguration();
        }

        public static void LoadConfiguration()
        {
            lock (SyncLock)
            {
                try
                {
                    // 1. Đọc từ App.config nếu có cấu hình <appSettings>
                    string appConfigPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
                    if (!string.IsNullOrEmpty(appConfigPath) && File.Exists(appConfigPath))
                    {
                        var doc = XDocument.Load(appConfigPath);
                        var appSettings = doc.Root != null ? doc.Root.Element("appSettings") : null;
                        if (appSettings != null)
                        {
                            foreach (var addEl in appSettings.Elements("add"))
                            {
                                string key = (string)addEl.Attribute("key");
                                string val = (string)addEl.Attribute("value");
                                if (!string.IsNullOrWhiteSpace(key) && val != null)
                                {
                                    ApplySettingKeyValue(key.Trim(), val.Trim());
                                }
                            }
                        }
                    }

                    // 2. Ưu tiên đọc từ file db_config.xml cạnh file .exe (nếu người dùng đã cấu hình qua giao diện hoặc sửa file)
                    if (File.Exists(ConfigFilePath))
                    {
                        var doc = XDocument.Load(ConfigFilePath);
                        var root = doc.Root;
                        if (root != null)
                        {
                            ServerHost = GetElementValue(root, "ServerHost", ServerHost);
                            Port = GetElementValue(root, "Port", Port);
                            DbName = GetElementValue(root, "DbName", DbName);
                            DbUser = GetElementValue(root, "DbUser", DbUser);
                            DbPass = GetElementValue(root, "DbPass", DbPass);
                        }
                    }
                    else
                    {
                        // Tự động sinh file db_config.xml mẫu để quản trị viên dễ chỉnh sửa khi triển khai LAN
                        SaveConfiguration(ServerHost, Port, DbName, DbUser, DbPass);
                    }
                }
                catch
                {
                    // Giữ cấu hình mặc định nếu file lỗi
                }
                _configLoaded = true;
            }
        }

        private static void ApplySettingKeyValue(string key, string val)
        {
            if (string.Equals(key, "ServerHost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                ServerHost = val;
            else if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                Port = val;
            else if (string.Equals(key, "DbName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                DbName = val;
            else if (string.Equals(key, "DbUser", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                DbUser = val;
            else if (string.Equals(key, "DbPass", StringComparison.OrdinalIgnoreCase))
                DbPass = val;
        }

        private static string GetElementValue(XElement root, string name, string defaultValue)
        {
            var el = root.Element(name);
            if (el == null) return defaultValue;
            if (name == "DbPass") return el.Value;
            return string.IsNullOrWhiteSpace(el.Value) ? defaultValue : el.Value.Trim();
        }

        public static void SaveConfiguration(string host, string port, string dbName, string user, string pass)
        {
            lock (SyncLock)
            {
                ServerHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
                Port = string.IsNullOrWhiteSpace(port) ? "3306" : port.Trim();
                DbName = string.IsNullOrWhiteSpace(dbName) ? "QuizzAppDB" : dbName.Trim();
                DbUser = string.IsNullOrWhiteSpace(user) ? "root" : user.Trim();
                DbPass = pass ?? "";
                _databaseEnsured = false;

                try
                {
                    var doc = new XDocument(
                        new XComment(" Cấu hình kết nối Máy chủ MySQL (Đổi ServerHost thành IP máy chủ LAN, ví dụ: 192.168.1.100) "),
                        new XElement("DatabaseConfig",
                            new XElement("ServerHost", ServerHost),
                            new XElement("Port", Port),
                            new XElement("DbName", DbName),
                            new XElement("DbUser", DbUser),
                            new XElement("DbPass", DbPass)
                        )
                    );
                    doc.Save(ConfigFilePath);
                }
                catch
                {
                }
            }
        }

        public static bool TestConnection(string host, string port, string dbName, string user, string pass, out string errorMessage)
        {
            errorMessage = "";
            string cleanHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            string cleanPort = string.IsNullOrWhiteSpace(port) ? "3306" : port.Trim();
            string cleanDb = string.IsNullOrWhiteSpace(dbName) ? "QuizzAppDB" : dbName.Trim();
            string cleanUser = string.IsNullOrWhiteSpace(user) ? "root" : user.Trim();
            string cleanPass = pass ?? "";

            try
            {
                string serverConn = string.Format(
                    "Server={0};Port={1};Uid={2};Pwd={3};Charset=utf8mb4;AllowBatch=True;Connection Timeout=5;",
                    cleanHost, cleanPort, cleanUser, cleanPass);

                using (var conn = new MySqlConnection(serverConn))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(string.Format("CREATE DATABASE IF NOT EXISTS `{0}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", cleanDb), conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                // Thử kết nối trực tiếp vào Database nếu tài khoản LAN chỉ được cấp quyền trên QuizzAppDB
                try
                {
                    string dbConn = string.Format(
                        "Server={0};Port={1};Database={2};Uid={3};Pwd={4};Charset=utf8mb4;AllowBatch=True;Connection Timeout=5;",
                        cleanHost, cleanPort, cleanDb, cleanUser, cleanPass);
                    using (var conn = new MySqlConnection(dbConn))
                    {
                        conn.Open();
                    }
                    return true;
                }
                catch
                {
                    errorMessage = ex.Message;
                    return false;
                }
            }
        }

        private static string GetConnectionString()
        {
            if (!_configLoaded)
            {
                LoadConfiguration();
            }

            if (!_databaseEnsured)
            {
                lock (SyncLock)
                {
                    if (!_databaseEnsured)
                    {
                        try
                        {
                            string serverConn = string.Format(
                                "Server={0};Port={1};Uid={2};Pwd={3};Charset=utf8mb4;AllowBatch=True;Connection Timeout=5;",
                                ServerHost, Port, DbUser, DbPass);
                            using (var conn = new MySqlConnection(serverConn))
                            {
                                conn.Open();
                                using (var cmd = new MySqlCommand(string.Format("CREATE DATABASE IF NOT EXISTS `{0}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", DbName), conn))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            _databaseEnsured = true;
                        }
                        catch
                        {
                            // Nếu user LAN chỉ có quyền trên Database cụ thể mà không có quyền CREATE DATABASE toàn cục,
                            // vẫn cho phép đi tiếp để kết nối trực tiếp vào Database bên dưới.
                        }
                    }
                }
            }

            return string.Format(
                "Server={0};Port={1};Database={2};Uid={3};Pwd={4};Charset=utf8mb4;AllowBatch=True;Pooling=True;MinimumPoolSize=1;MaximumPoolSize=100;Connection Timeout=5;Default Command Timeout=30;",
                ServerHost, Port, DbName, DbUser, DbPass);
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
