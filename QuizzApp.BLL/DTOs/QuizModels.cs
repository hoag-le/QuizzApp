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

        public static string Hash(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword)) return "";
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plainPassword + StaticSalt);
                byte[] hash = sha.ComputeHash(bytes);
                return "$QM256$" + Convert.ToBase64String(hash);
            }
        }

        public static bool Verify(string plainPassword, string storedPasswordOrHash)
        {
            if (string.IsNullOrEmpty(storedPasswordOrHash)) return false;
            if (storedPasswordOrHash.StartsWith("$QM256$"))
            {
                return string.Equals(Hash(plainPassword), storedPasswordOrHash, StringComparison.Ordinal);
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
                return IsAdmin ? "Giáo viên (Admin)" : "Học sinh (Thí sinh)";
            }
        }

        public string RoleBadgeBg
        {
            get { return IsAdmin ? "#DBEAFE" : "#DCFCE7"; }
        }

        public string RoleBadgeFg
        {
            get { return IsAdmin ? "#1D4ED8" : "#15803D"; }
        }

        public string UsernameDisplay
        {
            get { return "@" + Username; }
        }

        public string CreatedAtDisplay
        {
            get { return string.IsNullOrWhiteSpace(CreatedAt) ? "Hệ thống MySQL" : "Ngày tạo: " + CreatedAt; }
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
        public string IconBg { get; set; } = "#DBEAFE";
        public string IconFg { get; set; } = "#1D4ED8";

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
                    OnPropertyChanged(nameof(SaveButtonBg));
                    OnPropertyChanged(nameof(SaveButtonFg));
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

        public string ModeBadgeBg
        {
            get { return IsPracticeMode ? "#FEF3C7" : "#DBEAFE"; }
        }

        public string ModeBadgeFg
        {
            get { return IsPracticeMode ? "#B45309" : "#1D4ED8"; }
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

        public string SaveButtonBg
        {
            get { return IsSaved ? "#FEF3C7" : "#E2E8F0"; }
        }

        public string SaveButtonFg
        {
            get { return IsSaved ? "#B45309" : "#334155"; }
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

        public string DifficultyBg
        {
            get
            {
                if (string.Equals(Difficulty, "Hard", StringComparison.OrdinalIgnoreCase)) return "#FEE2E2";
                if (string.Equals(Difficulty, "Medium", StringComparison.OrdinalIgnoreCase)) return "#FEF3C7";
                return "#DCFCE7";
            }
        }

        public string DifficultyFg
        {
            get
            {
                if (string.Equals(Difficulty, "Hard", StringComparison.OrdinalIgnoreCase)) return "#B91C1C";
                if (string.Equals(Difficulty, "Medium", StringComparison.OrdinalIgnoreCase)) return "#B45309";
                return "#15803D";
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
                    ? "Tự nộp (Hết giờ)"
                    : "Đã nộp bài";
            }
        }

        public string StudentAndDateText
        {
            get
            {
                return string.IsNullOrEmpty(StudentName)
                    ? string.Format("{0} ({1})", CompletedAt, StatusTypeText)
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
            get { return Score >= 5.0 ? "Đạt" : "Chưa đạt"; }
        }

        public string StatusBadgeBg
        {
            get { return Score >= 5.0 ? "#DCFCE7" : "#FEE2E2"; }
        }

        public string StatusBadgeFg
        {
            get { return Score >= 5.0 ? "#15803D" : "#B91C1C"; }
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
                    ? string.Format("Sai {0}/{1} lượt ({2}%)", WrongCount, TotalAttempts, (WrongCount * 100 / TotalAttempts))
                    : string.Format("Sai {0} lượt", WrongCount);
            }
        }
    }
}
