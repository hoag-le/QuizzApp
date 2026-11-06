using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace QuizzApp.BLL
{
    public enum RoleType
    {
        Teacher,
        Student
    }

    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard
    }

    public enum QuestionType
    {
        SingleChoice,
        MultipleChoice
    }

    public enum AttemptStatus
    {
        InProgress,
        Submitted,
        AutoSubmitted
    }

    public enum ExamMode
    {
        Real,
        Practice
    }

    public static class PasswordHasher
    {
        private const string StaticSalt = "QuizMaster_v1_Salt_2026";
        private const string DynamicPrefix = "$QM256S$";
        private const string LegacyPrefix = "$QM256$";

        public static string Hash(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword)) return "";

            byte[] saltBytes = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(saltBytes);
            }
            string saltBase64 = Convert.ToBase64String(saltBytes);
            return ComputeDynamicSaltedHash(plainPassword, saltBase64);
        }

        private static string ComputeDynamicSaltedHash(string plainPassword, string saltBase64)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(saltBase64 + ":" + plainPassword);
                byte[] hash = sha.ComputeHash(bytes);
                return DynamicPrefix + saltBase64 + "$" + Convert.ToBase64String(hash);
            }
        }

        private static string ComputeLegacyStaticHash(string plainPassword)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plainPassword + StaticSalt);
                byte[] hash = sha.ComputeHash(bytes);
                return LegacyPrefix + Convert.ToBase64String(hash);
            }
        }

        public static bool IsDynamicSaltedHash(string storedPasswordOrHash)
        {
            return !string.IsNullOrEmpty(storedPasswordOrHash) &&
                   storedPasswordOrHash.StartsWith(DynamicPrefix, StringComparison.Ordinal);
        }

        public static bool Verify(string plainPassword, string storedPasswordOrHash)
        {
            if (string.IsNullOrEmpty(storedPasswordOrHash)) return false;

            if (storedPasswordOrHash.StartsWith(DynamicPrefix, StringComparison.Ordinal))
            {
                string payload = storedPasswordOrHash.Substring(DynamicPrefix.Length);
                int sepIdx = payload.IndexOf('$');
                if (sepIdx <= 0) return false;

                string saltBase64 = payload.Substring(0, sepIdx);
                string expected = ComputeDynamicSaltedHash(plainPassword ?? "", saltBase64);
                return string.Equals(expected, storedPasswordOrHash, StringComparison.Ordinal);
            }

            if (storedPasswordOrHash.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            {
                return string.Equals(ComputeLegacyStaticHash(plainPassword ?? ""), storedPasswordOrHash, StringComparison.Ordinal);
            }

            return string.Equals(plainPassword, storedPasswordOrHash, StringComparison.Ordinal);
        }
    }

    public class UserAccount
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; } // "Admin", "Teacher", "Student"
        public string CreatedAt { get; set; }

        public bool IsAdmin
        {
            get
            {
                return string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Role, "Teacher", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string RoleDisplay
        {
            get
            {
                return IsAdmin ? "Giáo viên" : "Học sinh";
            }
        }

        public string UsernameDisplay
        {
            get { return "@" + Username; }
        }

        public string CreatedAtDisplay
        {
            get { return string.IsNullOrWhiteSpace(CreatedAt) ? "" : "Ngày tạo: " + CreatedAt; }
        }
    }

    public static class SessionManager
    {
        public static UserAccount CurrentUser { get; set; }
    }

    public class Subject
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int ChapterCount { get; set; }
        public int QuestionCount { get; set; }

        public string SummaryText
        {
            get { return string.Format("{0} chương • {1} câu hỏi", ChapterCount, QuestionCount); }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class Chapter
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class Exam : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public int DurationMinutes { get; set; }
        public int QuestionCount { get; set; }
        public bool ShuffleQuestions { get; set; }
        public bool ShuffleAnswers { get; set; }
        public string Mode { get; set; } = "Real"; // "Real" or "Practice"
        public DateTime? ExamStartTime { get; set; }

        private bool _isManuallyOpened;
        public bool IsManuallyOpened
        {
            get { return _isManuallyOpened; }
            set
            {
                if (_isManuallyOpened != value)
                {
                    _isManuallyOpened = value;
                    OnPropertyChanged(nameof(IsManuallyOpened));
                    OnPropertyChanged(nameof(IsAvailableForStudent));
                    OnPropertyChanged(nameof(ScheduleDisplay));
                    OnPropertyChanged(nameof(ManualOpenButtonText));
                }
            }
        }

        private bool _isSaved;
        public bool IsSaved
        {
            get { return _isSaved; }
            set
            {
                if (_isSaved != value)
                {
                    _isSaved = value;
                    OnPropertyChanged(nameof(IsSaved));
                    OnPropertyChanged(nameof(SaveButtonText));
                }
            }
        }

        public bool IsPracticeMode
        {
            get { return string.Equals(Mode, "Practice", StringComparison.OrdinalIgnoreCase); }
        }

        public string ModeDisplay
        {
            get { return IsPracticeMode ? "Luyện tập" : "Thi thật"; }
        }

        public string SubjectDisplay
        {
            get { return string.IsNullOrEmpty(SubjectName) ? "Chung" : SubjectName; }
        }

        public string QuestionCountText
        {
            get { return string.Format("{0} câu hỏi", QuestionCount); }
        }

        public string DurationText
        {
            get { return string.Format("{0} phút", DurationMinutes); }
        }

        public string ShuffleInfoText
        {
            get
            {
                if (ShuffleQuestions && ShuffleAnswers) return "Trộn câu & đáp án";
                if (ShuffleQuestions) return "Trộn câu hỏi";
                if (ShuffleAnswers) return "Trộn đáp án";
                return "Cố định";
            }
        }

        public string SaveButtonText
        {
            get { return IsSaved ? "★ Đã lưu" : "☆ Lưu đề"; }
        }

        public bool IsAvailableForStudent
        {
            get
            {
                if (IsPracticeMode) return true;
                if (IsManuallyOpened) return true;
                if (ExamStartTime.HasValue)
                {
                    return DateTime.Now >= ExamStartTime.Value.AddMinutes(-5);
                }
                return false;
            }
        }

        public bool HasScheduledTime
        {
            get { return !IsPracticeMode && ExamStartTime.HasValue; }
        }

        public string ScheduledTimeBadgeText
        {
            get
            {
                if (!IsPracticeMode && ExamStartTime.HasValue)
                {
                    return string.Format("🕒 Giờ thi: {0:HH:mm dd/MM/yyyy}", ExamStartTime.Value);
                }
                return "";
            }
        }

        public string ScheduleDisplay
        {
            get
            {
                if (IsPracticeMode) return "Mở tự do";
                string timePart = ExamStartTime.HasValue
                    ? string.Format("Giờ thi: {0:HH:mm dd/MM/yyyy}", ExamStartTime.Value)
                    : "Chưa đặt giờ thi";
                if (IsManuallyOpened)
                {
                    return timePart + "  •  Đang mở thủ công";
                }
                if (ExamStartTime.HasValue && DateTime.Now >= ExamStartTime.Value.AddMinutes(-5))
                {
                    return timePart + "  •  Đã mở theo giờ";
                }
                return timePart + "  •  Chưa mở";
            }
        }

        public string ManualOpenButtonText
        {
            get { return IsManuallyOpened ? "🔒 Đóng mở thủ công" : "🔓 Mở đề thủ công"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public class Question : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public int ChapterId { get; set; }
        public string ChapterName { get; set; }
        public int Index { get; set; }
        public string QuestionText { get; set; }
        public string OptionA { get; set; }
        public string OptionB { get; set; }
        public string OptionC { get; set; }
        public string OptionD { get; set; }
        public string CorrectOption { get; set; } = "A";
        public string Difficulty { get; set; } = "Easy";
        public string QuestionType { get; set; } = "SingleChoice";
        public string Explanation { get; set; }

        public string DisplayKeyA { get; set; } = "A";
        public string DisplayKeyB { get; set; } = "B";
        public string DisplayKeyC { get; set; } = "C";
        public string DisplayKeyD { get; set; } = "D";

        private string _selectedOption = "";
        public string SelectedOption
        {
            get { return _selectedOption; }
            set
            {
                if (_selectedOption != value)
                {
                    _selectedOption = value ?? "";
                    OnPropertyChanged(nameof(SelectedOption));
                    OnPropertyChanged(nameof(IsAnswered));
                }
            }
        }

        private bool _isFlagged;
        public bool IsFlagged
        {
            get { return _isFlagged; }
            set
            {
                if (_isFlagged != value)
                {
                    _isFlagged = value;
                    OnPropertyChanged(nameof(IsFlagged));
                }
            }
        }

        public bool IsMultipleChoice
        {
            get
            {
                return string.Equals(QuestionType, "MultipleChoice", StringComparison.OrdinalIgnoreCase) ||
                       (CorrectOption != null && CorrectOption.Contains(","));
            }
        }

        public string DifficultyDisplay
        {
            get
            {
                if (string.Equals(Difficulty, "Hard", StringComparison.OrdinalIgnoreCase)) return "Khó";
                if (string.Equals(Difficulty, "Medium", StringComparison.OrdinalIgnoreCase)) return "Trung bình";
                return "Dễ";
            }
        }

        public string TypeDisplay
        {
            get { return IsMultipleChoice ? "Nhiều đáp án" : "1 đáp án"; }
        }

        public bool IsAnswered
        {
            get { return !string.IsNullOrWhiteSpace(SelectedOption); }
        }

        public bool IsOptionSelected(string originalKey)
        {
            if (string.IsNullOrWhiteSpace(SelectedOption)) return false;
            var parts = SelectedOption.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim().ToUpperInvariant());
            return parts.Contains(originalKey.ToUpperInvariant());
        }

        public bool IsOptionCorrect(string originalKey)
        {
            if (string.IsNullOrWhiteSpace(CorrectOption)) return false;
            var parts = CorrectOption.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim().ToUpperInvariant());
            return parts.Contains(originalKey.ToUpperInvariant());
        }

        public void ToggleSelectedOption(string originalKey)
        {
            originalKey = originalKey.Trim().ToUpperInvariant();
            if (!IsMultipleChoice)
            {
                SelectedOption = originalKey;
            }
            else
            {
                var set = new SortedSet<string>(
                    (SelectedOption ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(s => s.Trim().ToUpperInvariant()),
                    StringComparer.OrdinalIgnoreCase);

                if (set.Contains(originalKey))
                    set.Remove(originalKey);
                else
                    set.Add(originalKey);

                SelectedOption = string.Join(",", set);
            }
        }

        public bool IsCorrect
        {
            get
            {
                if (!IsAnswered || string.IsNullOrWhiteSpace(CorrectOption)) return false;
                var sel = SelectedOption.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim().ToUpperInvariant())
                                        .OrderBy(s => s);
                var cor = CorrectOption.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim().ToUpperInvariant())
                                       .OrderBy(s => s);
                return sel.SequenceEqual(cor);
            }
        }

        public string DisplayHeader
        {
            get { return string.Format("Câu {0} [{1}]: {2}", Index, DifficultyDisplay, QuestionText); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public class AttemptRecord
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public string ExamTitle { get; set; }
        public int UserId { get; set; }
        public string StudentName { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public double Score { get; set; }
        public int CorrectCount { get; set; }
        public int TotalQuestions { get; set; }
        public int DurationSeconds { get; set; }
        public string Status { get; set; }
        public int RandomSeed { get; set; }
    }

    public class AttemptAnswerRecord
    {
        public int AttemptId { get; set; }
        public int QuestionId { get; set; }
        public string SelectedOptions { get; set; }
        public bool IsFlagged { get; set; }
    }

    public class ExamHistoryItem
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string StudentName { get; set; }
        public int ExamId { get; set; }
        public string ExamTitle { get; set; }
        public double Score { get; set; }
        public int CorrectCount { get; set; }
        public int TotalQuestions { get; set; }
        public int DurationSeconds { get; set; }
        public string CompletedAt { get; set; }
        public string Status { get; set; } = "Submitted";

        public bool IsPassed
        {
            get { return Score >= 5.0; }
        }

        public string ScoreText
        {
            get { return string.Format("{0:0.0} / 10", Score); }
        }

        public string ResultRatioText
        {
            get { return string.Format("Đúng {0}/{1} câu", CorrectCount, TotalQuestions); }
        }

        public string StatusTypeText
        {
            get
            {
                return string.Equals(Status, "AutoSubmitted", StringComparison.OrdinalIgnoreCase)
                    ? "Tự động nộp"
                    : "Đã nộp bài";
            }
        }

        public string StudentAndDateText
        {
            get
            {
                return string.IsNullOrEmpty(StudentName)
                    ? string.Format("{0}  •  {1}", CompletedAt, StatusTypeText)
                    : string.Format("Học sinh: {0}  •  {1}  •  {2}", StudentName, CompletedAt, StatusTypeText);
            }
        }

        public string DurationFormatted
        {
            get
            {
                int mins = DurationSeconds / 60;
                int secs = DurationSeconds % 60;
                return string.Format("{0:D2}:{1:D2}", mins, secs);
            }
        }

        public string StatusBadgeText
        {
            get { return IsPassed ? "Đạt" : "Chưa đạt"; }
        }
    }

    public class WrongQuestionStat
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; }
        public string ExamTitle { get; set; }
        public string DifficultyDisplay { get; set; }
        public int WrongCount { get; set; }
        public int TotalAttempts { get; set; }

        public string WrongRateText
        {
            get
            {
                return TotalAttempts > 0
                    ? string.Format("Sai {0}/{1} lượt", WrongCount, TotalAttempts)
                    : string.Format("Sai {0} lượt", WrongCount);
            }
        }
    }
}
