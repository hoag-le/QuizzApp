using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    [Serializable]
    public class LocalExamMeta
    {
        public int ExamId { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public bool ShuffleQuestions { get; set; }
        public bool ShuffleAnswers { get; set; }
        public string Mode { get; set; }
        public bool IsSaved { get; set; }
    }

    [Serializable]
    public class LocalQuestionMeta
    {
        public int QuestionId { get; set; }
        public int ChapterId { get; set; }
        public string Explanation { get; set; }
    }

    [Serializable]
    public class LocalChapterItem
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Name { get; set; }
    }

    [Serializable]
    public class LocalAttemptMeta
    {
        public int AttemptId { get; set; }
        public int RandomSeed { get; set; }
        public string Status { get; set; }
    }

    [Serializable]
    public class LocalAttemptAnswerMeta
    {
        public int AttemptId { get; set; }
        public int QuestionId { get; set; }
        public string SelectedOptions { get; set; }
        public bool IsFlagged { get; set; }
    }

    [Serializable]
    public class LocalMetadataStore
    {
        public List<LocalExamMeta> Exams { get; set; } = new List<LocalExamMeta>();
        public List<LocalQuestionMeta> Questions { get; set; } = new List<LocalQuestionMeta>();
        public List<LocalChapterItem> Chapters { get; set; } = new List<LocalChapterItem>();
        public List<LocalAttemptMeta> Attempts { get; set; } = new List<LocalAttemptMeta>();
        public List<LocalAttemptAnswerMeta> AttemptAnswers { get; set; } = new List<LocalAttemptAnswerMeta>();
    }

    public class DbMetaItemRow
    {
        public string Category { get; set; }
        public string ItemKey { get; set; }
        public string XmlData { get; set; }
    }

    public static class QuizService
    {
        private static readonly object MetaLock = new object();
        private static readonly string MetaFilePath = Path.Combine(GetBaseDirectory(), "QuizzApp_Meta.xml");
        private static LocalMetadataStore _metaCache;
        private static DateTime _lastMetaFetchUtc = DateTime.MinValue;
        private static bool _metaTableEnsured = false;

        private static string GetBaseDirectory()
        {
            string asmDir = Path.GetDirectoryName(typeof(QuizService).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir) && File.Exists(Path.Combine(asmDir, "QuizzApp.BLL.exe")))
            {
                return asmDir;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        static QuizService()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new System.Reflection.AssemblyName(args.Name).Name;
                    string candidate = Path.Combine(GetBaseDirectory(), asmName + ".dll");
                    if (File.Exists(candidate))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidate);
                    }
                }
                catch
                {
                }
                return null;
            };
        }

        // ==================== SERVER CONNECTION CONFIG (LAN / MULTI-USER) ====================

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
            return string.Format("{0}:{1} ({2})", QuizzDbContext.ServerHost, QuizzDbContext.Port, QuizzDbContext.DbName);
        }

        public static bool ConfigureServerConnection(string host, string port, string dbName, string user, string pass, out string errorMessage)
        {
            if (!QuizzDbContext.TestConnection(host, port, dbName, user, pass, out errorMessage))
            {
                return false;
            }

            QuizzDbContext.SaveConfiguration(host, port, dbName, user, pass);
            lock (MetaLock)
            {
                _metaTableEnsured = false;
                _metaCache = null;
                _lastMetaFetchUtc = DateTime.MinValue;
            }

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

        // ==================== SHARED MYSQL METADATA ENGINE ====================

        private static string SerializeItemToXml<T>(T item)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, item);
                return sw.ToString();
            }
        }

        private static T DeserializeItemFromXml<T>(string xml) where T : class
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var serializer = new XmlSerializer(typeof(T));
                using (var sr = new StringReader(xml))
                {
                    return serializer.Deserialize(sr) as T;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureMetaTableExists(QuizzDbContext db)
        {
            if (_metaTableEnsured) return;
            db.Database.ExecuteSqlCommand(
                "CREATE TABLE IF NOT EXISTS `QuizzAppMetaItems` (" +
                "`Category` VARCHAR(32) NOT NULL, " +
                "`ItemKey` VARCHAR(64) NOT NULL, " +
                "`XmlData` LONGTEXT NOT NULL, " +
                "`UpdatedAt` DATETIME NOT NULL, " +
                "PRIMARY KEY (`Category`, `ItemKey`)" +
                ") CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
            );
            _metaTableEnsured = true;
        }

        private static void UpsertMetaRow<T>(string category, string itemKey, T item, QuizzDbContext existingDb = null)
        {
            try
            {
                string xml = SerializeItemToXml(item);
                const string sql =
                    "INSERT INTO `QuizzAppMetaItems` (`Category`, `ItemKey`, `XmlData`, `UpdatedAt`) " +
                    "VALUES (@p0, @p1, @p2, NOW()) " +
                    "ON DUPLICATE KEY UPDATE `XmlData` = VALUES(`XmlData`), `UpdatedAt` = NOW();";

                if (existingDb != null)
                {
                    EnsureMetaTableExists(existingDb);
                    existingDb.Database.ExecuteSqlCommand(sql, category, itemKey, xml);
                }
                else
                {
                    using (var db = new QuizzDbContext())
                    {
                        EnsureMetaTableExists(db);
                        db.Database.ExecuteSqlCommand(sql, category, itemKey, xml);
                    }
                }
            }
            catch
            {
                // Ignore transient metadata sync errors
            }
        }

        private static void DeleteMetaRow(string category, string itemKey, QuizzDbContext existingDb = null)
        {
            try
            {
                const string sql = "DELETE FROM `QuizzAppMetaItems` WHERE `Category` = @p0 AND `ItemKey` = @p1;";
                if (existingDb != null)
                {
                    EnsureMetaTableExists(existingDb);
                    existingDb.Database.ExecuteSqlCommand(sql, category, itemKey);
                }
                else
                {
                    using (var db = new QuizzDbContext())
                    {
                        EnsureMetaTableExists(db);
                        db.Database.ExecuteSqlCommand(sql, category, itemKey);
                    }
                }
            }
            catch
            {
            }
        }

        private static void DeleteMetaByKeyPrefix(string category, string prefix, QuizzDbContext existingDb = null)
        {
            try
            {
                const string sql = "DELETE FROM `QuizzAppMetaItems` WHERE `Category` = @p0 AND `ItemKey` LIKE @p1;";
                string pattern = prefix + "%";
                if (existingDb != null)
                {
                    EnsureMetaTableExists(existingDb);
                    existingDb.Database.ExecuteSqlCommand(sql, category, pattern);
                }
                else
                {
                    using (var db = new QuizzDbContext())
                    {
                        EnsureMetaTableExists(db);
                        db.Database.ExecuteSqlCommand(sql, category, pattern);
                    }
                }
            }
            catch
            {
            }
        }

        private static void PushStoreToMySql(QuizzDbContext db, LocalMetadataStore store)
        {
            if (store == null) return;
            EnsureMetaTableExists(db);

            foreach (var c in store.Chapters)
            {
                UpsertMetaRow("Chapter", c.Id.ToString(), c, db);
            }
            foreach (var e in store.Exams)
            {
                UpsertMetaRow("Exam", e.ExamId.ToString(), e, db);
            }
            foreach (var q in store.Questions)
            {
                UpsertMetaRow("Question", q.QuestionId.ToString(), q, db);
            }
            foreach (var a in store.Attempts)
            {
                UpsertMetaRow("Attempt", a.AttemptId.ToString(), a, db);
            }
            foreach (var ans in store.AttemptAnswers)
            {
                UpsertMetaRow("Answer", string.Format("{0}_{1}", ans.AttemptId, ans.QuestionId), ans, db);
            }
        }

        private static LocalMetadataStore LoadMetaFromLocalFile()
        {
            try
            {
                if (File.Exists(MetaFilePath))
                {
                    var serializer = new XmlSerializer(typeof(LocalMetadataStore));
                    using (var fs = new FileStream(MetaFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var store = (LocalMetadataStore)serializer.Deserialize(fs);
                        if (store != null) return store;
                    }
                }
            }
            catch
            {
            }
            return new LocalMetadataStore();
        }

        private static void SaveMetaToLocalFile(LocalMetadataStore store)
        {
            try
            {
                if (store == null) return;
                var serializer = new XmlSerializer(typeof(LocalMetadataStore));
                using (var fs = new FileStream(MetaFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    serializer.Serialize(fs, store);
                }
            }
            catch
            {
            }
        }

        private static LocalMetadataStore GetMeta(bool forceRefresh = false)
        {
            lock (MetaLock)
            {
                if (!forceRefresh && _metaCache != null && (DateTime.UtcNow - _lastMetaFetchUtc).TotalMilliseconds < 800)
                {
                    return _metaCache;
                }

                try
                {
                    using (var db = new QuizzDbContext())
                    {
                        EnsureMetaTableExists(db);
                        var rows = db.Database.SqlQuery<DbMetaItemRow>(
                            "SELECT `Category`, `ItemKey`, `XmlData` FROM `QuizzAppMetaItems`;"
                        ).ToList();

                        if (rows.Count == 0 && File.Exists(MetaFilePath))
                        {
                            // Tự động đồng bộ dữ liệu từ file QuizzApp_Meta.xml cục bộ lên MySQL lần đầu
                            var migratedStore = LoadMetaFromLocalFile();
                            PushStoreToMySql(db, migratedStore);
                            _metaCache = migratedStore;
                            _lastMetaFetchUtc = DateTime.UtcNow;
                            return _metaCache;
                        }

                        var store = new LocalMetadataStore();
                        foreach (var row in rows)
                        {
                            if (row.Category == "Chapter")
                            {
                                var item = DeserializeItemFromXml<LocalChapterItem>(row.XmlData);
                                if (item != null) store.Chapters.Add(item);
                            }
                            else if (row.Category == "Exam")
                            {
                                var item = DeserializeItemFromXml<LocalExamMeta>(row.XmlData);
                                if (item != null) store.Exams.Add(item);
                            }
                            else if (row.Category == "Question")
                            {
                                var item = DeserializeItemFromXml<LocalQuestionMeta>(row.XmlData);
                                if (item != null) store.Questions.Add(item);
                            }
                            else if (row.Category == "Attempt")
                            {
                                var item = DeserializeItemFromXml<LocalAttemptMeta>(row.XmlData);
                                if (item != null) store.Attempts.Add(item);
                            }
                            else if (row.Category == "Answer")
                            {
                                var item = DeserializeItemFromXml<LocalAttemptAnswerMeta>(row.XmlData);
                                if (item != null) store.AttemptAnswers.Add(item);
                            }
                        }

                        if ((store.Exams.Count == 0 || store.Questions.Count == 0) && File.Exists(MetaFilePath))
                        {
                            var localStore = LoadMetaFromLocalFile();
                            foreach (var e in localStore.Exams)
                            {
                                if (!store.Exams.Any(x => x.ExamId == e.ExamId))
                                {
                                    store.Exams.Add(e);
                                    UpsertMetaRow("Exam", e.ExamId.ToString(), e, db);
                                }
                            }
                            foreach (var q in localStore.Questions)
                            {
                                if (!store.Questions.Any(x => x.QuestionId == q.QuestionId))
                                {
                                    store.Questions.Add(q);
                                    UpsertMetaRow("Question", q.QuestionId.ToString(), q, db);
                                }
                            }
                            foreach (var c in localStore.Chapters)
                            {
                                if (!store.Chapters.Any(x => x.Id == c.Id))
                                {
                                    store.Chapters.Add(c);
                                    UpsertMetaRow("Chapter", c.Id.ToString(), c, db);
                                }
                            }
                        }

                        _metaCache = store;
                        _lastMetaFetchUtc = DateTime.UtcNow;
                        return _metaCache;
                    }
                }
                catch
                {
                    if (_metaCache == null)
                    {
                        _metaCache = LoadMetaFromLocalFile();
                    }
                    return _metaCache;
                }
            }
        }

        private static void SaveMeta()
        {
            lock (MetaLock)
            {
                if (_metaCache != null)
                {
                    SaveMetaToLocalFile(_metaCache);
                }
            }
        }

        public static void InitializeDatabase()
        {
            using (var db = new QuizzDbContext())
            {
                db.Database.Initialize(false);
                EnsureMetaTableExists(db);

                // Đảm bảo có tài khoản admin và học viên mẫu
                if (!db.Users.Any(u => u.Username.ToLower() == "admin"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "admin",
                        PasswordHash = "123456",
                        FullName = "Giáo viên Quản trị",
                        Role = "Admin"
                    });
                }

                if (!db.Users.Any(u => u.Username.ToLower() == "hocvien"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "hocvien",
                        PasswordHash = "123456",
                        FullName = "Nguyễn Văn An",
                        Role = "Student"
                    });
                }

                if (!db.Users.Any(u => u.Username.ToLower() == "sv1"))
                {
                    db.Users.Add(new QuizzApp.DAL.Entities.User
                    {
                        Username = "sv1",
                        PasswordHash = "123456",
                        FullName = "Nguyễn Văn A",
                        Role = "Student"
                    });
                }

                db.SaveChanges();

                // Nạp và đồng bộ Metadata từ MySQL (hoặc migrate từ file XML nếu lần đầu)
                GetMeta(true);

                // Nếu chưa có đề thi nào trong DB, tự động nạp bộ dữ liệu mẫu
                if (!db.Exams.Any())
                {
                    SeedSampleExamsAndQuestions(db);
                }

                EnsureDefaultChapters(db);
            }
        }

        private static void EnsureDefaultChapters(QuizzDbContext db)
        {
            var meta = GetMeta();
            var subjects = db.Subjects.ToList();
            bool changed = false;

            foreach (var s in subjects)
            {
                if (!meta.Chapters.Any(c => c.SubjectId == s.Id))
                {
                    int nextId = meta.Chapters.Count > 0 ? meta.Chapters.Max(c => c.Id) + 1 : 1;
                    var addedChapters = new List<LocalChapterItem>();

                    if (s.SubjectCode == "CSHARP")
                    {
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 1: Cú pháp & Kiểu dữ liệu" });
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 2: Lập trình Hướng đối tượng (OOP)" });
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 3: LINQ & Quản lý bộ nhớ" });
                    }
                    else if (s.SubjectCode == "NET101")
                    {
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 1: XAML & Bố cục Layout" });
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 2: Data Binding & MVVM" });
                    }
                    else if (s.SubjectCode == "SQL101")
                    {
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 1: Truy vấn SQL cơ bản" });
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 2: ADO.NET & Entity Framework" });
                    }
                    else
                    {
                        addedChapters.Add(new LocalChapterItem { Id = nextId++, SubjectId = s.Id, Name = "Chương 1: Kiến thức chung" });
                    }

                    foreach (var ch in addedChapters)
                    {
                        meta.Chapters.Add(ch);
                        UpsertMetaRow("Chapter", ch.Id.ToString(), ch, db);
                    }
                    changed = true;
                }
            }

            if (changed)
            {
                SaveMeta();
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

            // Đề 1: C# cơ bản
            var exam1 = new QuizzApp.DAL.Entities.Exam
            {
                Title = "C# cơ bản",
                SubjectId = subCSharp.Id,
                DurationMinutes = 15,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam1);
            db.SaveChanges();

            SetExamMeta(exam1.Id, "C#", "Kiến thức lập trình C# cơ bản, kiểu dữ liệu, vòng lặp và hướng đối tượng.", true, false, "Real", true, db);

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

            // Đề 2: .NET và WPF
            var exam2 = new QuizzApp.DAL.Entities.Exam
            {
                Title = ".NET và WPF",
                SubjectId = subNet.Id,
                DurationMinutes = 15,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam2);
            db.SaveChanges();

            SetExamMeta(exam2.Id, "NET", "Kiểm tra kiến thức nền tảng .NET, XAML, Data Binding và điều khiển giao diện WPF.", false, false, "Real", false, db);

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

            // Đề 3: Cơ sở dữ liệu & SQL (Chế độ luyện tập)
            var exam3 = new QuizzApp.DAL.Entities.Exam
            {
                Title = "Cơ sở dữ liệu & SQL",
                SubjectId = subSql.Id,
                DurationMinutes = 10,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Exams.Add(exam3);
            db.SaveChanges();

            SetExamMeta(exam3.Id, "SQL", "Kiểm tra kiến thức truy vấn SQL, thiết kế bảng, khóa chính, khóa ngoại và Entity Framework.", true, true, "Practice", false, db);

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

            SaveMeta();
        }

        private static void AddSeedQuestion(QuizzDbContext db, int examId, int subjectId, int orderIndex, string content, string a, string b, string c, string d, string correctKey, int diffLevel, string explanation)
        {
            var q = new QuizzApp.DAL.Entities.Question
            {
                Content = content,
                DifficultyLevel = diffLevel,
                SubjectId = subjectId,
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

            var chap = GetMeta().Chapters.FirstOrDefault(ch => ch.SubjectId == subjectId);
            SetQuestionMeta(q.Id, chap != null ? chap.Id : 1, explanation, db);
        }

        private static void SetExamMeta(int examId, string code, string desc, bool shuffleQ, bool shuffleA, string mode, bool isSaved, QuizzDbContext existingDb = null)
        {
            var meta = GetMeta();
            var existing = meta.Exams.FirstOrDefault(x => x.ExamId == examId);
            if (existing == null)
            {
                existing = new LocalExamMeta { ExamId = examId };
                meta.Exams.Add(existing);
            }
            existing.Code = code ?? "QUIZ";
            existing.Description = desc ?? "";
            existing.ShuffleQuestions = shuffleQ;
            existing.ShuffleAnswers = shuffleA;
            existing.Mode = mode ?? "Real";
            existing.IsSaved = isSaved;

            UpsertMetaRow("Exam", examId.ToString(), existing, existingDb);
        }

        private static void SetQuestionMeta(int questionId, int chapterId, string explanation, QuizzDbContext existingDb = null)
        {
            var meta = GetMeta();
            var existing = meta.Questions.FirstOrDefault(x => x.QuestionId == questionId);
            if (existing == null)
            {
                existing = new LocalQuestionMeta { QuestionId = questionId };
                meta.Questions.Add(existing);
            }
            existing.ChapterId = chapterId;
            existing.Explanation = explanation ?? "";

            UpsertMetaRow("Question", questionId.ToString(), existing, existingDb);
        }

        private static string MapRoleToEntityRole(string uiRole)
        {
            if (string.Equals(uiRole, "Admin", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (string.Equals(uiRole, "Teacher", StringComparison.OrdinalIgnoreCase)) return "Teacher";
            return "Student";
        }

        private static int MapDifficultyToInt(string diff)
        {
            if (string.Equals(diff, "Hard", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(diff, "Medium", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static string MapIntToDifficulty(int level)
        {
            if (level >= 3) return "Hard";
            if (level == 2) return "Medium";
            return "Easy";
        }

        // ==================== AUTHENTICATION ====================

        public static UserAccount AuthenticateUser(string username, string password)
        {
            string cleanUser = (username ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(cleanUser)) return null;

            using (var db = new QuizzDbContext())
            {
                var u = db.Users.FirstOrDefault(x => x.Username.ToLower() == cleanUser);
                if (u != null && PasswordHasher.Verify(password ?? "", u.PasswordHash))
                {
                    return new UserAccount
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Password = u.PasswordHash,
                        FullName = u.FullName,
                        Role = u.Role
                    };
                }
            }
            return null;
        }

        public static bool RegisterUser(string username, string password, string fullName, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Vui lòng nhập đầy đủ tên đăng nhập, mật khẩu và họ tên.";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower))
                {
                    errorMessage = "Tên đăng nhập này đã tồn tại. Vui lòng chọn tên khác!";
                    return false;
                }

                db.Users.Add(new QuizzApp.DAL.Entities.User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = "Student"
                });
                db.SaveChanges();
            }
            return true;
        }

        public static List<UserAccount> GetAllUsers()
        {
            using (var db = new QuizzDbContext())
            {
                var users = db.Users.OrderBy(u => u.Id).ToList();
                return users.Select(u => new UserAccount
                {
                    Id = u.Id,
                    Username = u.Username,
                    Password = u.PasswordHash,
                    FullName = u.FullName,
                    Role = u.Role,
                    CreatedAt = ""
                }).ToList();
            }
        }

        public static bool AddUserByAdmin(string username, string password, string fullName, string role, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            string entityRole = MapRoleToEntityRole(role);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Vui lòng nhập đầy đủ Tên đăng nhập, Mật khẩu và Họ tên!";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower))
                {
                    errorMessage = "Tên đăng nhập này đã tồn tại trong hệ thống!";
                    return false;
                }

                db.Users.Add(new QuizzApp.DAL.Entities.User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = entityRole
                });
                db.SaveChanges();
            }
            return true;
        }

        public static bool UpdateUserByAdmin(int userId, string username, string newPasswordOptional, string fullName, string role, out string errorMessage)
        {
            errorMessage = "";
            username = (username ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            string entityRole = MapRoleToEntityRole(role);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(fullName))
            {
                errorMessage = "Tên đăng nhập và Họ tên không được để trống!";
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                string lower = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == lower && u.Id != userId))
                {
                    errorMessage = "Tên đăng nhập này đã được sử dụng bởi tài khoản khác!";
                    return false;
                }

                var user = db.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null)
                {
                    errorMessage = "Không tìm thấy tài khoản cần cập nhật!";
                    return false;
                }

                user.Username = username;
                user.FullName = fullName;
                user.Role = entityRole;
                if (!string.IsNullOrWhiteSpace(newPasswordOptional))
                {
                    user.PasswordHash = PasswordHasher.Hash(newPasswordOptional.Trim());
                }
                db.SaveChanges();
            }
            return true;
        }

        public static void DeleteUser(int userId)
        {
            using (var db = new QuizzDbContext())
            {
                var attempts = db.ExamAttempts.Include(a => a.AttemptDetails).Where(a => a.UserId == userId).ToList();
                foreach (var att in attempts)
                {
                    db.AttemptDetails.RemoveRange(att.AttemptDetails);
                    DeleteMetaRow("Attempt", att.Id.ToString(), db);
                    DeleteMetaByKeyPrefix("Answer", att.Id.ToString() + "_", db);
                }
                db.ExamAttempts.RemoveRange(attempts);

                var user = db.Users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    db.Users.Remove(user);
                }
                db.SaveChanges();
            }
        }

        // ==================== SUBJECTS & CHAPTERS ====================

        public static List<Subject> GetAllSubjects()
        {
            using (var db = new QuizzDbContext())
            {
                EnsureDefaultChapters(db);
                var meta = GetMeta();
                var subjects = db.Subjects.Include(s => s.Questions).OrderBy(s => s.Id).ToList();

                return subjects.Select(s => new Subject
                {
                    Id = s.Id,
                    Code = s.SubjectCode,
                    Name = s.SubjectName,
                    Description = string.Format("Mã môn học: {0}", s.SubjectCode),
                    ChapterCount = Math.Max(1, meta.Chapters.Count(c => c.SubjectId == s.Id)),
                    QuestionCount = s.Questions != null ? s.Questions.Count : 0
                }).ToList();
            }
        }

        private static string GenerateSubjectCode(string name, string desc)
        {
            string cleanDesc = (desc ?? "").Replace("Mã môn học:", "").Trim();
            if (!string.IsNullOrEmpty(cleanDesc) && cleanDesc.Length <= 18 && !cleanDesc.Contains(" "))
            {
                return cleanDesc.ToUpperInvariant();
            }
            string prefix = "MH";
            if (!string.IsNullOrWhiteSpace(name))
            {
                var chars = name.Where(char.IsLetterOrDigit).Take(4).ToArray();
                if (chars.Length > 0) prefix = new string(chars).ToUpperInvariant();
            }
            return prefix + DateTime.Now.ToString("mmss");
        }

        public static int AddSubject(string name, string description)
        {
            using (var db = new QuizzDbContext())
            {
                string code = GenerateSubjectCode(name, description);
                var subj = new QuizzApp.DAL.Entities.Subject
                {
                    SubjectCode = code,
                    SubjectName = name.Trim()
                };
                db.Subjects.Add(subj);
                db.SaveChanges();

                var meta = GetMeta(true);
                int nextChapId = meta.Chapters.Count > 0 ? meta.Chapters.Max(c => c.Id) + 1 : 1;
                var newChap = new LocalChapterItem
                {
                    Id = nextChapId,
                    SubjectId = subj.Id,
                    Name = "Chương 1: Kiến thức chung"
                };
                meta.Chapters.Add(newChap);
                UpsertMetaRow("Chapter", newChap.Id.ToString(), newChap, db);
                SaveMeta();

                return subj.Id;
            }
        }

        public static void UpdateSubject(int id, string name, string description)
        {
            using (var db = new QuizzDbContext())
            {
                var subj = db.Subjects.FirstOrDefault(s => s.Id == id);
                if (subj != null)
                {
                    subj.SubjectName = name.Trim();
                    string cleanDesc = (description ?? "").Replace("Mã môn học:", "").Trim();
                    if (!string.IsNullOrEmpty(cleanDesc) && cleanDesc.Length <= 20 && !cleanDesc.Contains(" "))
                    {
                        subj.SubjectCode = cleanDesc.ToUpperInvariant();
                    }
                    db.SaveChanges();
                }
            }
        }

        public static void DeleteSubject(int id)
        {
            using (var db = new QuizzDbContext())
            {
                var exams = db.Exams.Where(e => e.SubjectId == id).ToList();
                foreach (var ex in exams)
                {
                    DeleteExamInternal(db, ex.Id);
                }

                var questions = db.Questions.Include(q => q.QuestionOptions).Where(q => q.SubjectId == id).ToList();
                foreach (var q in questions)
                {
                    DeleteQuestionInternal(db, q.Id);
                }

                var subj = db.Subjects.FirstOrDefault(s => s.Id == id);
                if (subj != null)
                {
                    db.Subjects.Remove(subj);
                }
                db.SaveChanges();

                var meta = GetMeta();
                var chapsToDelete = meta.Chapters.Where(c => c.SubjectId == id).Select(c => c.Id).ToList();
                foreach (int cid in chapsToDelete)
                {
                    DeleteMetaRow("Chapter", cid.ToString(), db);
                }
                meta.Chapters.RemoveAll(c => c.SubjectId == id);
                SaveMeta();
            }
        }

        public static List<Chapter> GetChaptersBySubject(int? subjectId = null)
        {
            using (var db = new QuizzDbContext())
            {
                EnsureDefaultChapters(db);
                var meta = GetMeta();
                var subjects = db.Subjects.ToDictionary(s => s.Id, s => s.SubjectName);

                IEnumerable<LocalChapterItem> query = meta.Chapters;
                if (subjectId.HasValue && subjectId.Value > 0)
                {
                    query = query.Where(c => c.SubjectId == subjectId.Value);
                }

                return query.OrderBy(c => c.Id).Select(c => new Chapter
                {
                    Id = c.Id,
                    SubjectId = c.SubjectId,
                    Name = c.Name,
                    SubjectName = subjects.ContainsKey(c.SubjectId) ? subjects[c.SubjectId] : ""
                }).ToList();
            }
        }

        public static void AddChapter(int subjectId, string chapterName)
        {
            var meta = GetMeta(true);
            int nextId = meta.Chapters.Count > 0 ? meta.Chapters.Max(c => c.Id) + 1 : 1;
            var newChap = new LocalChapterItem
            {
                Id = nextId,
                SubjectId = subjectId,
                Name = (chapterName ?? "").Trim()
            };
            meta.Chapters.Add(newChap);
            UpsertMetaRow("Chapter", newChap.Id.ToString(), newChap);
            SaveMeta();
        }

        public static void DeleteChapter(int chapterId)
        {
            var meta = GetMeta();
            meta.Chapters.RemoveAll(c => c.Id == chapterId);
            DeleteMetaRow("Chapter", chapterId.ToString());
            SaveMeta();
        }

        // ==================== EXAMS & MATRIX BUILDER ====================

        public static List<Exam> GetAllExams()
        {
            using (var db = new QuizzDbContext())
            {
                var meta = GetMeta();
                var exams = db.Exams
                    .Include(e => e.Subject)
                    .Include(e => e.ExamQuestions)
                    .OrderBy(e => e.Id)
                    .ToList();

                var result = new List<Exam>();
                foreach (var e in exams)
                {
                    var em = meta.Exams.FirstOrDefault(x => x.ExamId == e.Id);
                    string subjName = e.Subject != null ? e.Subject.SubjectName : "Chung";
                    result.Add(new Exam
                    {
                        Id = e.Id,
                        Code = em != null && !string.IsNullOrEmpty(em.Code) ? em.Code : (e.Subject != null ? e.Subject.SubjectCode : "QUIZ"),
                        Title = e.Title,
                        Description = em != null && !string.IsNullOrEmpty(em.Description)
                            ? em.Description
                            : string.Format("Đề kiểm tra trắc nghiệm môn {0}", subjName),
                        SubjectId = e.SubjectId,
                        SubjectName = subjName,
                        DurationMinutes = e.DurationMinutes,
                        ShuffleQuestions = em != null ? em.ShuffleQuestions : true,
                        ShuffleAnswers = em != null ? em.ShuffleAnswers : false,
                        Mode = em != null && !string.IsNullOrEmpty(em.Mode) ? em.Mode : "Real",
                        IconBg = "#DBEAFE",
                        IconFg = "#1D4ED8",
                        IsSaved = em != null && em.IsSaved,
                        QuestionCount = e.ExamQuestions != null ? e.ExamQuestions.Count : 0
                    });
                }
                return result;
            }
        }

        public static void GetSubjectDifficultyCounts(int subjectId, out int easyAvailable, out int mediumAvailable, out int hardAvailable)
        {
            easyAvailable = 0;
            mediumAvailable = 0;
            hardAvailable = 0;

            using (var db = new QuizzDbContext())
            {
                var questions = db.Questions.Where(q => q.SubjectId == subjectId).ToList();
                easyAvailable = questions.Count(q => q.DifficultyLevel <= 1);
                mediumAvailable = questions.Count(q => q.DifficultyLevel == 2);
                hardAvailable = questions.Count(q => q.DifficultyLevel >= 3);
            }
        }

        public static bool BuildExamWithMatrix(Exam exam, int easyReq, int mediumReq, int hardReq, out string message)
        {
            message = "";
            GetSubjectDifficultyCounts(exam.SubjectId, out int easyAvail, out int medAvail, out int hardAvail);

            if (easyReq > easyAvail || mediumReq > medAvail || hardReq > hardAvail)
            {
                message = string.Format(
                    "Ngân hàng câu hỏi môn này không đủ số lượng yêu cầu!\n" +
                    "• Câu Dễ khả dụng: {0} (Yêu cầu: {1})\n" +
                    "• Câu Trung bình khả dụng: {2} (Yêu cầu: {3})\n" +
                    "• Câu Khó khả dụng: {4} (Yêu cầu: {5})",
                    easyAvail, easyReq, medAvail, mediumReq, hardAvail, hardReq);
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                var newExam = new QuizzApp.DAL.Entities.Exam
                {
                    Title = exam.Title,
                    SubjectId = exam.SubjectId,
                    DurationMinutes = exam.DurationMinutes,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                db.Exams.Add(newExam);
                db.SaveChanges();

                SetExamMeta(newExam.Id, exam.Code ?? "QUIZ", exam.Description, exam.ShuffleQuestions, exam.ShuffleAnswers, exam.Mode, false, db);
                SaveMeta();

                var rng = new Random();
                var allSubjQuestions = db.Questions.Where(q => q.SubjectId == exam.SubjectId).ToList();

                var pickedIds = new List<int>();
                pickedIds.AddRange(allSubjQuestions.Where(q => q.DifficultyLevel <= 1).OrderBy(x => rng.Next()).Take(easyReq).Select(q => q.Id));
                pickedIds.AddRange(allSubjQuestions.Where(q => q.DifficultyLevel == 2).OrderBy(x => rng.Next()).Take(mediumReq).Select(q => q.Id));
                pickedIds.AddRange(allSubjQuestions.Where(q => q.DifficultyLevel >= 3).OrderBy(x => rng.Next()).Take(hardReq).Select(q => q.Id));

                int order = 1;
                foreach (int qid in pickedIds)
                {
                    db.ExamQuestions.Add(new QuizzApp.DAL.Entities.ExamQuestion
                    {
                        ExamId = newExam.Id,
                        QuestionId = qid,
                        OrderIndex = order++
                    });
                }
                db.SaveChanges();

                message = string.Format("Đã tạo đề thi '{0}' thành công với {1} câu hỏi theo ma trận ({2} Dễ, {3} TB, {4} Khó)!",
                    exam.Title, pickedIds.Count, easyReq, mediumReq, hardReq);
                return true;
            }
        }

        public static int AddExam(Exam exam)
        {
            using (var db = new QuizzDbContext())
            {
                int targetSubjectId = exam.SubjectId > 0 ? exam.SubjectId : db.Subjects.Select(s => s.Id).FirstOrDefault();
                var entity = new QuizzApp.DAL.Entities.Exam
                {
                    Title = exam.Title,
                    SubjectId = targetSubjectId,
                    DurationMinutes = exam.DurationMinutes,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                db.Exams.Add(entity);
                db.SaveChanges();

                SetExamMeta(entity.Id, exam.Code ?? "QUIZ", exam.Description, exam.ShuffleQuestions, exam.ShuffleAnswers, exam.Mode ?? "Real", exam.IsSaved, db);
                SaveMeta();
                return entity.Id;
            }
        }

        public static void UpdateExam(Exam exam)
        {
            using (var db = new QuizzDbContext())
            {
                var entity = db.Exams.Include(e => e.ExamQuestions).FirstOrDefault(e => e.Id == exam.Id);
                if (entity != null)
                {
                    entity.Title = exam.Title;
                    entity.SubjectId = exam.SubjectId > 0 ? exam.SubjectId : entity.SubjectId;
                    entity.DurationMinutes = exam.DurationMinutes;

                    // Đồng bộ môn học cho các câu hỏi thuộc đề thi
                    var qIds = entity.ExamQuestions.Select(eq => eq.QuestionId).ToList();
                    var linkedQuestions = db.Questions.Where(q => qIds.Contains(q.Id)).ToList();
                    foreach (var q in linkedQuestions)
                    {
                        q.SubjectId = entity.SubjectId;
                    }

                    db.SaveChanges();
                }

                SetExamMeta(exam.Id, exam.Code ?? "QUIZ", exam.Description, exam.ShuffleQuestions, exam.ShuffleAnswers, exam.Mode ?? "Real", exam.IsSaved, db);
                SaveMeta();
            }
        }

        public static void DeleteExam(int examId)
        {
            using (var db = new QuizzDbContext())
            {
                DeleteExamInternal(db, examId);
                db.SaveChanges();
            }
        }

        private static void DeleteExamInternal(QuizzDbContext db, int examId)
        {
            var attempts = db.ExamAttempts.Include(a => a.AttemptDetails).Where(a => a.ExamId == examId).ToList();
            foreach (var att in attempts)
            {
                db.AttemptDetails.RemoveRange(att.AttemptDetails);
                DeleteMetaRow("Attempt", att.Id.ToString(), db);
                DeleteMetaByKeyPrefix("Answer", att.Id.ToString() + "_", db);
            }
            db.ExamAttempts.RemoveRange(attempts);

            var eqs = db.ExamQuestions.Where(eq => eq.ExamId == examId).ToList();
            db.ExamQuestions.RemoveRange(eqs);

            var exam = db.Exams.FirstOrDefault(e => e.Id == examId);
            if (exam != null)
            {
                db.Exams.Remove(exam);
            }

            var meta = GetMeta();
            meta.Exams.RemoveAll(x => x.ExamId == examId);
            DeleteMetaRow("Exam", examId.ToString(), db);
            SaveMeta();
        }

        public static void ToggleSaveExam(int examId, bool isSaved)
        {
            var meta = GetMeta();
            var em = meta.Exams.FirstOrDefault(x => x.ExamId == examId);
            if (em == null)
            {
                em = new LocalExamMeta { ExamId = examId, Code = "QUIZ", Mode = "Real", ShuffleQuestions = true };
                meta.Exams.Add(em);
            }
            em.IsSaved = isSaved;
            UpsertMetaRow("Exam", examId.ToString(), em);
            SaveMeta();
        }

        // ==================== QUESTIONS CRUD & IMPORT/EXPORT ====================

        public static List<Question> GetQuestionsByExamId(int examId)
        {
            using (var db = new QuizzDbContext())
            {
                var examQuestions = db.ExamQuestions
                    .Include(eq => eq.Question.QuestionOptions)
                    .Include(eq => eq.Question.Subject)
                    .Where(eq => eq.ExamId == examId)
                    .OrderBy(eq => eq.OrderIndex)
                    .ThenBy(eq => eq.QuestionId)
                    .ToList();

                var result = new List<Question>();
                int idx = 1;
                foreach (var eq in examQuestions)
                {
                    if (eq.Question != null)
                    {
                        result.Add(MapQuestionEntityToDto(eq.Question, examId, idx++));
                    }
                }
                return result;
            }
        }

        private static Question MapQuestionEntityToDto(QuizzApp.DAL.Entities.Question entity, int examId, int index)
        {
            var meta = GetMeta();
            var qm = meta.Questions.FirstOrDefault(x => x.QuestionId == entity.Id);
            var opts = (entity.QuestionOptions ?? new List<QuizzApp.DAL.Entities.QuestionOption>())
                .OrderBy(o => o.Id)
                .ToList();

            string optA = opts.Count > 0 ? opts[0].OptionContent : "";
            string optB = opts.Count > 1 ? opts[1].OptionContent : "";
            string optC = opts.Count > 2 ? opts[2].OptionContent : "";
            string optD = opts.Count > 3 ? opts[3].OptionContent : "";

            var correctLetters = new List<string>();
            if (opts.Count > 0 && opts[0].IsCorrect) correctLetters.Add("A");
            if (opts.Count > 1 && opts[1].IsCorrect) correctLetters.Add("B");
            if (opts.Count > 2 && opts[2].IsCorrect) correctLetters.Add("C");
            if (opts.Count > 3 && opts[3].IsCorrect) correctLetters.Add("D");
            if (correctLetters.Count == 0) correctLetters.Add("A");

            string correctStr = string.Join(",", correctLetters);
            int chapId = qm != null && qm.ChapterId > 0 ? qm.ChapterId : 1;
            var chapObj = meta.Chapters.FirstOrDefault(c => c.Id == chapId)
                          ?? meta.Chapters.FirstOrDefault(c => c.SubjectId == entity.SubjectId);

            return new Question
            {
                Id = entity.Id,
                ExamId = examId,
                SubjectId = entity.SubjectId,
                SubjectName = entity.Subject != null ? entity.Subject.SubjectName : "Chung",
                ChapterId = chapObj != null ? chapObj.Id : 1,
                ChapterName = chapObj != null ? chapObj.Name : "Chương 1",
                Index = index,
                QuestionText = entity.Content,
                OptionA = optA,
                OptionB = optB,
                OptionC = optC,
                OptionD = optD,
                CorrectOption = correctStr,
                Difficulty = MapIntToDifficulty(entity.DifficultyLevel),
                QuestionType = correctLetters.Count > 1 ? "MultipleChoice" : "SingleChoice",
                Explanation = qm != null ? qm.Explanation : ""
            };
        }

        public static void AddQuestion(Question q)
        {
            using (var db = new QuizzDbContext())
            {
                int subjectId = q.SubjectId > 0 ? q.SubjectId : db.Subjects.Select(s => s.Id).FirstOrDefault();
                var entity = new QuizzApp.DAL.Entities.Question
                {
                    Content = q.QuestionText,
                    DifficultyLevel = MapDifficultyToInt(q.Difficulty),
                    SubjectId = subjectId,
                    QuestionOptions = new List<QuizzApp.DAL.Entities.QuestionOption>
                    {
                        new QuizzApp.DAL.Entities.QuestionOption { OptionContent = q.OptionA ?? "", IsCorrect = q.IsOptionCorrect("A") },
                        new QuizzApp.DAL.Entities.QuestionOption { OptionContent = q.OptionB ?? "", IsCorrect = q.IsOptionCorrect("B") },
                        new QuizzApp.DAL.Entities.QuestionOption { OptionContent = string.IsNullOrWhiteSpace(q.OptionC) ? " " : q.OptionC, IsCorrect = q.IsOptionCorrect("C") },
                        new QuizzApp.DAL.Entities.QuestionOption { OptionContent = string.IsNullOrWhiteSpace(q.OptionD) ? " " : q.OptionD, IsCorrect = q.IsOptionCorrect("D") }
                    }
                };
                db.Questions.Add(entity);
                db.SaveChanges();

                if (q.ExamId > 0)
                {
                    int nextOrder = db.ExamQuestions.Count(eq => eq.ExamId == q.ExamId) + 1;
                    db.ExamQuestions.Add(new QuizzApp.DAL.Entities.ExamQuestion
                    {
                        ExamId = q.ExamId,
                        QuestionId = entity.Id,
                        OrderIndex = nextOrder
                    });
                    db.SaveChanges();
                }

                SetQuestionMeta(entity.Id, q.ChapterId, q.Explanation, db);
                SaveMeta();
            }
        }

        public static void UpdateQuestion(Question q)
        {
            using (var db = new QuizzDbContext())
            {
                var entity = db.Questions
                    .Include(x => x.QuestionOptions)
                    .FirstOrDefault(x => x.Id == q.Id);

                if (entity != null)
                {
                    entity.Content = q.QuestionText;
                    entity.DifficultyLevel = MapDifficultyToInt(q.Difficulty);
                    if (q.SubjectId > 0) entity.SubjectId = q.SubjectId;

                    var opts = entity.QuestionOptions.OrderBy(o => o.Id).ToList();
                    while (opts.Count < 4)
                    {
                        var newOpt = new QuizzApp.DAL.Entities.QuestionOption { QuestionId = entity.Id, OptionContent = " ", IsCorrect = false };
                        db.QuestionOptions.Add(newOpt);
                        opts.Add(newOpt);
                    }

                    opts[0].OptionContent = q.OptionA ?? "";
                    opts[0].IsCorrect = q.IsOptionCorrect("A");

                    opts[1].OptionContent = q.OptionB ?? "";
                    opts[1].IsCorrect = q.IsOptionCorrect("B");

                    opts[2].OptionContent = string.IsNullOrWhiteSpace(q.OptionC) ? " " : q.OptionC;
                    opts[2].IsCorrect = q.IsOptionCorrect("C");

                    opts[3].OptionContent = string.IsNullOrWhiteSpace(q.OptionD) ? " " : q.OptionD;
                    opts[3].IsCorrect = q.IsOptionCorrect("D");

                    db.SaveChanges();
                }

                SetQuestionMeta(q.Id, q.ChapterId, q.Explanation, db);
                SaveMeta();
            }
        }

        public static void DeleteQuestion(int questionId)
        {
            using (var db = new QuizzDbContext())
            {
                DeleteQuestionInternal(db, questionId);
                db.SaveChanges();
            }
        }

        private static void DeleteQuestionInternal(QuizzDbContext db, int questionId)
        {
            var details = db.AttemptDetails.Where(ad => ad.QuestionId == questionId).ToList();
            db.AttemptDetails.RemoveRange(details);

            var eqs = db.ExamQuestions.Where(eq => eq.QuestionId == questionId).ToList();
            db.ExamQuestions.RemoveRange(eqs);

            var opts = db.QuestionOptions.Where(o => o.QuestionId == questionId).ToList();
            db.QuestionOptions.RemoveRange(opts);

            var q = db.Questions.FirstOrDefault(x => x.Id == questionId);
            if (q != null)
            {
                db.Questions.Remove(q);
            }

            var meta = GetMeta();
            meta.Questions.RemoveAll(x => x.QuestionId == questionId);
            DeleteMetaRow("Question", questionId.ToString(), db);
            SaveMeta();
        }

        public static int GetTotalQuestionCount()
        {
            using (var db = new QuizzDbContext())
            {
                return db.Questions.Count();
            }
        }

        public static int ImportQuestionsFromCsv(string filePath, int defaultExamId, int defaultSubjectId, int defaultChapterId, out string reportMessage)
        {
            int imported = 0;
            int errors = 0;
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (i == 0 && (line.StartsWith("QuestionText", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Nội dung", StringComparison.OrdinalIgnoreCase)))
                    continue;

                string[] cols = SplitCsvLine(line);
                if (cols.Length < 6)
                {
                    errors++;
                    continue;
                }

                string qText = cols[0].Trim();
                string optA = cols[1].Trim();
                string optB = cols[2].Trim();
                string optC = cols[3].Trim();
                string optD = cols[4].Trim();
                string correct = cols[5].Trim().ToUpperInvariant();
                string diff = cols.Length > 6 && !string.IsNullOrWhiteSpace(cols[6]) ? cols[6].Trim() : "Easy";
                string expl = cols.Length > 7 ? cols[7].Trim() : "";

                if (string.IsNullOrEmpty(qText) || string.IsNullOrEmpty(optA) || string.IsNullOrEmpty(optB))
                {
                    errors++;
                    continue;
                }

                AddQuestion(new Question
                {
                    ExamId = defaultExamId,
                    SubjectId = defaultSubjectId,
                    ChapterId = defaultChapterId,
                    QuestionText = qText,
                    OptionA = optA,
                    OptionB = optB,
                    OptionC = optC,
                    OptionD = optD,
                    CorrectOption = correct,
                    Difficulty = diff,
                    Explanation = expl
                });
                imported++;
            }

            reportMessage = string.Format("Đã nhập thành công {0} câu hỏi từ file CSV{1}",
                imported,
                errors > 0 ? string.Format(" ({0} dòng bỏ qua do thiếu cột).", errors) : ".");
            return imported;
        }

        public static void ExportQuestionsToCsv(string filePath, List<Question> questions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Nội dung câu hỏi,Đáp án A,Đáp án B,Đáp án C,Đáp án D,Đáp án đúng,Độ khó,Giải thích");
            foreach (var q in questions)
            {
                sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                    EscapeCsv(q.QuestionText),
                    EscapeCsv(q.OptionA),
                    EscapeCsv(q.OptionB),
                    EscapeCsv(q.OptionC),
                    EscapeCsv(q.OptionD),
                    EscapeCsv(q.CorrectOption),
                    EscapeCsv(q.Difficulty),
                    EscapeCsv(q.Explanation)));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
            {
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            }
            return val;
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // ==================== AUTO-SAVE ATTEMPTS & RECOVERY ====================

        public static AttemptRecord StartNewAttempt(Exam exam)
        {
            using (var db = new QuizzDbContext())
            {
                int uid = SessionManager.CurrentUser != null ? SessionManager.CurrentUser.Id : 0;
                var userEntity = db.Users.FirstOrDefault(u => u.Id == uid) ?? db.Users.FirstOrDefault();
                int validUserId = userEntity != null ? userEntity.Id : 1;
                string studentName = userEntity != null ? userEntity.FullName : "Học sinh";

                DateTime nowLocal = DateTime.Now;
                int seed = Guid.NewGuid().GetHashCode();

                var attemptEntity = new QuizzApp.DAL.Entities.ExamAttempt
                {
                    ExamId = exam.Id,
                    UserId = validUserId,
                    StartTime = nowLocal,
                    SubmitTime = null,
                    TotalScore = 0,
                    CorrectAnswersCount = 0
                };
                db.ExamAttempts.Add(attemptEntity);
                db.SaveChanges();

                var attemptMeta = new LocalAttemptMeta
                {
                    AttemptId = attemptEntity.Id,
                    RandomSeed = seed,
                    Status = "InProgress"
                };

                var meta = GetMeta();
                meta.Attempts.RemoveAll(x => x.AttemptId == attemptEntity.Id);
                meta.Attempts.Add(attemptMeta);
                UpsertMetaRow("Attempt", attemptEntity.Id.ToString(), attemptMeta, db);
                SaveMeta();

                return new AttemptRecord
                {
                    Id = attemptEntity.Id,
                    ExamId = exam.Id,
                    ExamTitle = exam.Title,
                    UserId = validUserId,
                    StudentName = studentName,
                    StartedAtUtc = DateTime.SpecifyKind(nowLocal, DateTimeKind.Local).ToUniversalTime(),
                    Status = "InProgress",
                    RandomSeed = seed
                };
            }
        }

        public static AttemptRecord GetActiveAttemptForUser(int userId)
        {
            if (userId <= 0) return null;

            using (var db = new QuizzDbContext())
            {
                var active = db.ExamAttempts
                    .Include(a => a.Exam)
                    .Include(a => a.User)
                    .Where(a => a.UserId == userId && a.SubmitTime == null)
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefault();

                if (active != null && active.Exam != null)
                {
                    DateTime startedUtc = DateTime.SpecifyKind(active.StartTime, DateTimeKind.Local).ToUniversalTime();
                    TimeSpan remaining = startedUtc.AddMinutes(active.Exam.DurationMinutes) - DateTime.UtcNow;
                    if (remaining.TotalSeconds > 5)
                    {
                        var meta = GetMeta();
                        var am = meta.Attempts.FirstOrDefault(x => x.AttemptId == active.Id);
                        return new AttemptRecord
                        {
                            Id = active.Id,
                            ExamId = active.ExamId,
                            ExamTitle = active.Exam.Title,
                            UserId = active.UserId,
                            StudentName = active.User != null ? active.User.FullName : "Học sinh",
                            StartedAtUtc = startedUtc,
                            Status = "InProgress",
                            RandomSeed = am != null ? am.RandomSeed : active.Id
                        };
                    }
                }
            }
            return null;
        }

        public static void SaveAttemptAnswerProgress(int attemptId, int questionId, string selectedOptions, bool isFlagged)
        {
            if (attemptId <= 0) return;

            var ansMeta = new LocalAttemptAnswerMeta
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                SelectedOptions = selectedOptions ?? "",
                IsFlagged = isFlagged
            };

            using (var db = new QuizzDbContext())
            {
                var q = db.Questions
                    .Include(x => x.QuestionOptions)
                    .FirstOrDefault(x => x.Id == questionId);

                int? selectedOptionId = null;
                if (q != null && !string.IsNullOrWhiteSpace(selectedOptions))
                {
                    var opts = q.QuestionOptions.OrderBy(o => o.Id).ToList();
                    string firstKey = selectedOptions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(s => s.Trim().ToUpperInvariant())
                                                     .FirstOrDefault();
                    if (firstKey == "A" && opts.Count > 0) selectedOptionId = opts[0].Id;
                    else if (firstKey == "B" && opts.Count > 1) selectedOptionId = opts[1].Id;
                    else if (firstKey == "C" && opts.Count > 2) selectedOptionId = opts[2].Id;
                    else if (firstKey == "D" && opts.Count > 3) selectedOptionId = opts[3].Id;
                }

                var detail = db.AttemptDetails.FirstOrDefault(ad => ad.ExamAttemptId == attemptId && ad.QuestionId == questionId);
                if (detail == null)
                {
                    detail = new QuizzApp.DAL.Entities.AttemptDetail
                    {
                        ExamAttemptId = attemptId,
                        QuestionId = questionId,
                        SelectedOptionId = selectedOptionId
                    };
                    db.AttemptDetails.Add(detail);
                }
                else
                {
                    detail.SelectedOptionId = selectedOptionId;
                }
                db.SaveChanges();

                // Lưu nguyên tử từng câu trả lời vào MySQL (không ghi đè bài làm của người dùng khác)
                UpsertMetaRow("Answer", string.Format("{0}_{1}", attemptId, questionId), ansMeta, db);
            }

            lock (MetaLock)
            {
                if (_metaCache != null)
                {
                    var existing = _metaCache.AttemptAnswers.FirstOrDefault(x => x.AttemptId == attemptId && x.QuestionId == questionId);
                    if (existing == null)
                    {
                        _metaCache.AttemptAnswers.Add(ansMeta);
                    }
                    else
                    {
                        existing.SelectedOptions = ansMeta.SelectedOptions;
                        existing.IsFlagged = ansMeta.IsFlagged;
                    }
                }
            }
        }

        public static Dictionary<int, AttemptAnswerRecord> GetSavedAnswersForAttempt(int attemptId)
        {
            var dict = new Dictionary<int, AttemptAnswerRecord>();
            if (attemptId <= 0) return dict;

            using (var db = new QuizzDbContext())
            {
                var details = db.AttemptDetails
                    .Include(ad => ad.Question.QuestionOptions)
                    .Where(ad => ad.ExamAttemptId == attemptId)
                    .ToList();

                var meta = GetMeta(true);
                foreach (var d in details)
                {
                    string selectedKey = "";
                    if (d.SelectedOptionId.HasValue && d.Question != null && d.Question.QuestionOptions != null)
                    {
                        var opts = d.Question.QuestionOptions.OrderBy(o => o.Id).ToList();
                        for (int i = 0; i < opts.Count; i++)
                        {
                            if (opts[i].Id == d.SelectedOptionId.Value)
                            {
                                selectedKey = ((char)('A' + i)).ToString();
                                break;
                            }
                        }
                    }

                    var am = meta.AttemptAnswers.FirstOrDefault(x => x.AttemptId == attemptId && x.QuestionId == d.QuestionId);
                    dict[d.QuestionId] = new AttemptAnswerRecord
                    {
                        AttemptId = attemptId,
                        QuestionId = d.QuestionId,
                        SelectedOptions = am != null && !string.IsNullOrEmpty(am.SelectedOptions) ? am.SelectedOptions : selectedKey,
                        IsFlagged = am != null && am.IsFlagged
                    };
                }
            }
            return dict;
        }

        public static void AbandonInProgressAttempt(int attemptId)
        {
            if (attemptId <= 0) return;

            using (var db = new QuizzDbContext())
            {
                var details = db.AttemptDetails.Where(ad => ad.QuestionId > 0 && ad.ExamAttemptId == attemptId).ToList();
                db.AttemptDetails.RemoveRange(details);

                var att = db.ExamAttempts.FirstOrDefault(a => a.Id == attemptId);
                if (att != null)
                {
                    db.ExamAttempts.Remove(att);
                }
                db.SaveChanges();

                DeleteMetaRow("Attempt", attemptId.ToString(), db);
                DeleteMetaByKeyPrefix("Answer", attemptId.ToString() + "_", db);
            }

            var meta = GetMeta();
            meta.Attempts.RemoveAll(x => x.AttemptId == attemptId);
            meta.AttemptAnswers.RemoveAll(x => x.AttemptId == attemptId);
            SaveMeta();
        }

        public static void CompleteAttemptAndSaveHistory(int attemptId, int examId, string examTitle, double score, int correctCount, int totalQuestions, int durationSeconds, bool isAutoSubmitted)
        {
            string statusStr = isAutoSubmitted ? "AutoSubmitted" : "Submitted";

            using (var db = new QuizzDbContext())
            {
                var att = db.ExamAttempts.FirstOrDefault(a => a.Id == attemptId);
                if (att != null)
                {
                    att.SubmitTime = DateTime.Now;
                    att.TotalScore = Math.Round(score, 1);
                    att.CorrectAnswersCount = correctCount;
                    db.SaveChanges();
                }

                var meta = GetMeta();
                var am = meta.Attempts.FirstOrDefault(x => x.AttemptId == attemptId);
                if (am == null)
                {
                    am = new LocalAttemptMeta { AttemptId = attemptId, RandomSeed = attemptId };
                    meta.Attempts.Add(am);
                }
                am.Status = statusStr;
                UpsertMetaRow("Attempt", attemptId.ToString(), am, db);
                SaveMeta();
            }
        }

        // ==================== HISTORY & ANALYTICS ====================

        public static List<ExamHistoryItem> GetExamHistory(int? filterUserId = null)
        {
            using (var db = new QuizzDbContext())
            {
                var query = db.ExamAttempts
                    .Include(a => a.Exam.ExamQuestions)
                    .Include(a => a.User)
                    .Where(a => a.SubmitTime != null);

                if (filterUserId.HasValue && filterUserId.Value > 0)
                {
                    int uid = filterUserId.Value;
                    query = query.Where(a => a.UserId == uid);
                }

                var attempts = query.OrderByDescending(a => a.Id).ToList();
                var meta = GetMeta();
                var list = new List<ExamHistoryItem>();

                foreach (var a in attempts)
                {
                    var am = meta.Attempts.FirstOrDefault(x => x.AttemptId == a.Id);
                    int totalQ = a.Exam != null && a.Exam.ExamQuestions != null ? a.Exam.ExamQuestions.Count : 0;
                    int durSecs = a.SubmitTime.HasValue
                        ? Math.Max(1, (int)(a.SubmitTime.Value - a.StartTime).TotalSeconds)
                        : 0;

                    list.Add(new ExamHistoryItem
                    {
                        Id = a.Id,
                        UserId = a.UserId,
                        StudentName = a.User != null ? a.User.FullName : "Học sinh",
                        ExamId = a.ExamId,
                        ExamTitle = a.Exam != null ? a.Exam.Title : "Đề thi",
                        Score = Math.Round(a.TotalScore, 1),
                        CorrectCount = a.CorrectAnswersCount,
                        TotalQuestions = totalQ,
                        DurationSeconds = durSecs,
                        CompletedAt = (a.SubmitTime ?? a.StartTime).ToString("dd/MM/yyyy HH:mm"),
                        Status = am != null && !string.IsNullOrEmpty(am.Status) ? am.Status : "Submitted"
                    });
                }

                return list;
            }
        }

        public static List<WrongQuestionStat> GetTopWrongQuestions(int limit = 8)
        {
            using (var db = new QuizzDbContext())
            {
                var details = db.AttemptDetails
                    .Include(ad => ad.Question.QuestionOptions)
                    .Include(ad => ad.ExamAttempt.Exam)
                    .Where(ad => ad.ExamAttempt.SubmitTime != null)
                    .ToList();

                var statsMap = new Dictionary<int, WrongQuestionStat>();

                foreach (var ad in details)
                {
                    if (ad.Question == null) continue;
                    int qid = ad.Question.Id;

                    bool isCorrect = false;
                    if (ad.SelectedOptionId.HasValue && ad.Question.QuestionOptions != null)
                    {
                        var selectedOpt = ad.Question.QuestionOptions.FirstOrDefault(o => o.Id == ad.SelectedOptionId.Value);
                        if (selectedOpt != null && selectedOpt.IsCorrect)
                        {
                            isCorrect = true;
                        }
                    }

                    if (!statsMap.ContainsKey(qid))
                    {
                        string diffStr = MapIntToDifficulty(ad.Question.DifficultyLevel);
                        var tempQ = new Question { Difficulty = diffStr };
                        statsMap[qid] = new WrongQuestionStat
                        {
                            QuestionId = qid,
                            QuestionText = ad.Question.Content,
                            ExamTitle = ad.ExamAttempt != null && ad.ExamAttempt.Exam != null ? ad.ExamAttempt.Exam.Title : "Đề thi",
                            DifficultyDisplay = tempQ.DifficultyDisplay,
                            WrongCount = 0,
                            TotalAttempts = 0
                        };
                    }

                    statsMap[qid].TotalAttempts++;
                    if (!isCorrect)
                    {
                        statsMap[qid].WrongCount++;
                    }
                }

                return statsMap.Values
                    .Where(x => x.WrongCount > 0)
                    .OrderByDescending(x => x.WrongCount)
                    .Take(limit)
                    .ToList();
            }
        }

        public static void ExportHistoryToCsv(string filePath, List<ExamHistoryItem> history)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mã lượt,Học sinh,Tên đề thi,Điểm số,Số câu đúng,Tổng số câu,Thời gian (giây),Ngày hoàn thành,Trạng thái");
            foreach (var h in history)
            {
                sb.AppendLine(string.Format("{0},{1},{2},{3:0.0},{4},{5},{6},{7},{8}",
                    h.Id,
                    EscapeCsv(h.StudentName),
                    EscapeCsv(h.ExamTitle),
                    h.Score,
                    h.CorrectCount,
                    h.TotalQuestions,
                    h.DurationSeconds,
                    EscapeCsv(h.CompletedAt),
                    EscapeCsv(h.StatusTypeText)));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public static void ClearExamHistory(int? userId = null)
        {
            using (var db = new QuizzDbContext())
            {
                if (userId.HasValue && userId.Value > 0)
                {
                    int uid = userId.Value;
                    var userAttempts = db.ExamAttempts
                        .Include(a => a.AttemptDetails)
                        .Where(a => a.UserId == uid && a.SubmitTime != null)
                        .ToList();

                    var meta = GetMeta();
                    foreach (var att in userAttempts)
                    {
                        db.AttemptDetails.RemoveRange(att.AttemptDetails);
                        DeleteMetaRow("Attempt", att.Id.ToString(), db);
                        DeleteMetaByKeyPrefix("Answer", att.Id.ToString() + "_", db);
                        meta.Attempts.RemoveAll(x => x.AttemptId == att.Id);
                        meta.AttemptAnswers.RemoveAll(x => x.AttemptId == att.Id);
                    }
                    db.ExamAttempts.RemoveRange(userAttempts);
                    db.SaveChanges();
                    SaveMeta();
                }
                else
                {
                    var allDetails = db.AttemptDetails.ToList();
                    db.AttemptDetails.RemoveRange(allDetails);

                    var allAttempts = db.ExamAttempts.ToList();
                    db.ExamAttempts.RemoveRange(allAttempts);

                    db.SaveChanges();

                    EnsureMetaTableExists(db);
                    db.Database.ExecuteSqlCommand("DELETE FROM `QuizzAppMetaItems` WHERE `Category` IN ('Attempt', 'Answer');");

                    var meta = GetMeta();
                    meta.Attempts.Clear();
                    meta.AttemptAnswers.Clear();
                    SaveMeta();
                }
            }
        }

        // ==================== BACKUP & RESTORE ====================

        public static void BackupDatabase(string destinationPath)
        {
            var latestMeta = GetMeta(true);
            SaveMetaToLocalFile(latestMeta);
            if (File.Exists(MetaFilePath))
            {
                File.Copy(MetaFilePath, destinationPath, true);
            }
            else
            {
                File.WriteAllText(destinationPath, "QuizzApp MySQL Backup Metadata", Encoding.UTF8);
            }
        }

        public static void RestoreDatabase(string sourcePath)
        {
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, MetaFilePath, true);
                var restoredStore = LoadMetaFromLocalFile();
                using (var db = new QuizzDbContext())
                {
                    PushStoreToMySql(db, restoredStore);
                }
                lock (MetaLock)
                {
                    _metaCache = restoredStore;
                    _lastMetaFetchUtc = DateTime.UtcNow;
                }
            }
            InitializeDatabase();
        }
    }
}
