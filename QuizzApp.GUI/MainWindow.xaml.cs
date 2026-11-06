using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using QuizzApp.BLL;

namespace QuizzApp.GUI
{
    public partial class MainWindow : Window
    {
        private List<Exam> _allExams = new List<Exam>();
        private bool _onlySavedMode = false;
        private AttemptRecord _pendingAttempt = null;
        private DispatcherTimer _scheduleRefreshTimer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAppData();
            StartScheduleRefreshTimer();
            Closed += (s, e) => _scheduleRefreshTimer?.Stop();
        }

        private void StartScheduleRefreshTimer()
        {
            _scheduleRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _scheduleRefreshTimer.Tick += (s, e) =>
            {
                if (panelRealExamsView != null && panelRealExamsView.Visibility == Visibility.Visible)
                {
                    LoadExams();
                }
            };
            _scheduleRefreshTimer.Start();
        }

        private void InitializeAppData()
        {
            try
            {
                QuizService.InitializeDatabase();
                if (SessionManager.CurrentUser != null)
                {
                    txtSidebarUser.Text = SessionManager.CurrentUser.FullName;
                    txtSidebarRole.Text = SessionManager.CurrentUser.RoleDisplay;
                }

                LoadSubjectFilters();
                LoadExams();
                UpdateHeaderStatsBadge();
                CheckForActiveAttempt();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSubjectFilters()
        {
            var subjects = QuizService.GetAllSubjects();
            var filterItems = new List<Subject>
            {
                new Subject { Id = 0, Name = "Tất cả môn học" }
            };
            filterItems.AddRange(subjects);
            cboSubjectFilter.ItemsSource = filterItems;
            cboSubjectFilter.SelectedIndex = 0;
        }

        private void CheckForActiveAttempt()
        {
            int uid = SessionManager.CurrentUser?.Id ?? 0;
            _pendingAttempt = QuizService.GetActiveAttemptForUser(uid);
            bool onExamTabs = (panelRealExamsView != null && panelRealExamsView.Visibility == Visibility.Visible) ||
                              (panelPracticeExamsView != null && panelPracticeExamsView.Visibility == Visibility.Visible);

            if (_pendingAttempt != null && onExamTabs)
            {
                bannerResumeAttempt.Visibility = Visibility.Visible;
                txtResumeAttemptInfo.Text = $"Đề thi: {_pendingAttempt.ExamTitle}";
            }
            else
            {
                bannerResumeAttempt.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnResumeAttempt_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAttempt == null) return;
            var exam = _allExams.FirstOrDefault(x => x.Id == _pendingAttempt.ExamId);
            if (exam == null)
            {
                QuizService.AbandonInProgressAttempt(_pendingAttempt.Id);
                CheckForActiveAttempt();
                return;
            }

            var questions = QuizService.GetQuestionsByExamId(exam.Id);
            var quizWin = new QuizWindow(exam, questions, _pendingAttempt)
            {
                Owner = this
            };
            quizWin.ShowDialog();

            LoadExams();
            CheckForActiveAttempt();
            UpdateHeaderStatsBadge();
            if (quizWin.ExamCompleted)
            {
                BtnNavHistory_Click(null, null);
            }
        }

        private void BtnDiscardAttempt_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAttempt != null)
            {
                QuizService.AbandonInProgressAttempt(_pendingAttempt.Id);
                CheckForActiveAttempt();
            }
        }

        private void LoadExams()
        {
            _allExams = QuizService.GetAllExams();
            BindRealExams();
            ApplyPracticeAndSavedFilter();
        }

        private void BindRealExams()
        {
            if (itemsRealExams == null) return;

            int uid = SessionManager.CurrentUser?.Id ?? 0;
            var completedExamIds = QuizService.GetCompletedExamIdsForUser(uid);

            var realExams = _allExams
                .Where(x => !x.IsPracticeMode && x.IsAvailableForStudent && !completedExamIds.Contains(x.Id))
                .ToList();

            itemsRealExams.ItemsSource = realExams;
            if (borderEmptyRealExams != null)
            {
                borderEmptyRealExams.Visibility = realExams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ApplyPracticeAndSavedFilter()
        {
            if (itemsPracticeExams == null) return;

            string keyword = txtSearch?.Text?.Trim().ToLower() ?? "";
            int selectedSubjectId = (cboSubjectFilter?.SelectedItem as Subject)?.Id ?? 0;

            IEnumerable<Exam> filtered = _allExams;

            if (_onlySavedMode)
            {
                filtered = filtered.Where(x => x.IsPracticeMode && x.IsSaved);
            }
            else
            {
                filtered = filtered.Where(x => x.IsPracticeMode);
            }

            if (selectedSubjectId > 0)
            {
                filtered = filtered.Where(x => x.SubjectId == selectedSubjectId);
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                filtered = filtered.Where(x =>
                    x.Title.ToLower().Contains(keyword) ||
                    x.Description.ToLower().Contains(keyword) ||
                    x.SubjectDisplay.ToLower().Contains(keyword));
            }

            var resultList = filtered.ToList();
            itemsPracticeExams.ItemsSource = resultList;
            if (txtEmptyPracticeExams != null)
            {
                txtEmptyPracticeExams.Text = _onlySavedMode
                    ? "Bạn chưa lưu đề thi nào phù hợp."
                    : "Không tìm thấy đề luyện tập nào phù hợp.";
                txtEmptyPracticeExams.Visibility = resultList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateHeaderStatsBadge()
        {
            int? uid = SessionManager.CurrentUser?.Id;
            var history = QuizService.GetExamHistory(uid);
            string userPrefix = SessionManager.CurrentUser != null ? $"{SessionManager.CurrentUser.FullName} • " : "";
            txtQuickStatsBadge.Text = $"{userPrefix}Đã làm: {history.Count} bài";
        }

        private void SetActiveNavButton(Button activeBtn)
        {
            Button[] navButtons = { btnNavRealExams, btnNavPracticeExams, btnNavSaved, btnNavHistory };
            foreach (var btn in navButtons)
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

        private void BtnNavRealExams_Click(object sender, RoutedEventArgs e)
        {
            _onlySavedMode = false;
            SetActiveNavButton(btnNavRealExams);

            txtHeaderTitle.Text = "Phòng thi chính thức";

            panelRealExamsView.Visibility = Visibility.Visible;
            panelPracticeExamsView.Visibility = Visibility.Collapsed;
            panelHistoryView.Visibility = Visibility.Collapsed;

            CheckForActiveAttempt();
            LoadExams();
        }

        private void BtnNavPracticeExams_Click(object sender, RoutedEventArgs e)
        {
            _onlySavedMode = false;
            SetActiveNavButton(btnNavPracticeExams);

            txtHeaderTitle.Text = "Thư viện đề Luyện tập";
            txtPracticeSectionHeading.Text = "Danh sách đề luyện tập";
            bannerPracticeWelcome.Visibility = Visibility.Visible;

            panelRealExamsView.Visibility = Visibility.Collapsed;
            panelPracticeExamsView.Visibility = Visibility.Visible;
            panelHistoryView.Visibility = Visibility.Collapsed;

            CheckForActiveAttempt();
            LoadExams();
        }

        private void BtnNavSaved_Click(object sender, RoutedEventArgs e)
        {
            _onlySavedMode = true;
            SetActiveNavButton(btnNavSaved);

            txtHeaderTitle.Text = "Đề thi đã lưu";
            txtPracticeSectionHeading.Text = "Đề đã lưu của bạn";
            bannerPracticeWelcome.Visibility = Visibility.Collapsed;

            panelRealExamsView.Visibility = Visibility.Collapsed;
            panelPracticeExamsView.Visibility = Visibility.Visible;
            panelHistoryView.Visibility = Visibility.Collapsed;

            CheckForActiveAttempt();
            LoadExams();
        }

        private void BtnNavHistory_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNavButton(btnNavHistory);

            txtHeaderTitle.Text = "Lịch sử làm bài";

            panelRealExamsView.Visibility = Visibility.Collapsed;
            panelPracticeExamsView.Visibility = Visibility.Collapsed;
            panelHistoryView.Visibility = Visibility.Visible;
            bannerResumeAttempt.Visibility = Visibility.Collapsed;

            LoadHistoryView();
        }

        private void LoadHistoryView()
        {
            int? uid = SessionManager.CurrentUser?.Id;
            var history = QuizService.GetExamHistory(uid);
            itemsHistory.ItemsSource = history;
            borderEmptyHistory.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            btnClearHistory.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            txtStatTotalTests.Text = history.Count.ToString();
            if (history.Count > 0)
            {
                txtStatAvgScore.Text = $"{history.Average(h => h.Score):0.0}";
                txtStatBestScore.Text = $"{history.Max(h => h.Score):0.0}";
            }
            else
            {
                txtStatAvgScore.Text = "0.0";
                txtStatBestScore.Text = "0.0";
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyPracticeAndSavedFilter();
        }

        private void CboSubjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyPracticeAndSavedFilter();
        }

        private void BtnStartExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam)
            {
                var questions = QuizService.GetQuestionsByExamId(exam.Id);
                if (questions.Count == 0)
                {
                    MessageBox.Show("Đề thi này hiện chưa có câu hỏi nào!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var quizWin = new QuizWindow(exam, questions)
                {
                    Owner = this
                };
                quizWin.ShowDialog();

                LoadExams();
                CheckForActiveAttempt();
                UpdateHeaderStatsBadge();
                if (quizWin.ExamCompleted)
                {
                    BtnNavHistory_Click(null, null);
                }
            }
        }

        private void BtnToggleSaveExam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Exam exam && exam.IsPracticeMode)
            {
                exam.IsSaved = !exam.IsSaved;
                QuizService.ToggleSaveExam(exam.Id, exam.IsSaved);
                if (_onlySavedMode)
                {
                    ApplyPracticeAndSavedFilter();
                }
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử làm bài của mình không?",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                int? uid = SessionManager.CurrentUser != null ? (int?)SessionManager.CurrentUser.Id : null;
                QuizService.ClearExamHistory(uid);
                LoadHistoryView();
                UpdateHeaderStatsBadge();
            }
        }

        private void BtnBackToAdmin_Click(object sender, RoutedEventArgs e)
        {
            var adminWin = new AdminWindow();
            adminWin.Show();
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
