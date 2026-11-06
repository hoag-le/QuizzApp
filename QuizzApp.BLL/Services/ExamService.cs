using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Quản lý Đề thi (Exam), lưu đề yêu thích và tạo đề thi tự động theo ma trận độ khó.
    /// </summary>
    public static class ExamService
    {
        public static List<Exam> GetAllExams()
        {
            using (var db = new QuizzDbContext())
            {
                var exams = db.Exams
                    .Include(e => e.Subject)
                    .Include(e => e.ExamQuestions)
                    .OrderBy(e => e.Id)
                    .ToList();

                var result = new List<Exam>();
                foreach (var e in exams)
                {
                    string subjName = e.Subject != null ? e.Subject.SubjectName : "Chung";
                    string modeStr = !string.IsNullOrEmpty(e.Mode) ? e.Mode : "Real";
                    bool isPractice = string.Equals(modeStr, "Practice", StringComparison.OrdinalIgnoreCase);
                    result.Add(new Exam
                    {
                        Id = e.Id,
                        Code = !string.IsNullOrEmpty(e.Code) ? e.Code : (e.Subject != null ? e.Subject.SubjectCode : "QUIZ"),
                        Title = e.Title,
                        Description = !string.IsNullOrEmpty(e.Description)
                            ? e.Description
                            : string.Format("Đề kiểm tra trắc nghiệm môn {0}", subjName),
                        SubjectId = e.SubjectId,
                        SubjectName = subjName,
                        DurationMinutes = e.DurationMinutes,
                        ShuffleQuestions = e.ShuffleQuestions,
                        ShuffleAnswers = e.ShuffleAnswers,
                        Mode = modeStr,
                        ExamStartTime = e.ExamStartTime,
                        IsManuallyOpened = e.IsManuallyOpened,
                        IsSaved = isPractice && e.IsSaved,
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
                    "• Câu Dễ khả dụng: {0} / Yêu cầu: {1}\n" +
                    "• Câu Trung bình khả dụng: {2} / Yêu cầu: {3}\n" +
                    "• Câu Khó khả dụng: {4} / Yêu cầu: {5}",
                    easyAvail, easyReq, medAvail, mediumReq, hardAvail, hardReq);
                return false;
            }

            using (var db = new QuizzDbContext())
            {
                var newExam = new QuizzApp.DAL.Entities.Exam
                {
                    Code = exam.Code ?? "QUIZ",
                    Title = exam.Title,
                    Description = exam.Description ?? "",
                    SubjectId = exam.SubjectId,
                    DurationMinutes = exam.DurationMinutes,
                    ShuffleQuestions = exam.ShuffleQuestions,
                    ShuffleAnswers = exam.ShuffleAnswers,
                    Mode = exam.Mode ?? "Real",
                    ExamStartTime = exam.ExamStartTime,
                    IsManuallyOpened = exam.IsManuallyOpened,
                    IsSaved = false,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                db.Exams.Add(newExam);
                db.SaveChanges();

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

                message = string.Format("Đã tạo đề thi '{0}' thành công với {1} câu hỏi theo ma trận!",
                    exam.Title, pickedIds.Count);
                return true;
            }
        }

        public static int AddExam(Exam exam)
        {
            using (var db = new QuizzDbContext())
            {
                int targetSubjectId = exam.SubjectId > 0 ? exam.SubjectId : db.Subjects.Select(s => s.Id).FirstOrDefault();
                string modeStr = exam.Mode ?? "Real";
                bool isPractice = string.Equals(modeStr, "Practice", StringComparison.OrdinalIgnoreCase);
                var entity = new QuizzApp.DAL.Entities.Exam
                {
                    Code = exam.Code ?? "QUIZ",
                    Title = exam.Title,
                    Description = exam.Description ?? "",
                    SubjectId = targetSubjectId,
                    DurationMinutes = exam.DurationMinutes,
                    ShuffleQuestions = exam.ShuffleQuestions,
                    ShuffleAnswers = exam.ShuffleAnswers,
                    Mode = modeStr,
                    ExamStartTime = isPractice ? null : exam.ExamStartTime,
                    IsManuallyOpened = isPractice ? true : exam.IsManuallyOpened,
                    IsSaved = isPractice && exam.IsSaved,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                db.Exams.Add(entity);
                db.SaveChanges();

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
                    string modeStr = exam.Mode ?? "Real";
                    bool isPractice = string.Equals(modeStr, "Practice", StringComparison.OrdinalIgnoreCase);
                    entity.Code = exam.Code ?? "QUIZ";
                    entity.Title = exam.Title;
                    entity.Description = exam.Description ?? "";
                    entity.SubjectId = exam.SubjectId > 0 ? exam.SubjectId : entity.SubjectId;
                    entity.DurationMinutes = exam.DurationMinutes;
                    entity.ShuffleQuestions = exam.ShuffleQuestions;
                    entity.ShuffleAnswers = exam.ShuffleAnswers;
                    entity.Mode = modeStr;
                    entity.ExamStartTime = isPractice ? null : exam.ExamStartTime;
                    entity.IsManuallyOpened = isPractice ? true : exam.IsManuallyOpened;
                    entity.IsSaved = isPractice && exam.IsSaved;

                    // Đồng bộ môn học cho các câu hỏi thuộc đề thi
                    var qIds = entity.ExamQuestions.Select(eq => eq.QuestionId).ToList();
                    var linkedQuestions = db.Questions.Where(q => qIds.Contains(q.Id)).ToList();
                    foreach (var q in linkedQuestions)
                    {
                        q.SubjectId = entity.SubjectId;
                    }

                    db.SaveChanges();
                }
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

        internal static void DeleteExamInternal(QuizzDbContext db, int examId)
        {
            var attempts = db.ExamAttempts.Include(a => a.AttemptDetails).Where(a => a.ExamId == examId).ToList();
            foreach (var att in attempts)
            {
                db.AttemptDetails.RemoveRange(att.AttemptDetails);
            }
            db.ExamAttempts.RemoveRange(attempts);

            var eqs = db.ExamQuestions.Where(eq => eq.ExamId == examId).ToList();
            db.ExamQuestions.RemoveRange(eqs);

            var exam = db.Exams.FirstOrDefault(e => e.Id == examId);
            if (exam != null)
            {
                db.Exams.Remove(exam);
            }
        }

        public static void ToggleSaveExam(int examId, bool isSaved)
        {
            using (var db = new QuizzDbContext())
            {
                var exam = db.Exams.FirstOrDefault(e => e.Id == examId);
                if (exam != null)
                {
                    bool isPractice = string.Equals(exam.Mode, "Practice", StringComparison.OrdinalIgnoreCase);
                    exam.IsSaved = isPractice && isSaved;
                    db.SaveChanges();
                }
            }
        }

        public static void ToggleManualOpenExam(int examId, bool isManuallyOpened)
        {
            using (var db = new QuizzDbContext())
            {
                var exam = db.Exams.FirstOrDefault(e => e.Id == examId);
                if (exam != null)
                {
                    exam.IsManuallyOpened = isManuallyOpened;
                    db.SaveChanges();
                }
            }
        }
    }
}
