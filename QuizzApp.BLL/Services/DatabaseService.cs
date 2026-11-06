using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Quản lý cấu hình kết nối máy chủ MySQL, khởi tạo cơ sở dữ liệu và nạp dữ liệu mẫu (Seeding).
    /// </summary>
    public static class DatabaseService
    {
        public static void GetServerConfig(out string host, out string port, out string dbName, out string user, out string pass)
        {
            QuizzDbContext.LoadConfiguration();
            host = QuizzDbContext.ServerHost;
            port = QuizzDbContext.Port;
            dbName = QuizzDbContext.DbName;
            user = QuizzDbContext.DbUser;
            pass = QuizzDbContext.DbPass;
        }

        public static string GetServerSummaryText()
        {
            return string.Format("{0}:{1} • {2}", QuizzDbContext.ServerHost, QuizzDbContext.Port, QuizzDbContext.DbName);
        }

        public static bool ConfigureServerConnection(string host, string port, string dbName, string user, string pass, out string errorMessage)
        {
            if (!QuizzDbContext.TestConnection(host, port, dbName, user, pass, out errorMessage))
            {
                return false;
            }

            QuizzDbContext.SaveConfiguration(host, port, dbName, user, pass);

            try
            {
                InitializeDatabase();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static void InitializeDatabase()
        {
            using (var db = new QuizzDbContext())
            {
                db.Database.Initialize(false);

                // Đảm bảo có tài khoản admin và học viên mẫu (mật khẩu đã được băm bảo mật)
                if (!db.Users.Any(u => u.Username.ToLower() == "admin"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "admin",
                        PasswordHash = PasswordHasher.Hash("123456"),
                        FullName = "Giáo viên Quản trị",
                        Role = "Admin"
                    });
                }

                if (!db.Users.Any(u => u.Username.ToLower() == "hocvien"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "hocvien",
                        PasswordHash = PasswordHasher.Hash("123456"),
                        FullName = "Nguyễn Văn An",
                        Role = "Student"
                    });
                }

                if (!db.Users.Any(u => u.Username.ToLower() == "sv1"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "sv1",
                        PasswordHash = PasswordHasher.Hash("123456"),
                        FullName = "Nguyễn Văn A",
                        Role = "Student"
                    });
                }

                // Tự động nâng cấp mật khẩu plain-text cũ trong DB sang chuẩn băm có Salt
                var allUsers = db.Users.ToList();
                foreach (var u in allUsers)
                {
                    if (!string.IsNullOrEmpty(u.PasswordHash) &&
                        !u.PasswordHash.StartsWith("$QM256", StringComparison.Ordinal))
                    {
                        u.PasswordHash = PasswordHasher.Hash(u.PasswordHash);
                    }
                }

                db.SaveChanges();

                // Nếu chưa có đề thi nào trong DB, tự động nạp bộ dữ liệu mẫu
                if (!db.Exams.Any())
                {
                    SeedSampleExamsAndQuestions(db);
                }

                EnsureDefaultChapters(db);
            }
        }

        internal static void EnsureDefaultChapters(QuizzDbContext db)
        {
            var subjects = db.Subjects.Include(s => s.Chapters).ToList();
            bool changed = false;

            foreach (var s in subjects)
            {
                if (s.Chapters == null || !s.Chapters.Any())
                {
                    if (s.SubjectCode == "CSHARP")
                    {
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 1: Cú pháp & Kiểu dữ liệu" });
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 2: Lập trình Hướng đối tượng (OOP)" });
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 3: LINQ & Quản lý bộ nhớ" });
                    }
                    else if (s.SubjectCode == "NET101")
                    {
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 1: XAML & Bố cục Layout" });
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 2: Data Binding & MVVM" });
                    }
                    else if (s.SubjectCode == "SQL101")
                    {
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 1: Truy vấn SQL cơ bản" });
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 2: ADO.NET & Entity Framework" });
                    }
                    else
                    {
                        db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter { SubjectId = s.Id, Name = "Chương 1: Kiến thức chung" });
                    }
                    changed = true;
                }
            }

            if (changed)
            {
                db.SaveChanges();
            }
        }

        private static void SeedSampleExamsAndQuestions(QuizzDbContext db)
        {
            // 1. Môn Lập trình C#
            var subCSharp = db.Subjects.FirstOrDefault(s => s.SubjectCode == "CSHARP");
            if (subCSharp == null)
            {
                subCSharp = new QuizzApp.DAL.Entities.Subject { SubjectCode = "CSHARP", SubjectName = "Lập trình C#" };
                db.Subjects.Add(subCSharp);
                db.SaveChanges();
            }

            // 2. Môn Công nghệ .NET & WPF
            var subNet = db.Subjects.FirstOrDefault(s => s.SubjectCode == "NET101");
            if (subNet == null)
            {
                subNet = new QuizzApp.DAL.Entities.Subject { SubjectCode = "NET101", SubjectName = "Công nghệ .NET & WPF" };
                db.Subjects.Add(subNet);
                db.SaveChanges();
            }

            // 3. Môn Cơ sở dữ liệu & SQL
            var subSql = db.Subjects.FirstOrDefault(s => s.SubjectCode == "SQL101");
            if (subSql == null)
            {
                subSql = new QuizzApp.DAL.Entities.Subject { SubjectCode = "SQL101", SubjectName = "Cơ sở dữ liệu & SQL" };
                db.Subjects.Add(subSql);
                db.SaveChanges();
            }

            EnsureDefaultChapters(db);

            // Đề 1: C# cơ bản (Thi thật)
            var exam1 = new QuizzApp.DAL.Entities.Exam
            {
                Code = "C#",
                Title = "C# cơ bản",
                Description = "Kiến thức lập trình C# cơ bản, kiểu dữ liệu, vòng lặp và hướng đối tượng.",
                SubjectId = subCSharp.Id,
                DurationMinutes = 15,
                ShuffleQuestions = true,
                ShuffleAnswers = false,
                Mode = "Real",
                IsSaved = false,
                IsActive = true,
                IsManuallyOpened = true,
                ExamStartTime = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam1);
            db.SaveChanges();

            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 1, "Từ khóa nào dùng để khai báo một lớp (class) trong C#?",
                "struct", "class", "interface", "object", "B", 1, "Từ khóa 'class' được sử dụng để khai báo một lớp trong C#.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 2, "Kiểu dữ liệu nào sau đây là kiểu giá trị (Value Type) trong C#?",
                "string", "object", "int", "dynamic", "C", 1, "'int' (System.Int32) là kiểu giá trị (Value Type), trong khi string và object là kiểu tham chiếu.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 3, "Phương thức nào là điểm bắt đầu thực thi của một chương trình C#?",
                "Start()", "Init()", "Main()", "Run()", "C", 1, "Hàm Main() là điểm khởi đầu (entry point) mặc định của mọi ứng dụng C#.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 4, "Để kế thừa một lớp trong C#, ta sử dụng ký hiệu nào?",
                "extends", "implements", "Dấu hai chấm (:)", "->", "C", 1, "Trong C#, dấu hai chấm (:) được dùng cho cả kế thừa lớp và thực thi interface.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 5, "Từ khóa nào cho phép lớp con ghi đè (override) phương thức của lớp cha?",
                "virtual / abstract", "static", "sealed", "const", "A", 2, "Phương thức ở lớp cha phải được đánh dấu là 'virtual' hoặc 'abstract' thì lớp con mới có thể 'override'.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 6, "Giá trị mặc định của kiểu dữ liệu bool trong C# là gì?",
                "true", "false", "null", "0", "B", 1, "Kiểu bool có giá trị mặc định là false.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 7, "Cấu trúc nào dùng để bắt và xử lý ngoại lệ (Exception) trong C#?",
                "if - else", "switch - case", "try - catch - finally", "do - while", "C", 2, "Khối lệnh try - catch - finally được dùng để bắt và xử lý các lỗi phát sinh khi chạy chương trình.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 8, "Từ khóa 'sealed' khi đặt trước một class có ý nghĩa gì?",
                "Chỉ cho phép tạo 1 đối tượng duy nhất", "Ngăn không cho lớp khác kế thừa lớp đó", "Cho phép truy cập từ mọi nơi", "Bắt buộc phải kế thừa", "B", 2, "Lớp được đánh dấu 'sealed' không thể được kế thừa bởi bất kỳ lớp nào khác.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 9, "LINQ là viết tắt của cụm từ nào?",
                "Language Integrated Query", "Linear Integrated Query", "Local Index Query", "Linked Object Query", "A", 3, "LINQ viết tắt của Language Integrated Query, hỗ trợ truy vấn dữ liệu trực tiếp trong C#.");
            AddSeedQuestion(db, exam1.Id, subCSharp.Id, 10, "Bộ thu gom rác tự động trong .NET được gọi là gì?",
                "Memory Cleaner", "Garbage Collector (GC)", "Object Destroyer", "Auto Dispose", "B", 3, "Garbage Collector (GC) tự động quản lý và giải phóng bộ nhớ cho các đối tượng không còn được sử dụng.");

            // Đề 2: .NET và WPF (Thi thật)
            var exam2 = new QuizzApp.DAL.Entities.Exam
            {
                Code = "NET",
                Title = ".NET và WPF",
                Description = "Kiểm tra kiến thức nền tảng .NET, XAML, Data Binding và điều khiển giao diện WPF.",
                SubjectId = subNet.Id,
                DurationMinutes = 15,
                ShuffleQuestions = false,
                ShuffleAnswers = false,
                Mode = "Real",
                IsSaved = false,
                IsActive = true,
                IsManuallyOpened = true,
                ExamStartTime = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam2);
            db.SaveChanges();

            AddSeedQuestion(db, exam2.Id, subNet.Id, 1, "XAML trong WPF là viết tắt của từ gì?",
                "eXtensible Application Markup Language", "XML Application Modeling Language", "eXtreme Access Markup Language", "X-Platform Application Markup Language", "A", 1, "XAML là viết tắt của eXtensible Application Markup Language, dùng để thiết kế giao diện trong WPF.");
            AddSeedQuestion(db, exam2.Id, subNet.Id, 2, "Layout Panel nào trong WPF sắp xếp các phần tử con theo một hàng ngang hoặc cột dọc nối tiếp nhau?",
                "Grid", "Canvas", "StackPanel", "DockPanel", "C", 1, "StackPanel xếp các control theo chiều dọc (Vertical) hoặc chiều ngang (Horizontal).");
            AddSeedQuestion(db, exam2.Id, subNet.Id, 3, "Interface nào cần được thực thi để thông báo cho giao diện WPF tự động cập nhật khi giá trị thuộc tính trong C# thay đổi?",
                "IDisposable", "INotifyPropertyChanged", "IComparable", "IEnumerable", "B", 2, "INotifyPropertyChanged phát sự kiện PropertyChanged giúp Data Binding của WPF biết khi nào cần làm mới UI.");
            AddSeedQuestion(db, exam2.Id, subNet.Id, 4, "Thuộc tính nào của điều khiển trong WPF cho phép ẩn hoàn toàn phần tử và không chiếm không gian bố cục?",
                "Visibility.Hidden", "Visibility.Collapsed", "Opacity = 0", "IsEnabled = false", "B", 2, "Visibility.Collapsed ẩn phần tử và giải phóng luôn diện tích của nó trên Layout, còn Hidden vẫn giữ khoảng trống.");
            AddSeedQuestion(db, exam2.Id, subNet.Id, 5, "Chế độ Binding (Binding Mode) nào cho phép đồng bộ dữ liệu 2 chiều giữa giao diện (UI) và đối tượng dữ liệu (Source)?",
                "OneWay", "OneTime", "TwoWay", "OneWayToSource", "C", 1, "TwoWay binding cập nhật cả khi dữ liệu nguồn thay đổi lẫn khi người dùng nhập liệu trên UI.");

            // Đề 3: Cơ sở dữ liệu & SQL (Chế độ Luyện tập)
            var exam3 = new QuizzApp.DAL.Entities.Exam
            {
                Code = "SQL",
                Title = "Cơ sở dữ liệu & SQL",
                Description = "Kiểm tra kiến thức truy vấn SQL, thiết kế bảng, khóa chính, khóa ngoại và Entity Framework.",
                SubjectId = subSql.Id,
                DurationMinutes = 10,
                ShuffleQuestions = true,
                ShuffleAnswers = true,
                Mode = "Practice",
                IsSaved = false,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam3);
            db.SaveChanges();

            AddSeedQuestion(db, exam3.Id, subSql.Id, 1, "Câu lệnh SQL nào được sử dụng để trích xuất dữ liệu từ một bảng?",
                "GET", "OPEN", "SELECT", "EXTRACT", "C", 1, "Câu lệnh SELECT được dùng để truy vấn dữ liệu từ một hoặc nhiều bảng.");
            AddSeedQuestion(db, exam3.Id, subSql.Id, 2, "Mệnh đề nào dùng để lọc các bản ghi thỏa mãn điều kiện trước khi nhóm (GROUP BY)?",
                "HAVING", "WHERE", "ORDER BY", "FILTER", "B", 2, "WHERE lọc các dòng dữ liệu trước khi thực hiện GROUP BY, còn HAVING lọc sau khi đã nhóm.");
            AddSeedQuestion(db, exam3.Id, subSql.Id, 3, "Khóa chính (Primary Key) có đặc điểm nào sau đây?",
                "Có thể chứa nhiều giá trị NULL", "Xác định duy nhất mỗi bản ghi và không được phép NULL", "Chỉ được phép dùng kiểu số nguyên", "Một bảng có thể có tối đa 3 khóa chính", "B", 1, "Primary Key đảm bảo tính duy nhất (Unique) cho mỗi dòng và không chấp nhận giá trị NULL.");
            AddSeedQuestion(db, exam3.Id, subSql.Id, 4, "Trong ADO.NET, phương thức nào của đối tượng Command được dùng để thực thi câu lệnh INSERT, UPDATE hoặc DELETE?",
                "ExecuteReader()", "ExecuteScalar()", "ExecuteNonQuery()", "ExecuteData()", "C", 2, "ExecuteNonQuery() thực thi các câu lệnh không trả về tập dữ liệu và trả về số dòng bị ảnh hưởng.");
            AddSeedQuestion(db, exam3.Id, subSql.Id, 5, "Để tránh lỗ hổng bảo mật SQL Injection khi viết truy vấn trong C#, ta nên làm gì?",
                "Cộng chuỗi trực tiếp vào câu lệnh SQL", "Sử dụng Parameterized Queries / Entity Framework LINQ", "Tắt thông báo lỗi CSDL", "Chỉ dùng câu lệnh SELECT", "B", 3, "Sử dụng tham số (@param) hoặc LINQ trong Entity Framework giúp tách biệt mã lệnh SQL và dữ liệu đầu vào.");
        }

        private static void AddSeedQuestion(QuizzDbContext db, int examId, int subjectId, int orderIndex, string content, string a, string b, string c, string d, string correctKey, int diffLevel, string explanation)
        {
            var chap = db.Chapters.FirstOrDefault(ch => ch.SubjectId == subjectId);
            int chapId = chap != null ? chap.Id : 1;

            var q = new QuizzApp.DAL.Entities.Question
            {
                Content = content,
                DifficultyLevel = diffLevel,
                SubjectId = subjectId,
                ChapterId = chapId,
                Explanation = explanation ?? "",
                QuestionOptions = new List<QuizzApp.DAL.Entities.QuestionOption>
                {
                    new QuizzApp.DAL.Entities.QuestionOption { OptionContent = a, IsCorrect = (correctKey == "A") },
                    new QuizzApp.DAL.Entities.QuestionOption { OptionContent = b, IsCorrect = (correctKey == "B") },
                    new QuizzApp.DAL.Entities.QuestionOption { OptionContent = c, IsCorrect = (correctKey == "C") },
                    new QuizzApp.DAL.Entities.QuestionOption { OptionContent = d, IsCorrect = (correctKey == "D") }
                }
            };
            db.Questions.Add(q);
            db.SaveChanges();

            db.ExamQuestions.Add(new QuizzApp.DAL.Entities.ExamQuestion
            {
                ExamId = examId,
                QuestionId = q.Id,
                OrderIndex = orderIndex
            });
            db.SaveChanges();
        }
    }
}
