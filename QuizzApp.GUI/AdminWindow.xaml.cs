using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

using QuizzApp.BLL;

namespace QuizzApp.GUI
{
    public partial class AdminWindow : Window
    {
        private List<Subject> _subjects = new List<Subject>();
        private List<Exam> _exams = new List<Exam>();
        private List<UserAccount> _users = new List<UserAccount>();
        private Subject _selectedSubjectForEdit = null;
        private Exam _selectedExamForEdit = null;
        private Exam _currentExamForQuestions = null;
        private Question _selectedQuestionForEdit = null;
        private UserAccount _selectedUserForEdit = null;

        public AdminWindow()
        {
            InitializeComponent();
            if (SessionManager.CurrentUser != null)
            {
                txtAdminName.Text = SessionManager.CurrentUser.FullName;
            }
            RefreshDashboardData();
        }

        private void RefreshDashboardData()
        {
            _subjects = QuizService.GetAllSubjects();
            _exams = QuizService.GetAllExams();
            _users = QuizService.GetAllUsers();
            int totalQuestions = QuizService.GetTotalQuestionCount();
            var allHistory = QuizService.GetExamHistory();

            double avgScore = allHistory.Count > 0 ? allHistory.Average(x => x.Score) : 0.0;

            txtKpiSubjects.Text = $"{_subjects.Count} môn học";
            txtKpiExams.Text = $"{_exams.Count} đề thi";
            txtKpiQuestions.Text = $"{totalQuestions} câu hỏi";
            txtKpiSubmissions.Text = $"{allHistory.Count} lượt • TB: {avgScore:0.0}";

            // Populate Subject Dropdowns & List
            int prevSubjId = (cboExamSubject.SelectedItem as Subject)?.Id ?? 0;
            cboExamSubject.ItemsSource = _subjects;
            if (_subjects.Count > 0)
            {
                cboExamSubject.SelectedItem = _subjects.FirstOrDefault(s => s.Id == prevSubjId) ?? _subjects[0];
            }

            if (cboFilterExamSubject != null)
            {
                int prevFilterSubjId = (cboFilterExamSubject.SelectedItem as Subject)?.Id ?? 0;
                var filterList = new List<Subject> { new Subject { Id = 0, Name = "Tất cả môn học" } };
                filterList.AddRange(_subjects);
                cboFilterExamSubject.ItemsSource = filterList;
                cboFilterExamSubject.SelectedItem = filterList.FirstOrDefault(s => s.Id == prevFilterSubjId) ?? filterList[0];
            }

            ApplySubjectFilter();
            ApplyExamFilter();
            ApplyUserFilter();

            if (_currentExamForQuestions != null)
            {
                _currentExamForQuestions = _exams.FirstOrDefault(x => x.Id == _currentExamForQuestions.Id) ?? _currentExamForQuestions;
                txtCurrentExamForQuestions.Text = _currentExamForQuestions.Title;
                txtQuestionEditorExamSubtitle.Text = $"Đề đang thực hiện: {_currentExamForQuestions.Title}";
            }
            else if (_exams.Count > 0)
            {
                _currentExamForQuestions = _exams[0];
                txtCurrentExamForQuestions.Text = _currentExamForQuestions.Title;
                txtQuestionEditorExamSubtitle.Text = $"Đề đang thực hiện: {_currentExamForQuestions.Title}";
            }

            // Analytics & History
            itemsAdminHistory.ItemsSource = allHistory;
            int exc = allHistory.Count(h => h.Score >= 8.0);
            int good = allHistory.Count(h => h.Score >= 6.5 && h.Score < 8.0);
            int pass = allHistory.Count(h => h.Score >= 5.0 && h.Score < 6.5);
            int fail = allHistory.Count(h => h.Score < 5.0);

            int maxCount = Math.Max(1, new[] { exc, good, pass, fail }.Max());
            double maxBarHeight = 110.0;

            txtDistExcellent.Text = $"{exc} lượt";
            txtDistGood.Text = $"{good} lượt";
            txtDistPass.Text = $"{pass} lượt";
            txtDistFail.Text = $"{fail} lượt";

            barDistExcellent.Height = exc == 0 ? 6 : Math.Max(14, (exc / (double)maxCount) * maxBarHeight);
            barDistGood.Height = good == 0 ? 6 : Math.Max(14, (good / (double)maxCount) * maxBarHeight);
            barDistPass.Height = pass == 0 ? 6 : Math.Max(14, (pass / (double)maxCount) * maxBarHeight);
            barDistFail.Height = fail == 0 ? 6 : Math.Max(14, (fail / (double)maxCount) * maxBarHeight);

            itemsWrongStats.ItemsSource = QuizService.GetTopWrongQuestions();
            UpdateMatrixAvailabilityLabel();
        }

        private void SetActiveMenu(Button activeBtn)
        {
            Button[] buttons = { btnTabExams, btnTabSubjects, btnTabResults, btnTabUsers };
            foreach (var btn in buttons)
            {
                if (btn == activeBtn)
                {
                    btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3563E9"));
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                }
            }
        }

        private void BtnTabExams_Click(object sender, RoutedEventArgs e)
        {
            SetActiveMenu(btnTabExams);
            txtHeaderTitle.Text = "Dashboard Tạo & Quản lý Đề thi";

            gridKpiStats.Visibility = Visibility.Collapsed;
            viewManageExams.Visibility = Visibility.Visible;
            viewManageQuestions.Visibility = Visibility.Collapsed;
            viewManageSubjects.Visibility = Visibility.Collapsed;
            viewAllResults.Visibility = Visibility.Collapsed;
            viewManageUsers.Visibility = Visibility.Collapsed;
            ShowExamEditorPanel(false);
            RefreshDashboardData();
        }

        private void OpenQuestionManagerForExam(Exam exam)
        {
            SetActiveMenu(btnTabExams);
            txtHeaderTitle.Text = $"Quản lý câu hỏi: {exam.Title}";

            gridKpiStats.Visibility = Visibility.Collapsed;
            viewManageExams.Visibility = Visibility.Collapsed;
            viewManageQuestions.Visibility = Visibility.Visible;
            viewManageSubjects.Visibility = Visibility.Collapsed;
            viewAllResults.Visibility = Visibility.Collapsed;
            viewManageUsers.Visibility = Visibility.Collapsed;

            _currentExamForQuestions = exam;
            RefreshDashboardData();

            var chapters = QuizService.GetChaptersBySubject(_currentExamForQuestions.SubjectId);
            cboQChapter.ItemsSource = chapters;
            if (chapters.Count > 0) cboQChapter.SelectedIndex = 0;

            ShowQuestionEditorPanel(false);
            LoadQuestionsForSelectedExam();
        }

        private void BtnTabSubjects_Click(object sender, RoutedEventArgs e)
        {
            SetActiveMenu(btnTabSubjects);
            txtHeaderTitle.Text = "Quản lý Môn học & Chương";

            gridKpiStats.Visibility = Visibility.Collapsed;
            viewManageExams.Visibility = Visibility.Collapsed;
            viewManageQuestions.Visibility = Visibility.Collapsed;
            viewManageSubjects.Visibility = Visibility.Visible;
            viewAllResults.Visibility = Visibility.Collapsed;
            viewManageUsers.Visibility = Visibility.Collapsed;
            ShowSubjectEditorPanel(false);
            RefreshDashboardData();
        }

        private void BtnTabResults_Click(object sender, RoutedEventArgs e)
        {
            SetActiveMenu(btnTabResults);
            txtHeaderTitle.Text = "Thống kê & Báo cáo Kết quả";

            gridKpiStats.Visibility = Visibility.Visible;
            viewManageExams.Visibility = Visibility.Collapsed;
            viewManageQuestions.Visibility = Visibility.Collapsed;
            viewManageSubjects.Visibility = Visibility.Collapsed;
            viewAllResults.Visibility = Visibility.Visible;
            viewManageUsers.Visibility = Visibility.Collapsed;
            RefreshDashboardData();
        }

        private void BtnTabUsers_Click(object sender, RoutedEventArgs e)
        {
            SetActiveMenu(btnTabUsers);
            txtHeaderTitle.Text = "Quản lý Tài khoản Hệ thống";

            gridKpiStats.Visibility = Visibility.Collapsed;
            viewManageExams.Visibility = Visibility.Collapsed;
            viewManageQuestions.Visibility = Visibility.Collapsed;
            viewManageSubjects.Visibility = Visibility.Collapsed;
            viewAllResults.Visibility = Visibility.Collapsed;
            viewManageUsers.Visibility = Visibility.Visible;
            ShowUserEditorPanel(false);
            RefreshDashboardData();
        }

        // ==================== EXAM & MATRIX BUILDER LOGIC ====================

        private void ShowExamEditorPanel(bool show)
        {
            if (show)
            {
                borderExamListContainer.Visibility = Visibility.Collapsed;
                borderExamEditorPanel.Visibility = Visibility.Visible;
                colExamList.Width = new GridLength(0);
                colExamEditor.Width = new GridLength(1.0, GridUnitType.Star);
            }
            else
            {
                borderExamEditorPanel.Visibility = Visibility.Collapsed;
                borderExamListContainer.Visibility = Visibility.Visible;
                colExamList.Width = new GridLength(1.0, GridUnitType.Star);
                colExamEditor.Width = new GridLength(0);
                lstExams.SelectedItem = null;
                _selectedExamForEdit = null;
            }
        }

        private void BtnCloseExamEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowExamEditorPanel(false);
        }

        private void BtnOpenQuestionsFromEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExamForEdit != null)
            {
                OpenQuestionManagerForExam(_selectedExamForEdit);
            }
        }

        private void BtnBackToExamEditor_Click(object sender, RoutedEventArgs e)
        {
            viewManageQuestions.Visibility = Visibility.Collapsed;
            viewManageExams.Visibility = Visibility.Visible;
            txtHeaderTitle.Text = "Dashboard Tạo & Quản lý Đề thi";
            ShowExamEditorPanel(false);
            RefreshDashboardData();
        }

        private void CboExamMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (borderExamScheduleBox == null || cboExamMode == null) return;
            string mode = (cboExamMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Real";
            borderExamScheduleBox.Visibility = string.Equals(mode, "Practice", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleManualOpenExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam && !exam.IsPracticeMode)
            {
                exam.IsManuallyOpened = !exam.IsManuallyOpened;
                QuizService.ToggleManualOpenExam(exam.Id, exam.IsManuallyOpened);
                ApplyExamFilter();
            }
        }

        private bool TryGetExamScheduleInputs(string mode, out DateTime? examStartTime, out bool isManuallyOpened, out string errorMessage)
        {
            examStartTime = null;
            isManuallyOpened = false;
            errorMessage = "";

            if (string.Equals(mode, "Practice", StringComparison.OrdinalIgnoreCase))
            {
                isManuallyOpened = true;
                return true;
            }

            isManuallyOpened = chkManualOpenExam?.IsChecked == true;
            DateTime selectedDate = dpExamStartDate?.SelectedDate ?? DateTime.Today;
            string timeRaw = (txtExamStartTime?.Text ?? "").Trim();

            if (!TimeSpan.TryParse(timeRaw, out TimeSpan timeOfDay))
            {
                errorMessage = "Giờ thi không hợp lệ! Vui lòng nhập đúng định dạng HH:mm, ví dụ 08:30 hoặc 14:00.";
                return false;
            }

            examStartTime = selectedDate.Date.Add(timeOfDay);
            return true;
        }

        private void CboExamSubject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMatrixAvailabilityLabel();
        }

        private void TxtSearchExams_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyExamFilter();
        }

        private void CboFilterExamSubject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyExamFilter();
        }

        private void BtnSearchExams_Click(object sender, RoutedEventArgs e)
        {
            ApplyExamFilter();
        }

        private void ApplyExamFilter()
        {
            if (lstExams == null) return;

            var filtered = _exams.AsEnumerable();

            int filterSubjId = (cboFilterExamSubject?.SelectedItem as Subject)?.Id ?? 0;
            if (filterSubjId > 0)
            {
                filtered = filtered.Where(ex => ex.SubjectId == filterSubjId);
            }

            string kw = txtSearchExams?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(kw))
            {
                filtered = filtered.Where(ex =>
                    (ex.Title != null && ex.Title.ToLower().Contains(kw)) ||
                    (ex.Description != null && ex.Description.ToLower().Contains(kw)) ||
                    (ex.SubjectName != null && ex.SubjectName.ToLower().Contains(kw)));
            }

            lstExams.ItemsSource = filtered.ToList();
        }

        private void UpdateMatrixAvailabilityLabel()
        {
            if (txtMatrixAvailableInfo == null) return;
            int subjId = (cboExamSubject.SelectedItem as Subject)?.Id ?? 1;
            QuizService.GetSubjectDifficultyCounts(subjId, out int easy, out int med, out int hard);
            txtMatrixAvailableInfo.Text = $"Khả dụng trong ngân hàng môn này: Dễ: {easy} | Trung bình: {med} | Khó: {hard}";
        }

        private void BtnEditExamItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam)
            {
                OpenExamEditorForExam(exam);
            }
        }

        private void OpenExamEditorForExam(Exam exam)
        {
            if (exam == null) return;

            _selectedExamForEdit = exam;
            ShowExamEditorPanel(true);

            txtExamFormTitle.Text = $"Thiết lập đề: {exam.Title}";
            txtExamTitleInput.Text = exam.Title;
            txtExamDescInput.Text = exam.Description;
            txtExamDuration.Text = exam.DurationMinutes.ToString();
            chkShuffleQuestions.IsChecked = exam.ShuffleQuestions;
            chkShuffleAnswers.IsChecked = exam.ShuffleAnswers;

            var subj = _subjects.FirstOrDefault(s => s.Id == exam.SubjectId);
            if (subj != null) cboExamSubject.SelectedItem = subj;

            cboExamMode.SelectedIndex = exam.IsPracticeMode ? 1 : 0;
            dpExamStartDate.SelectedDate = (exam.ExamStartTime ?? DateTime.Now).Date;
            txtExamStartTime.Text = (exam.ExamStartTime ?? DateTime.Now).ToString("HH:mm");
            chkManualOpenExam.IsChecked = exam.IsManuallyOpened;
            if (borderExamScheduleBox != null) borderExamScheduleBox.Visibility = exam.IsPracticeMode ? Visibility.Collapsed : Visibility.Visible;

            btnSaveExam.Content = "💾  Cập nhật thông tin đề thi";
            btnDeleteExam.Visibility = Visibility.Visible;
            btnOpenQuestionsFromEditor.Visibility = Visibility.Visible;
        }

        private void BtnResetExamForm_Click(object sender, RoutedEventArgs e)
        {
            ResetExamForm();
            ShowExamEditorPanel(true);
        }

        private void ResetExamForm()
        {
            lstExams.SelectedItem = null;
            _selectedExamForEdit = null;
            txtExamFormTitle.Text = "Thiết lập đề thi mới";
            txtExamTitleInput.Text = "";
            txtExamDescInput.Text = "";
            txtExamDuration.Text = "15";
            chkShuffleQuestions.IsChecked = true;
            chkShuffleAnswers.IsChecked = false;
            cboExamMode.SelectedIndex = 0;
            if (dpExamStartDate != null) dpExamStartDate.SelectedDate = DateTime.Today;
            if (txtExamStartTime != null) txtExamStartTime.Text = DateTime.Now.AddMinutes(10).ToString("HH:mm");
            if (chkManualOpenExam != null) chkManualOpenExam.IsChecked = false;
            if (borderExamScheduleBox != null) borderExamScheduleBox.Visibility = Visibility.Visible;
            btnSaveExam.Content = "💾  Lưu đề thi thủ công";
            btnDeleteExam.Visibility = Visibility.Collapsed;
            btnOpenQuestionsFromEditor.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveExam_Click(object sender, RoutedEventArgs e)
        {
            string title = txtExamTitleInput.Text.Trim();
            string desc = txtExamDescInput.Text.Trim();

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Vui lòng nhập Tên đề thi!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtExamDuration.Text.Trim(), out int duration) || duration <= 0)
            {
                MessageBox.Show("Thời gian làm bài phải là số nguyên dương!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int subjectId = (cboExamSubject.SelectedItem as Subject)?.Id ?? 1;
            string mode = (cboExamMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Real";
            bool shuffleQ = chkShuffleQuestions.IsChecked == true;
            bool shuffleA = chkShuffleAnswers.IsChecked == true;
            if (!TryGetExamScheduleInputs(mode, out DateTime? examStartTime, out bool isManuallyOpened, out string schedErr))
            {
                MessageBox.Show(schedErr, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedExamForEdit == null)
            {
                var newExam = new Exam
                {
                    Code = "QUIZ",
                    Title = title,
                    Description = string.IsNullOrEmpty(desc) ? $"Đề kiểm tra trắc nghiệm {title}" : desc,
                    SubjectId = subjectId,
                    DurationMinutes = duration,
                    ShuffleQuestions = shuffleQ,
                    ShuffleAnswers = shuffleA,
                    Mode = mode,
                    ExamStartTime = examStartTime,
                    IsManuallyOpened = isManuallyOpened,
                    IsSaved = false
                };
                QuizService.AddExam(newExam);
                MessageBox.Show("Đã tạo đề thi mới thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _selectedExamForEdit.Title = title;
                _selectedExamForEdit.Description = desc;
                _selectedExamForEdit.SubjectId = subjectId;
                _selectedExamForEdit.DurationMinutes = duration;
                _selectedExamForEdit.ShuffleQuestions = shuffleQ;
                _selectedExamForEdit.ShuffleAnswers = shuffleA;
                _selectedExamForEdit.Mode = mode;
                _selectedExamForEdit.ExamStartTime = examStartTime;
                _selectedExamForEdit.IsManuallyOpened = isManuallyOpened;

                QuizService.UpdateExam(_selectedExamForEdit);
                MessageBox.Show("Đã cập nhật thông tin đề thi thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            ResetExamForm();
            ShowExamEditorPanel(false);
            RefreshDashboardData();
        }

        private void BtnGenerateMatrixExam_Click(object sender, RoutedEventArgs e)
        {
            string title = txtExamTitleInput.Text.Trim();
            string desc = txtExamDescInput.Text.Trim();

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Vui lòng nhập Tên đề thi trước khi sinh đề theo ma trận!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtExamDuration.Text.Trim(), out int duration) || duration <= 0)
            {
                MessageBox.Show("Thời gian làm bài không hợp lệ!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int.TryParse(txtMatrixEasy.Text.Trim(), out int easyReq);
            int.TryParse(txtMatrixMedium.Text.Trim(), out int medReq);
            int.TryParse(txtMatrixHard.Text.Trim(), out int hardReq);

            if (easyReq + medReq + hardReq <= 0)
            {
                MessageBox.Show("Tổng số lượng câu hỏi trong ma trận phải lớn hơn 0!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int subjectId = (cboExamSubject.SelectedItem as Subject)?.Id ?? 1;
            string mode = (cboExamMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Real";

            if (!TryGetExamScheduleInputs(mode, out DateTime? matrixStartTime, out bool matrixManualOpen, out string schedErr))
            {
                MessageBox.Show(schedErr, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var examTemplate = new Exam
            {
                Code = "QUIZ",
                Title = title,
                Description = string.IsNullOrEmpty(desc) ? "Đề sinh tự động theo ma trận" : desc,
                SubjectId = subjectId,
                DurationMinutes = duration,
                ShuffleQuestions = chkShuffleQuestions.IsChecked == true,
                ShuffleAnswers = chkShuffleAnswers.IsChecked == true,
                ExamStartTime = matrixStartTime,
                IsManuallyOpened = matrixManualOpen,
                Mode = mode
            };

            if (QuizService.BuildExamWithMatrix(examTemplate, easyReq, medReq, hardReq, out string msg))
            {
                MessageBox.Show(msg, "Sinh đề theo ma trận thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetExamForm();
                ShowExamEditorPanel(false);
                RefreshDashboardData();
            }
            else
            {
                MessageBox.Show(msg, "Cảnh báo không đủ câu hỏi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDeleteExam_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExamForEdit == null) return;

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa đề thi '{_selectedExamForEdit.Title}' không?",
                "Xác nhận xóa đề thi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                QuizService.DeleteExam(_selectedExamForEdit.Id);
                ResetExamForm();
                ShowExamEditorPanel(false);
                RefreshDashboardData();
            }
        }

        private void BtnManageExamQuestions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam)
            {
                OpenQuestionManagerForExam(exam);
            }
        }

        // ==================== QUESTION BANK CRUD & IMPORT/EXPORT ====================

        private void ShowQuestionEditorPanel(bool show)
        {
            if (show)
            {
                borderQuestionListContainer.Visibility = Visibility.Collapsed;
                borderQuestionEditorPanel.Visibility = Visibility.Visible;
                colQuestionList.Width = new GridLength(0);
                colQuestionEditor.Width = new GridLength(1.0, GridUnitType.Star);
            }
            else
            {
                borderQuestionEditorPanel.Visibility = Visibility.Collapsed;
                borderQuestionListContainer.Visibility = Visibility.Visible;
                colQuestionList.Width = new GridLength(1.0, GridUnitType.Star);
                colQuestionEditor.Width = new GridLength(0);
                lstQuestions.SelectedItem = null;
                _selectedQuestionForEdit = null;
            }
        }

        private void BtnCloseQuestionEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowQuestionEditorPanel(false);
        }

        private void CboFilterDifficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadQuestionsForSelectedExam();
        }

        private void TxtSearchQuestionBank_TextChanged(object sender, TextChangedEventArgs e)
        {
            LoadQuestionsForSelectedExam();
        }

        private void BtnSearchQuestions_Click(object sender, RoutedEventArgs e)
        {
            LoadQuestionsForSelectedExam();
        }

        private void LoadQuestionsForSelectedExam()
        {
            if (lstQuestions == null) return;

            if (_currentExamForQuestions != null)
            {
                txtCurrentExamForQuestions.Text = _currentExamForQuestions.Title;
                txtQuestionEditorExamSubtitle.Text = $"Đề đang thực hiện: {_currentExamForQuestions.Title}";

                var questions = QuizService.GetQuestionsByExamId(_currentExamForQuestions.Id);

                string diffFilter = (cboFilterDifficulty?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
                if (diffFilter != "All")
                {
                    questions = questions.Where(q => string.Equals(q.Difficulty, diffFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                string kw = txtSearchQuestionBank?.Text?.Trim().ToLower() ?? "";
                if (!string.IsNullOrEmpty(kw))
                {
                    questions = questions.Where(q => q.QuestionText.ToLower().Contains(kw)).ToList();
                }

                lstQuestions.ItemsSource = questions;
                txtQuestionListCount.Text = $"Danh sách câu hỏi: {questions.Count}";
                ResetQuestionForm();
            }
            else
            {
                lstQuestions.ItemsSource = null;
                txtQuestionListCount.Text = "Danh sách câu hỏi: 0";
            }
        }

        private void LstQuestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstQuestions.SelectedItem is Question q)
            {
                _selectedQuestionForEdit = q;
                ShowQuestionEditorPanel(true);

                txtQuestionFormTitle.Text = $"Chỉnh sửa Câu {q.Index}";
                txtQContent.Text = q.QuestionText;
                txtQOptA.Text = q.OptionA;
                txtQOptB.Text = q.OptionB;
                txtQOptC.Text = q.OptionC;
                txtQOptD.Text = q.OptionD;
                txtQCorrectInput.Text = q.CorrectOption;
                txtQExplanation.Text = q.Explanation;

                for (int i = 0; i < cboQDifficulty.Items.Count; i++)
                {
                    if (cboQDifficulty.Items[i] is ComboBoxItem item &&
                        string.Equals(item.Tag?.ToString(), q.Difficulty, StringComparison.OrdinalIgnoreCase))
                    {
                        cboQDifficulty.SelectedIndex = i;
                        break;
                    }
                }

                if (cboQChapter.ItemsSource is IEnumerable<Chapter> chaps)
                {
                    var matchChap = chaps.FirstOrDefault(c => c.Id == q.ChapterId);
                    if (matchChap != null) cboQChapter.SelectedItem = matchChap;
                }

                btnSaveQuestion.Content = "💾  Cập nhật câu hỏi";
                btnDeleteQuestion.Visibility = Visibility.Visible;
            }
        }

        private void BtnResetQuestionForm_Click(object sender, RoutedEventArgs e)
        {
            ResetQuestionForm();
            ShowQuestionEditorPanel(true);
        }

        private void ResetQuestionForm()
        {
            lstQuestions.SelectedItem = null;
            _selectedQuestionForEdit = null;
            txtQuestionFormTitle.Text = "Thêm câu hỏi mới";
            txtQContent.Text = "";
            txtQOptA.Text = "";
            txtQOptB.Text = "";
            txtQOptC.Text = "";
            txtQOptD.Text = "";
            txtQCorrectInput.Text = "A";
            txtQExplanation.Text = "";
            cboQDifficulty.SelectedIndex = 0;
            btnSaveQuestion.Content = "💾  Lưu câu hỏi mới";
            btnDeleteQuestion.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveQuestion_Click(object sender, RoutedEventArgs e)
        {
            if (_currentExamForQuestions == null)
            {
                MessageBox.Show("Vui lòng chọn một đề thi trước khi thêm câu hỏi!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var currentExam = _currentExamForQuestions;

            string content = txtQContent.Text.Trim();
            string optA = txtQOptA.Text.Trim();
            string optB = txtQOptB.Text.Trim();
            string optC = txtQOptC.Text.Trim();
            string optD = txtQOptD.Text.Trim();
            string explanation = txtQExplanation.Text.Trim();
            string rawCorrect = txtQCorrectInput.Text.Trim().ToUpperInvariant();
            string difficulty = (cboQDifficulty.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Easy";
            int chapterId = (cboQChapter.SelectedItem as Chapter)?.Id ?? 1;

            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(optA) || string.IsNullOrEmpty(optB))
            {
                MessageBox.Show("Nội dung câu hỏi và tối thiểu 2 đáp án không được để trống!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var validParts = rawCorrect.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => s == "A" || s == "B" || s == "C" || s == "D")
                                       .Distinct()
                                       .OrderBy(s => s)
                                       .ToList();

            if (validParts.Count == 0)
            {
                MessageBox.Show("Đáp án đúng phải chứa ít nhất một ký tự A, B, C hoặc D!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string normalizedCorrect = string.Join(",", validParts);

            if (_selectedQuestionForEdit == null)
            {
                var newQ = new Question
                {
                    ExamId = currentExam.Id,
                    SubjectId = currentExam.SubjectId,
                    ChapterId = chapterId,
                    QuestionText = content,
                    OptionA = optA,
                    OptionB = optB,
                    OptionC = optC,
                    OptionD = optD,
                    CorrectOption = normalizedCorrect,
                    Difficulty = difficulty,
                    Explanation = explanation
                };
                QuizService.AddQuestion(newQ);
                MessageBox.Show("Đã thêm câu hỏi mới vào ngân hàng đề!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _selectedQuestionForEdit.ChapterId = chapterId;
                _selectedQuestionForEdit.QuestionText = content;
                _selectedQuestionForEdit.OptionA = optA;
                _selectedQuestionForEdit.OptionB = optB;
                _selectedQuestionForEdit.OptionC = optC;
                _selectedQuestionForEdit.OptionD = optD;
                _selectedQuestionForEdit.CorrectOption = normalizedCorrect;
                _selectedQuestionForEdit.Difficulty = difficulty;
                _selectedQuestionForEdit.Explanation = explanation;

                QuizService.UpdateQuestion(_selectedQuestionForEdit);
                MessageBox.Show("Đã cập nhật câu hỏi thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            ShowQuestionEditorPanel(false);
            LoadQuestionsForSelectedExam();
            RefreshDashboardData();
        }

        private void BtnDeleteQuestion_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedQuestionForEdit == null) return;

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa Câu {_selectedQuestionForEdit.Index} khỏi đề thi này không?",
                "Xác nhận xóa câu hỏi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                QuizService.DeleteQuestion(_selectedQuestionForEdit.Id);
                ShowQuestionEditorPanel(false);
                LoadQuestionsForSelectedExam();
                RefreshDashboardData();
            }
        }

        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentExamForQuestions == null)
            {
                MessageBox.Show("Vui lòng chọn đề thi đích trước khi Import!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var currentExam = _currentExamForQuestions;

            var dlg = new OpenFileDialog
            {
                Title = "Chọn file CSV ngân hàng câu hỏi",
                Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    int chapId = (cboQChapter.SelectedItem as Chapter)?.Id ?? 1;
                    QuizService.ImportQuestionsFromCsv(dlg.FileName, currentExam.Id, currentExam.SubjectId, chapId, out string msg);
                    LoadQuestionsForSelectedExam();
                    RefreshDashboardData();
                    MessageBox.Show(msg, "Kết quả Import", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi đọc file CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentExamForQuestions == null) return;
            var currentExam = _currentExamForQuestions;
            var questions = QuizService.GetQuestionsByExamId(currentExam.Id);

            var dlg = new SaveFileDialog
            {
                Title = "Xuất danh sách câu hỏi ra file CSV/Excel",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"CauHoi_{currentExam.Title}_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    QuizService.ExportQuestionsToCsv(dlg.FileName, questions);
                    MessageBox.Show($"Đã xuất {questions.Count} câu hỏi ra file thành công:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ==================== SUBJECTS & CHAPTERS LOGIC ====================

        private void TxtSearchSubjects_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySubjectFilter();
        }

        private void BtnSearchSubjects_Click(object sender, RoutedEventArgs e)
        {
            ApplySubjectFilter();
        }

        private void ApplySubjectFilter()
        {
            if (lstSubjects == null) return;

            string kw = txtSearchSubjects?.Text?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(kw))
            {
                lstSubjects.ItemsSource = _subjects;
            }
            else
            {
                lstSubjects.ItemsSource = _subjects.Where(s =>
                    (s.Name != null && s.Name.ToLower().Contains(kw)) ||
                    (s.Description != null && s.Description.ToLower().Contains(kw))).ToList();
            }
        }

        private void ShowSubjectEditorPanel(bool show)
        {
            if (show)
            {
                borderSubjectListContainer.Visibility = Visibility.Collapsed;
                borderSubjectEditorPanel.Visibility = Visibility.Visible;
                colSubjectList.Width = new GridLength(0);
                colSubjectEditor.Width = new GridLength(1.0, GridUnitType.Star);
            }
            else
            {
                borderSubjectEditorPanel.Visibility = Visibility.Collapsed;
                borderSubjectListContainer.Visibility = Visibility.Visible;
                colSubjectList.Width = new GridLength(1.0, GridUnitType.Star);
                colSubjectEditor.Width = new GridLength(0);
                lstSubjects.SelectedItem = null;
                _selectedSubjectForEdit = null;
            }
        }

        private void BtnCloseSubjectEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowSubjectEditorPanel(false);
        }

        private void BtnResetSubjectForm_Click(object sender, RoutedEventArgs e)
        {
            ResetSubjectForm();
            ShowSubjectEditorPanel(true);
        }

        private void ResetSubjectForm()
        {
            lstSubjects.SelectedItem = null;
            _selectedSubjectForEdit = null;
            txtSubjectFormTitle.Text = "Thêm môn học mới";
            txtNewSubjectName.Text = "";
            txtNewSubjectDesc.Text = "";
            btnSaveSubject.Content = "💾  Lưu môn học mới";
            btnDeleteSubject.Visibility = Visibility.Collapsed;
            borderChaptersSection.Visibility = Visibility.Collapsed;
            lstChapters.ItemsSource = null;
        }

        private void LstSubjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSubjects.SelectedItem is Subject subj)
            {
                _selectedSubjectForEdit = subj;
                ShowSubjectEditorPanel(true);

                txtSubjectFormTitle.Text = $"Thiết lập môn học: {subj.Name}";
                txtNewSubjectName.Text = subj.Name;
                txtNewSubjectDesc.Text = subj.Description;
                btnSaveSubject.Content = "💾  Cập nhật thông tin môn học";
                btnDeleteSubject.Visibility = Visibility.Visible;
                borderChaptersSection.Visibility = Visibility.Visible;

                txtChapterSectionTitle.Text = $"Danh sách Chương học của môn: {subj.Name}";
                lstChapters.ItemsSource = QuizService.GetChaptersBySubject(subj.Id);
            }
        }

        private void BtnSaveSubject_Click(object sender, RoutedEventArgs e)
        {
            string name = txtNewSubjectName.Text.Trim();
            string desc = txtNewSubjectDesc.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Vui lòng nhập tên môn học!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedSubjectForEdit == null)
            {
                QuizService.AddSubject(name, desc);
                MessageBox.Show("Đã thêm môn học mới thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                QuizService.UpdateSubject(_selectedSubjectForEdit.Id, name, desc);
                MessageBox.Show("Đã cập nhật thông tin môn học thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            ResetSubjectForm();
            ShowSubjectEditorPanel(false);
            RefreshDashboardData();
        }

        private void BtnDeleteSubject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSubjectForEdit == null) return;

            var confirm = MessageBox.Show($"Bạn có chắc chắn muốn xóa môn học '{_selectedSubjectForEdit.Name}' cùng các chương liên quan không?",
                "Xác nhận xóa môn học", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                QuizService.DeleteSubject(_selectedSubjectForEdit.Id);
                ResetSubjectForm();
                ShowSubjectEditorPanel(false);
                RefreshDashboardData();
            }
        }

        private void BtnAddChapter_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSubjectForEdit == null)
            {
                MessageBox.Show("Vui lòng lưu môn học trước khi thêm chương!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string chapName = txtNewChapterName.Text.Trim();
            if (string.IsNullOrEmpty(chapName))
            {
                MessageBox.Show("Vui lòng nhập tên chương học!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int subjId = _selectedSubjectForEdit.Id;
            QuizService.AddChapter(subjId, chapName);
            txtNewChapterName.Text = "";
            lstChapters.ItemsSource = QuizService.GetChaptersBySubject(subjId);
            RefreshDashboardData();
            _selectedSubjectForEdit = _subjects.FirstOrDefault(s => s.Id == subjId) ?? _selectedSubjectForEdit;
        }

        private void BtnDeleteChapterItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Chapter chap && _selectedSubjectForEdit != null)
            {
                int subjId = _selectedSubjectForEdit.Id;
                QuizService.DeleteChapter(chap.Id);
                lstChapters.ItemsSource = QuizService.GetChaptersBySubject(subjId);
                RefreshDashboardData();
                _selectedSubjectForEdit = _subjects.FirstOrDefault(s => s.Id == subjId) ?? _selectedSubjectForEdit;
            }
        }

        // ==================== USER ACCOUNTS MANAGEMENT ====================

        private void ShowUserEditorPanel(bool show)
        {
            if (show)
            {
                borderUserListContainer.Visibility = Visibility.Collapsed;
                borderUserEditorPanel.Visibility = Visibility.Visible;
                colUserList.Width = new GridLength(0);
                colUserEditor.Width = new GridLength(1.0, GridUnitType.Star);
            }
            else
            {
                borderUserEditorPanel.Visibility = Visibility.Collapsed;
                borderUserListContainer.Visibility = Visibility.Visible;
                colUserList.Width = new GridLength(1.0, GridUnitType.Star);
                colUserEditor.Width = new GridLength(0);
                lstUsers.SelectedItem = null;
                _selectedUserForEdit = null;
            }
        }

        private void BtnCloseUserEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowUserEditorPanel(false);
        }

        private void BtnResetUserForm_Click(object sender, RoutedEventArgs e)
        {
            ResetUserForm();
            ShowUserEditorPanel(true);
        }

        private void ResetUserForm()
        {
            lstUsers.SelectedItem = null;
            _selectedUserForEdit = null;
            txtUserFormTitle.Text = "Thêm tài khoản mới";
            txtUserUsername.Text = "";
            txtUserFullName.Text = "";
            txtUserPassword.Text = "";
            txtUserPasswordLabel.Text = "Mật khẩu";
            cboUserRole.SelectedIndex = 0;
            btnSaveUser.Content = "💾  Lưu tài khoản mới";
            btnDeleteUser.Visibility = Visibility.Collapsed;
        }

        private void BtnEditUserItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is UserAccount user)
            {
                OpenUserEditorForUser(user);
            }
        }

        private void OpenUserEditorForUser(UserAccount user)
        {
            if (user == null) return;

            _selectedUserForEdit = user;
            ShowUserEditorPanel(true);

            txtUserFormTitle.Text = $"Chỉnh sửa tài khoản: {user.FullName}";
            txtUserUsername.Text = user.Username;
            txtUserFullName.Text = user.FullName;
            txtUserPassword.Text = "";
            txtUserPasswordLabel.Text = "Mật khẩu mới";
            cboUserRole.SelectedIndex = user.IsAdmin ? 1 : 0;

            btnSaveUser.Content = "💾  Cập nhật tài khoản";
            btnDeleteUser.Visibility = Visibility.Visible;
        }

        private void TxtSearchUsers_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyUserFilter();
        }

        private void CboFilterUserRole_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyUserFilter();
        }

        private void BtnSearchUsers_Click(object sender, RoutedEventArgs e)
        {
            ApplyUserFilter();
        }

        private void ApplyUserFilter()
        {
            if (lstUsers == null) return;

            var filtered = _users.AsEnumerable();

            string roleFilter = (cboFilterUserRole?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            if (roleFilter == "Admin")
            {
                filtered = filtered.Where(u => u.IsAdmin);
            }
            else if (roleFilter == "User")
            {
                filtered = filtered.Where(u => !u.IsAdmin);
            }

            string kw = txtSearchUsers?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(kw))
            {
                filtered = filtered.Where(u =>
                    (u.FullName != null && u.FullName.ToLower().Contains(kw)) ||
                    (u.Username != null && u.Username.ToLower().Contains(kw)));
            }

            lstUsers.ItemsSource = filtered.ToList();
        }

        private void BtnSaveUser_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUserUsername.Text.Trim();
            string fullName = txtUserFullName.Text.Trim();
            string password = txtUserPassword.Text.Trim();
            string role = (cboUserRole.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "User";

            if (_selectedUserForEdit == null)
            {
                if (QuizService.AddUserByAdmin(username, password, fullName, role, out string err))
                {
                    MessageBox.Show("Đã tạo tài khoản mới thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetUserForm();
                    ShowUserEditorPanel(false);
                    RefreshDashboardData();
                }
                else
                {
                    MessageBox.Show(err, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                if (QuizService.UpdateUserByAdmin(_selectedUserForEdit.Id, username, password, fullName, role, out string err))
                {
                    if (SessionManager.CurrentUser != null && SessionManager.CurrentUser.Id == _selectedUserForEdit.Id)
                    {
                        SessionManager.CurrentUser.FullName = fullName;
                        SessionManager.CurrentUser.Username = username;
                        txtAdminName.Text = fullName;
                    }
                    MessageBox.Show("Đã cập nhật thông tin tài khoản thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetUserForm();
                    ShowUserEditorPanel(false);
                    RefreshDashboardData();
                }
                else
                {
                    MessageBox.Show(err, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUserForEdit == null) return;

            if (SessionManager.CurrentUser != null && SessionManager.CurrentUser.Id == _selectedUserForEdit.Id)
            {
                MessageBox.Show("Không thể tự xóa tài khoản Quản trị viên đang đăng nhập!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa tài khoản '{_selectedUserForEdit.FullName}' không?",
                "Xác nhận xóa tài khoản",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                QuizService.DeleteUser(_selectedUserForEdit.Id);
                ResetUserForm();
                ShowUserEditorPanel(false);
                RefreshDashboardData();
            }
        }

        // ==================== RESULTS & ANALYTICS EXPORT ====================

        private void BtnExportResultsCsv_Click(object sender, RoutedEventArgs e)
        {
            var history = QuizService.GetExamHistory();
            if (history.Count == 0)
            {
                MessageBox.Show("Hiện chưa có dữ liệu lịch sử thi để xuất báo cáo!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Xuất báo cáo kết quả thi ra CSV/Excel",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"BaoCaoKetQuaThi_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    QuizService.ExportHistoryToCsv(dlg.FileName, history);
                    MessageBox.Show($"Đã xuất báo cáo kết quả thi thành công tại:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất báo cáo: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử thi của học sinh không?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                QuizService.ClearExamHistory();
                RefreshDashboardData();
            }
        }

        private void BtnOpenStudentView_Click(object sender, RoutedEventArgs e)
        {
            var studentWin = new MainWindow();
            studentWin.Show();
            Close();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.CurrentUser = null;
            var loginWin = new LoginWindow();
            loginWin.Show();
            Close();
        }
    }
}
