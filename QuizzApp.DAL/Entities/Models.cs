using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizzApp.DAL.Entities
{
    // 1. Bảng Người dùng (Admin, Teacher, Student)
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; }

        [Required, StringLength(255)]
        public string PasswordHash { get; set; }

        [Required, StringLength(100)]
        public string FullName { get; set; }

        [Required, StringLength(20)]
        public string Role { get; set; } = "Student"; // "Admin", "Teacher", "Student"

        public virtual ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();
    }

    // 2. Bảng Môn học
    public class Subject
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string SubjectCode { get; set; }

        [Required, StringLength(150)]
        public string SubjectName { get; set; }

        public virtual ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
        public virtual ICollection<Exam> Exams { get; set; } = new List<Exam>();
    }

    // 3. Bảng Chương học
    public class Chapter
    {
        [Key]
        public int Id { get; set; }

        public int SubjectId { get; set; }
        [ForeignKey("SubjectId")]
        public virtual Subject Subject { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = "";
    }

    // 4. Bảng Ngân hàng câu hỏi
    public class Question
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Content { get; set; }

        public int DifficultyLevel { get; set; } = 1; // 1: Dễ, 2: Trung bình, 3: Khó

        public int SubjectId { get; set; }
        [ForeignKey("SubjectId")]
        public virtual Subject Subject { get; set; }

        public int ChapterId { get; set; } = 1;

        public string Explanation { get; set; } = "";

        public virtual ICollection<QuestionOption> QuestionOptions { get; set; } = new List<QuestionOption>();
        public virtual ICollection<ExamQuestion> ExamQuestions { get; set; } = new List<ExamQuestion>();
    }

    // 5. Bảng Các phương án lựa chọn (A, B, C, D)
    public class QuestionOption
    {
        [Key]
        public int Id { get; set; }

        public int QuestionId { get; set; }
        [ForeignKey("QuestionId")]
        public virtual Question Question { get; set; }

        [Required]
        public string OptionContent { get; set; }

        public bool IsCorrect { get; set; } = false;
    }

    // 6. Bảng Đề thi / Ca thi
    public class Exam
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string Code { get; set; } = "QUIZ";

        [Required, StringLength(200)]
        public string Title { get; set; }

        public string Description { get; set; } = "";

        public int SubjectId { get; set; }
        [ForeignKey("SubjectId")]
        public virtual Subject Subject { get; set; }

        public int DurationMinutes { get; set; } = 45;
        public bool ShuffleQuestions { get; set; } = true;
        public bool ShuffleAnswers { get; set; } = false;

        [StringLength(20)]
        public string Mode { get; set; } = "Real"; // "Real" hoặc "Practice"

        public bool IsSaved { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime? ExamStartTime { get; set; }
        public bool IsManuallyOpened { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<ExamQuestion> ExamQuestions { get; set; } = new List<ExamQuestion>();
        public virtual ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();
    }

    // 7. Bảng Trung gian: Đề thi gồm những câu hỏi nào
    public class ExamQuestion
    {
        [Key, Column(Order = 0)]
        public int ExamId { get; set; }
        [ForeignKey("ExamId")]
        public virtual Exam Exam { get; set; }

        [Key, Column(Order = 1)]
        public int QuestionId { get; set; }
        [ForeignKey("QuestionId")]
        public virtual Question Question { get; set; }

        public int OrderIndex { get; set; }
    }

    // 8. Bảng Lịch sử làm bài của Thí sinh
    public class ExamAttempt
    {
        [Key]
        public int Id { get; set; }

        public int ExamId { get; set; }
        [ForeignKey("ExamId")]
        public virtual Exam Exam { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? SubmitTime { get; set; }
        public double TotalScore { get; set; } = 0;
        public int CorrectAnswersCount { get; set; } = 0;
        public int RandomSeed { get; set; } = 0;

        [StringLength(30)]
        public string Status { get; set; } = "InProgress"; // "InProgress", "Submitted", "AutoSubmitted"

        public virtual ICollection<AttemptDetail> AttemptDetails { get; set; } = new List<AttemptDetail>();
    }

    // 9. Bảng Chi tiết bài làm
    public class AttemptDetail
    {
        [Key]
        public int Id { get; set; }

        public int ExamAttemptId { get; set; }
        [ForeignKey("ExamAttemptId")]
        public virtual ExamAttempt ExamAttempt { get; set; }

        public int QuestionId { get; set; }
        [ForeignKey("QuestionId")]
        public virtual Question Question { get; set; }

        public int? SelectedOptionId { get; set; }

        [StringLength(50)]
        public string SelectedOptions { get; set; } = "";

        public bool IsFlagged { get; set; } = false;
    }
}
