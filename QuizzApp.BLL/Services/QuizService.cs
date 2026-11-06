using System.Collections.Generic;

namespace QuizzApp.BLL
{
    /// <summary>
    /// Facade điều phối các nghiệp vụ của hệ thống, ủy quyền tới các Service chuyên biệt:
    /// DatabaseService, AuthService, SubjectService, ExamService, QuestionService, AttemptService.
    /// </summary>
    public static class QuizService
    {
        // ==================== SERVER CONNECTION & DATABASE ====================

        public static void GetServerConfig(out string host, out string port, out string dbName, out string user, out string pass)
            => DatabaseService.GetServerConfig(out host, out port, out dbName, out user, out pass);

        public static string GetServerSummaryText()
            => DatabaseService.GetServerSummaryText();

        public static bool ConfigureServerConnection(string host, string port, string dbName, string user, string pass, out string errorMessage)
            => DatabaseService.ConfigureServerConnection(host, port, dbName, user, pass, out errorMessage);

        public static void InitializeDatabase()
            => DatabaseService.InitializeDatabase();

        // ==================== AUTHENTICATION & USERS ====================

        public static UserAccount AuthenticateUser(string username, string password)
            => AuthService.AuthenticateUser(username, password);

        public static bool RegisterUser(string username, string password, string fullName, out string errorMessage)
            => AuthService.RegisterUser(username, password, fullName, out errorMessage);

        public static List<UserAccount> GetAllUsers()
            => AuthService.GetAllUsers();

        public static bool AddUserByAdmin(string username, string password, string fullName, string role, out string errorMessage)
            => AuthService.AddUserByAdmin(username, password, fullName, role, out errorMessage);

        public static bool UpdateUserByAdmin(int userId, string username, string newPasswordOptional, string fullName, string role, out string errorMessage)
            => AuthService.UpdateUserByAdmin(userId, username, newPasswordOptional, fullName, role, out errorMessage);

        public static void DeleteUser(int userId)
            => AuthService.DeleteUser(userId);

        // ==================== SUBJECTS & CHAPTERS ====================

        public static List<Subject> GetAllSubjects()
            => SubjectService.GetAllSubjects();

        public static int AddSubject(string name, string description)
            => SubjectService.AddSubject(name, description);

        public static void UpdateSubject(int id, string name, string description)
            => SubjectService.UpdateSubject(id, name, description);

        public static void DeleteSubject(int id)
            => SubjectService.DeleteSubject(id);

        public static List<Chapter> GetChaptersBySubject(int? subjectId = null)
            => SubjectService.GetChaptersBySubject(subjectId);

        public static void AddChapter(int subjectId, string chapterName)
            => SubjectService.AddChapter(subjectId, chapterName);

        public static void DeleteChapter(int chapterId)
            => SubjectService.DeleteChapter(chapterId);

        // ==================== EXAMS & MATRIX BUILDER ====================

        public static List<Exam> GetAllExams()
            => ExamService.GetAllExams();

        public static void GetSubjectDifficultyCounts(int subjectId, out int easyAvailable, out int mediumAvailable, out int hardAvailable)
            => ExamService.GetSubjectDifficultyCounts(subjectId, out easyAvailable, out mediumAvailable, out hardAvailable);

        public static bool BuildExamWithMatrix(Exam exam, int easyReq, int mediumReq, int hardReq, out string message)
            => ExamService.BuildExamWithMatrix(exam, easyReq, mediumReq, hardReq, out message);

        public static int AddExam(Exam exam)
            => ExamService.AddExam(exam);

        public static void UpdateExam(Exam exam)
            => ExamService.UpdateExam(exam);

        public static void DeleteExam(int examId)
            => ExamService.DeleteExam(examId);

        public static void ToggleSaveExam(int examId, bool isSaved)
            => ExamService.ToggleSaveExam(examId, isSaved);

        public static void ToggleManualOpenExam(int examId, bool isManuallyOpened)
            => ExamService.ToggleManualOpenExam(examId, isManuallyOpened);

        // ==================== QUESTIONS CRUD & CSV IMPORT/EXPORT ====================

        public static List<Question> GetQuestionsByExamId(int examId)
            => QuestionService.GetQuestionsByExamId(examId);

        public static void AddQuestion(Question q)
            => QuestionService.AddQuestion(q);

        public static void UpdateQuestion(Question q)
            => QuestionService.UpdateQuestion(q);

        public static void DeleteQuestion(int questionId)
            => QuestionService.DeleteQuestion(questionId);

        public static int GetTotalQuestionCount()
            => QuestionService.GetTotalQuestionCount();

        public static int ImportQuestionsFromCsv(string filePath, int defaultExamId, int defaultSubjectId, int defaultChapterId, out string reportMessage)
            => QuestionService.ImportQuestionsFromCsv(filePath, defaultExamId, defaultSubjectId, defaultChapterId, out reportMessage);

        public static void ExportQuestionsToCsv(string filePath, List<Question> questions)
            => QuestionService.ExportQuestionsToCsv(filePath, questions);

        // ==================== ATTEMPTS, AUTO-SAVE & ANALYTICS ====================

        public static AttemptRecord StartNewAttempt(Exam exam)
            => AttemptService.StartNewAttempt(exam);

        public static AttemptRecord GetActiveAttemptForUser(int userId)
            => AttemptService.GetActiveAttemptForUser(userId);

        public static void SaveAttemptAnswerProgress(int attemptId, int questionId, string selectedOptions, bool isFlagged)
            => AttemptService.SaveAttemptAnswerProgress(attemptId, questionId, selectedOptions, isFlagged);

        public static Dictionary<int, AttemptAnswerRecord> GetSavedAnswersForAttempt(int attemptId)
            => AttemptService.GetSavedAnswersForAttempt(attemptId);

        public static void AbandonInProgressAttempt(int attemptId)
            => AttemptService.AbandonInProgressAttempt(attemptId);

        public static void CompleteAttemptAndSaveHistory(int attemptId, int examId, string examTitle, double score, int correctCount, int totalQuestions, int durationSeconds, bool isAutoSubmitted)
            => AttemptService.CompleteAttemptAndSaveHistory(attemptId, examId, examTitle, score, correctCount, totalQuestions, durationSeconds, isAutoSubmitted);

        public static HashSet<int> GetCompletedExamIdsForUser(int userId)
            => AttemptService.GetCompletedExamIdsForUser(userId);

        public static List<ExamHistoryItem> GetExamHistory(int? filterUserId = null)
            => AttemptService.GetExamHistory(filterUserId);

        public static List<WrongQuestionStat> GetTopWrongQuestions(int limit = 8)
            => AttemptService.GetTopWrongQuestions(limit);

        public static void ExportHistoryToCsv(string filePath, List<ExamHistoryItem> history)
            => AttemptService.ExportHistoryToCsv(filePath, history);

        public static void ClearExamHistory(int? userId = null)
            => AttemptService.ClearExamHistory(userId);
    }
}
