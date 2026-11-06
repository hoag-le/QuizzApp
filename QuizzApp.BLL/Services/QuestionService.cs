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
    /// Quản lý ngân hàng Câu hỏi (Question), nhập/xuất CSV và ánh xạ độ khó.
    /// </summary>
    public static class QuestionService
    {
        public static List<Question> GetQuestionsByExamId(int examId)
        {
            using (var db = new QuizzDbContext())
            {
                var chaptersDict = db.Chapters.ToDictionary(c => c.Id, c => c.Name);
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
                        result.Add(MapQuestionEntityToDto(eq.Question, examId, idx++, chaptersDict));
                    }
                }
                return result;
            }
        }

        public static void AddQuestion(Question q)
        {
            using (var db = new QuizzDbContext())
            {
                int subjectId = q.SubjectId > 0 ? q.SubjectId : db.Subjects.Select(s => s.Id).FirstOrDefault();
                int chapId = q.ChapterId > 0 ? q.ChapterId : (db.Chapters.Where(c => c.SubjectId == subjectId).Select(c => c.Id).FirstOrDefault());

                var entity = new QuizzApp.DAL.Entities.Question
                {
                    Content = q.QuestionText,
                    DifficultyLevel = MapDifficultyToInt(q.Difficulty),
                    SubjectId = subjectId,
                    ChapterId = chapId > 0 ? chapId : 1,
                    Explanation = q.Explanation ?? "",
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
                    if (q.ChapterId > 0) entity.ChapterId = q.ChapterId;
                    entity.Explanation = q.Explanation ?? "";

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

        internal static void DeleteQuestionInternal(QuizzDbContext db, int questionId)
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

        internal static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
            {
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            }
            return val;
        }

        internal static int MapDifficultyToInt(string diff)
        {
            if (string.Equals(diff, "Hard", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(diff, "Medium", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        internal static string MapIntToDifficulty(int level)
        {
            if (level >= 3) return "Hard";
            if (level == 2) return "Medium";
            return "Easy";
        }

        private static Question MapQuestionEntityToDto(QuizzApp.DAL.Entities.Question entity, int examId, int index, Dictionary<int, string> chaptersDict)
        {
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
            int chapId = entity.ChapterId > 0 ? entity.ChapterId : 1;
            string chapName = chaptersDict != null && chaptersDict.ContainsKey(chapId)
                ? chaptersDict[chapId]
                : "Chương 1";

            return new Question
            {
                Id = entity.Id,
                ExamId = examId,
                SubjectId = entity.SubjectId,
                SubjectName = entity.Subject != null ? entity.Subject.SubjectName : "Chung",
                ChapterId = chapId,
                ChapterName = chapName,
                Index = index,
                QuestionText = entity.Content,
                OptionA = optA,
                OptionB = optB,
                OptionC = optC,
                OptionD = optD,
                CorrectOption = correctStr,
                Difficulty = MapIntToDifficulty(entity.DifficultyLevel),
                QuestionType = correctLetters.Count > 1 ? "MultipleChoice" : "SingleChoice",
                Explanation = entity.Explanation ?? ""
            };
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
    }
}
