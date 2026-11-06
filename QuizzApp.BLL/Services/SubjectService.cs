using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using QuizzApp.DAL;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Quản lý danh mục Môn học (Subject) và Chương học (Chapter).
    /// </summary>
    public static class SubjectService
    {
        public static List<Subject> GetAllSubjects()
        {
            using (var db = new QuizzDbContext())
            {
                DatabaseService.EnsureDefaultChapters(db);
                var subjects = db.Subjects
                    .Include(s => s.Questions)
                    .Include(s => s.Chapters)
                    .OrderBy(s => s.Id)
                    .ToList();

                return subjects.Select(s => new Subject
                {
                    Id = s.Id,
                    Code = s.SubjectCode,
                    Name = s.SubjectName,
                    Description = string.Format("Mã môn học: {0}", s.SubjectCode),
                    ChapterCount = s.Chapters != null && s.Chapters.Count > 0 ? s.Chapters.Count : 1,
                    QuestionCount = s.Questions != null ? s.Questions.Count : 0
                }).ToList();
            }
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

                db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter
                {
                    SubjectId = subj.Id,
                    Name = "Chương 1: Kiến thức chung"
                });
                db.SaveChanges();

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
                    ExamService.DeleteExamInternal(db, ex.Id);
                }

                var questions = db.Questions.Include(q => q.QuestionOptions).Where(q => q.SubjectId == id).ToList();
                foreach (var q in questions)
                {
                    QuestionService.DeleteQuestionInternal(db, q.Id);
                }

                var chapters = db.Chapters.Where(c => c.SubjectId == id).ToList();
                db.Chapters.RemoveRange(chapters);

                var subj = db.Subjects.FirstOrDefault(s => s.Id == id);
                if (subj != null)
                {
                    db.Subjects.Remove(subj);
                }
                db.SaveChanges();
            }
        }

        public static List<Chapter> GetChaptersBySubject(int? subjectId = null)
        {
            using (var db = new QuizzDbContext())
            {
                DatabaseService.EnsureDefaultChapters(db);

                var query = db.Chapters.Include(c => c.Subject).AsQueryable();
                if (subjectId.HasValue && subjectId.Value > 0)
                {
                    int sid = subjectId.Value;
                    query = query.Where(c => c.SubjectId == sid);
                }

                return query.OrderBy(c => c.Id).ToList().Select(c => new Chapter
                {
                    Id = c.Id,
                    SubjectId = c.SubjectId,
                    Name = c.Name,
                    SubjectName = c.Subject != null ? c.Subject.SubjectName : ""
                }).ToList();
            }
        }

        public static void AddChapter(int subjectId, string chapterName)
        {
            using (var db = new QuizzDbContext())
            {
                db.Chapters.Add(new QuizzApp.DAL.Entities.Chapter
                {
                    SubjectId = subjectId,
                    Name = (chapterName ?? "").Trim()
                });
                db.SaveChanges();
            }
        }

        public static void DeleteChapter(int chapterId)
        {
            using (var db = new QuizzDbContext())
            {
                var chap = db.Chapters.FirstOrDefault(c => c.Id == chapterId);
                if (chap != null)
                {
                    db.Chapters.Remove(chap);
                    db.SaveChanges();
                }
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
    }
}
