using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Quản lý Lượt thi (ExamAttempt), tự động lưu tiến độ làm bài (Auto-save / Recovery) và thống kê kết quả.
    /// </summary>
    public static class AttemptService
    {
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
                    CorrectAnswersCount = 0,
                    RandomSeed = seed,
                    Status = "InProgress"
                };
                db.ExamAttempts.Add(attemptEntity);
                db.SaveChanges();

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
                        return new AttemptRecord
                        {
                            Id = active.Id,
                            ExamId = active.ExamId,
                            ExamTitle = active.Exam.Title,
                            UserId = active.UserId,
                            StudentName = active.User != null ? active.User.FullName : "Học sinh",
                            StartedAtUtc = startedUtc,
                            Status = !string.IsNullOrEmpty(active.Status) ? active.Status : "InProgress",
                            RandomSeed = active.RandomSeed != 0 ? active.RandomSeed : active.Id
                        };
                    }
                }
            }
            return null;
        }

        public static void SaveAttemptAnswerProgress(int attemptId, int questionId, string selectedOptions, bool isFlagged)
        {
            if (attemptId <= 0) return;

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
                        SelectedOptionId = selectedOptionId,
                        SelectedOptions = selectedOptions ?? "",
                        IsFlagged = isFlagged
                    };
                    db.AttemptDetails.Add(detail);
                }
                else
                {
                    detail.SelectedOptionId = selectedOptionId;
                    detail.SelectedOptions = selectedOptions ?? "";
                    detail.IsFlagged = isFlagged;
                }
                db.SaveChanges();
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

                foreach (var d in details)
                {
                    string selectedKey = d.SelectedOptions ?? "";
                    if (string.IsNullOrEmpty(selectedKey) && d.SelectedOptionId.HasValue && d.Question != null && d.Question.QuestionOptions != null)
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

                    dict[d.QuestionId] = new AttemptAnswerRecord
                    {
                        AttemptId = attemptId,
                        QuestionId = d.QuestionId,
                        SelectedOptions = selectedKey,
                        IsFlagged = d.IsFlagged
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
                var details = db.AttemptDetails.Where(ad => ad.ExamAttemptId == attemptId).ToList();
                db.AttemptDetails.RemoveRange(details);

                var att = db.ExamAttempts.FirstOrDefault(a => a.Id == attemptId);
                if (att != null)
                {
                    db.ExamAttempts.Remove(att);
                }
                db.SaveChanges();
            }
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
                    att.Status = statusStr;
                    db.SaveChanges();
                }
            }
        }

        public static HashSet<int> GetCompletedExamIdsForUser(int userId)
        {
            var result = new HashSet<int>();
            if (userId <= 0) return result;

            using (var db = new QuizzDbContext())
            {
                var ids = db.ExamAttempts
                    .Where(a => a.UserId == userId && a.SubmitTime != null)
                    .Select(a => a.ExamId)
                    .Distinct()
                    .ToList();

                foreach (int id in ids)
                {
                    result.Add(id);
                }
            }
            return result;
        }

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
                var list = new List<ExamHistoryItem>();

                foreach (var a in attempts)
                {
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
                        Status = !string.IsNullOrEmpty(a.Status) ? a.Status : "Submitted"
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
                    var orderedOpts = (ad.Question.QuestionOptions ?? new List<QuizzApp.DAL.Entities.QuestionOption>())
                        .OrderBy(o => o.Id)
                        .ToList();

                    var correctKeys = new List<string>();
                    for (int i = 0; i < orderedOpts.Count && i < 4; i++)
                    {
                        if (orderedOpts[i].IsCorrect)
                        {
                            correctKeys.Add(((char)('A' + i)).ToString());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(ad.SelectedOptions))
                    {
                        var selSet = ad.SelectedOptions
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToUpperInvariant())
                            .OrderBy(s => s);
                        isCorrect = selSet.SequenceEqual(correctKeys.OrderBy(s => s));
                    }
                    else if (ad.SelectedOptionId.HasValue && orderedOpts.Count > 0)
                    {
                        var selectedOpt = orderedOpts.FirstOrDefault(o => o.Id == ad.SelectedOptionId.Value);
                        if (selectedOpt != null && selectedOpt.IsCorrect && correctKeys.Count <= 1)
                        {
                            isCorrect = true;
                        }
                    }

                    if (!statsMap.ContainsKey(qid))
                    {
                        string diffStr = QuestionService.MapIntToDifficulty(ad.Question.DifficultyLevel);
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
                    QuestionService.EscapeCsv(h.StudentName),
                    QuestionService.EscapeCsv(h.ExamTitle),
                    h.Score,
                    h.CorrectCount,
                    h.TotalQuestions,
                    h.DurationSeconds,
                    QuestionService.EscapeCsv(h.CompletedAt),
                    QuestionService.EscapeCsv(h.StatusTypeText)));
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

                    foreach (var att in userAttempts)
                    {
                        db.AttemptDetails.RemoveRange(att.AttemptDetails);
                    }
                    db.ExamAttempts.RemoveRange(userAttempts);
                    db.SaveChanges();
                }
                else
                {
                    var allDetails = db.AttemptDetails.ToList();
                    db.AttemptDetails.RemoveRange(allDetails);

                    var allAttempts = db.ExamAttempts.ToList();
                    db.ExamAttempts.RemoveRange(allAttempts);

                    db.SaveChanges();
                }
            }
        }
    }
}
